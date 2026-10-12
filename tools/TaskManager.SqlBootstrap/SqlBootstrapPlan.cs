using System.Text.Json;
using System.Text.RegularExpressions;

namespace TaskManager.SqlBootstrap;

public enum SqlWorkloadRole { Api, Notifier, Migration }
public sealed record SqlBootstrapPrincipal(string Name, Guid ObjectId, Guid ClientId, SqlWorkloadRole Role);

public static class SqlBootstrapPlan
{
    private static readonly string[] ApiTables = ["AspNetUsers", "AspNetRoles", "AspNetUserRoles", "AspNetUserClaims",
        "AspNetRoleClaims", "AspNetUserLogins", "AspNetUserTokens", "Teams", "TeamInvitations", "Projects", "Tasks",
        "ExecutedTasks", "NotificationOutbox", "AiTaskDrafts", "AiTaskDraftItems", "AiTaskMaterializations",
        "AiTaskCommitReceipts", "AiDailyUsage"];
    private static readonly string[] NotifierReadTables = ["AspNetUsers", "AspNetRoles", "AspNetUserRoles", "AspNetUserClaims",
        "AspNetRoleClaims", "AspNetUserLogins", "AspNetUserTokens", "Teams", "Projects", "Tasks", "ExecutedTasks"];
    private static readonly string[] RetentionTables = ["AiTaskDrafts", "AiTaskDraftItems", "AiTaskMaterializations", "AiTaskCommitReceipts", "AiDailyUsage"];

    public static IReadOnlyList<SqlBootstrapPrincipal> ParseUsers(string json)
    {
        if (string.IsNullOrWhiteSpace(json) || System.Text.Encoding.UTF8.GetByteCount(json) > 16_384)
            throw Invalid();
        try
        {
            using var document = JsonDocument.Parse(json, new JsonDocumentOptions { MaxDepth = 4 });
            if (document.RootElement.ValueKind != JsonValueKind.Array || document.RootElement.GetArrayLength() != 3) throw Invalid();
            var users = new List<SqlBootstrapPrincipal>();
            foreach (var item in document.RootElement.EnumerateArray())
            {
                if (item.ValueKind != JsonValueKind.Object) throw Invalid();
                var keys = new HashSet<string>(StringComparer.Ordinal);
                foreach (var property in item.EnumerateObject())
                    if (!keys.Add(property.Name) || property.Name is not ("name" or "objectId" or "clientId" or "role")
                        || property.Value.ValueKind != JsonValueKind.String) throw Invalid();
                if (keys.Count != 4 || !Guid.TryParse(item.GetProperty("objectId").GetString(), out var objectId)
                    || !Guid.TryParse(item.GetProperty("clientId").GetString(), out var clientId)) throw Invalid();
                var role = item.GetProperty("role").GetString() switch
                { "Api" => SqlWorkloadRole.Api, "Notifier" => SqlWorkloadRole.Notifier, "Migration" => SqlWorkloadRole.Migration, _ => throw Invalid() };
                var principal = new SqlBootstrapPrincipal(item.GetProperty("name").GetString()!, objectId, clientId, role);
                Validate(principal); users.Add(principal);
            }
            if (users.Select(u => u.Name).Distinct(StringComparer.OrdinalIgnoreCase).Count() != 3
                || users.Select(u => u.ObjectId).Distinct().Count() != 3
                || users.Select(u => u.ClientId).Distinct().Count() != 3 || users.Select(u => u.Role).Distinct().Count() != 3) throw Invalid();
            return users.AsReadOnly();
        }
        catch (JsonException) { throw Invalid(); }
    }

    public static string CreatePrincipalSql(SqlBootstrapPrincipal principal)
    {
        Validate(principal);
        // Current CREATE USER section K specifies the client ID for a managed
        // identity SID. No Graph lookup/Directory Readers grant is required.
        // Live MI connection is still a required deployment verification gate.
        var sid = "0x" + Convert.ToHexString(principal.ClientId.ToByteArray());
        return $"""
            IF EXISTS (SELECT 1 FROM sys.database_principals WHERE name=N'{principal.Name}'
                AND (type<>'E' OR sid<>{sid}))
                THROW 50001, 'Existing SQL identity does not match the configured managed identity.', 1;
            IF NOT EXISTS (SELECT 1 FROM sys.database_principals WHERE name=N'{principal.Name}')
                CREATE USER [{principal.Name}] WITH SID = {sid}, TYPE = E;
            """;
    }

    public static string GrantSql(SqlBootstrapPrincipal principal, bool schemaReady)
    {
        Validate(principal);
        var role = RoleName(principal.Role);
        var statements = new List<string>
        {
            $"IF EXISTS (SELECT 1 FROM sys.database_principals WHERE name=N'{role}' AND (type<>'R' OR owning_principal_id<>DATABASE_PRINCIPAL_ID('dbo'))) THROW 50003, 'Existing workload role has unexpected ownership or type.', 1;",
            $"IF DATABASE_PRINCIPAL_ID(N'{role}') IS NULL CREATE ROLE [{role}] AUTHORIZATION dbo;",
            $"IF IS_ROLEMEMBER(N'{role}',N'{principal.Name}')=0 ALTER ROLE [{role}] ADD MEMBER [{principal.Name}];"
        };
        if (principal.Role == SqlWorkloadRole.Migration)
        {
            statements.Add($"GRANT CREATE TABLE TO [{role}];");
            statements.Add($"GRANT SELECT, INSERT, UPDATE, DELETE, ALTER, REFERENCES ON SCHEMA::dbo TO [{role}];");
        }
        else if (schemaReady)
        {
            if (principal.Role == SqlWorkloadRole.Api)
            {
                foreach (var table in ApiTables) statements.Add(TableGrant(table, "SELECT, INSERT, UPDATE, DELETE", role));
                statements.Add(TableGrant("UserNotifications", "SELECT", role));
                statements.Add($"GRANT UPDATE ON OBJECT::dbo.UserNotifications (ReadAtUtc) TO [{role}];");
            }
            else
            {
                foreach (var table in NotifierReadTables) statements.Add(TableGrant(table, "SELECT", role));
                statements.Add($"GRANT UPDATE ON OBJECT::dbo.Tasks (Version, ReminderSentAt, ReminderNotificationId, EscalationSentAt, EscalationNotificationId) TO [{role}];");
                statements.Add(TableGrant("NotificationOutbox", "SELECT, INSERT, UPDATE", role));
                statements.Add(TableGrant("NotificationProcessingMarkers", "SELECT, INSERT, UPDATE", role));
                statements.Add(TableGrant("UserNotifications", "SELECT, INSERT, DELETE", role));
                foreach (var table in RetentionTables) statements.Add(TableGrant(table, "SELECT, UPDATE, DELETE", role));
            }
            statements.Add($"DENY INSERT, UPDATE, DELETE ON OBJECT::dbo.__EFMigrationsHistory TO [{role}];");
        }
        return string.Join('\n', statements);
    }

    public static string ExcessAuthoritySql(SqlBootstrapPrincipal principal)
    {
        Validate(principal);
        var runtimeChecks = "";
        if (principal.Role != SqlWorkloadRole.Migration)
        {
            var allowed = new List<(string Table, string Permission)>();
            void Add(string table, params string[] permissions) => allowed.AddRange(permissions.Select(permission => (table, permission)));
            if (principal.Role == SqlWorkloadRole.Api)
            {
                foreach (var table in ApiTables) Add(table, "SELECT", "INSERT", "UPDATE", "DELETE");
                Add("UserNotifications", "SELECT");
            }
            else
            {
                foreach (var table in NotifierReadTables) Add(table, "SELECT");
                Add("NotificationOutbox", "SELECT", "INSERT", "UPDATE");
                Add("NotificationProcessingMarkers", "SELECT", "INSERT", "UPDATE");
                Add("UserNotifications", "SELECT", "INSERT", "DELETE");
                foreach (var table in RetentionTables) Add(table, "SELECT", "UPDATE", "DELETE");
            }
            var rows = string.Join(',', allowed.Select(pair => $"(N'{pair.Table}',N'{pair.Permission}')"));
            var columns = principal.Role == SqlWorkloadRole.Api
                ? "(N'UserNotifications',N'ReadAtUtc',N'UPDATE')"
                : string.Join(',', new[] { "Version", "ReminderSentAt", "ReminderNotificationId", "EscalationSentAt", "EscalationNotificationId" }
                    .Select(column => $"(N'Tasks',N'{column}',N'UPDATE')"));
            runtimeChecks = $"""
                OR EXISTS (SELECT 1 FROM (VALUES ('CREATE TABLE'),('CREATE VIEW'),('CREATE PROCEDURE'),('CREATE FUNCTION'),('ALTER'),('BACKUP DATABASE'),('BACKUP LOG')) p(permission)
                    WHERE HAS_PERMS_BY_NAME(DB_NAME(),'DATABASE',p.permission)=1)
                OR EXISTS (SELECT 1 FROM sys.tables t JOIN sys.schemas s ON s.schema_id=t.schema_id
                    CROSS JOIN (VALUES ('SELECT'),('INSERT'),('UPDATE'),('DELETE'),('ALTER'),('CONTROL'),('REFERENCES'),('TAKE OWNERSHIP')) p(permission)
                    WHERE t.is_ms_shipped=0 AND HAS_PERMS_BY_NAME(QUOTENAME(s.name)+'.'+QUOTENAME(t.name),'OBJECT',p.permission)=1
                    AND NOT (s.name='dbo' AND (EXISTS (SELECT 1 FROM (VALUES {rows}) a(name,permission) WHERE a.name=t.name AND a.permission=p.permission)
                        OR EXISTS (SELECT 1 FROM (VALUES {columns}) a(name,columnName,permission) WHERE a.name=t.name AND a.permission=p.permission))))
                OR EXISTS (SELECT 1 FROM sys.tables t JOIN sys.schemas s ON s.schema_id=t.schema_id JOIN sys.columns c ON c.object_id=t.object_id
                    CROSS JOIN (VALUES ('SELECT'),('UPDATE'),('REFERENCES')) p(permission)
                    WHERE t.is_ms_shipped=0 AND HAS_PERMS_BY_NAME(QUOTENAME(s.name)+'.'+QUOTENAME(t.name),'OBJECT',p.permission,c.name,'COLUMN')=1
                    AND NOT (s.name='dbo' AND (EXISTS (SELECT 1 FROM (VALUES {rows}) a(name,permission) WHERE a.name=t.name AND a.permission=p.permission)
                        OR EXISTS (SELECT 1 FROM (VALUES {columns}) a(name,columnName,permission) WHERE a.name=t.name AND a.columnName=c.name AND a.permission=p.permission))))
                OR EXISTS (SELECT 1 FROM sys.objects o JOIN sys.schemas s ON s.schema_id=o.schema_id
                    CROSS JOIN (VALUES ('SELECT'),('INSERT'),('UPDATE'),('DELETE'),('EXECUTE'),('ALTER'),('CONTROL')) p(permission)
                    WHERE o.is_ms_shipped=0 AND o.type IN ('V','P','PC','FN','IF','TF') AND HAS_PERMS_BY_NAME(QUOTENAME(s.name)+'.'+QUOTENAME(o.name),'OBJECT',p.permission)=1)
                """;
        }
        return $"""
            SELECT CASE WHEN IS_ROLEMEMBER('db_owner')=1
                OR IS_ROLEMEMBER('db_securityadmin')=1
                OR EXISTS (SELECT 1 FROM (VALUES ('CONTROL'),('ALTER ANY USER'),('ALTER ANY ROLE'),('CREATE ROLE'),('ALTER ANY SCHEMA'),('CREATE SCHEMA'),('IMPERSONATE ANY USER')) p(permission)
                    WHERE HAS_PERMS_BY_NAME(DB_NAME(),'DATABASE',p.permission)=1)
                {runtimeChecks}
                THEN 1 ELSE 0 END;
            """;
    }

    public static string RoleName(SqlWorkloadRole role) => role switch
    { SqlWorkloadRole.Api => "taskmanager_api", SqlWorkloadRole.Notifier => "taskmanager_notifier", SqlWorkloadRole.Migration => "taskmanager_migration", _ => throw Invalid() };

    private static string TableGrant(string table, string permissions, string role) =>
        $"IF OBJECT_ID(N'dbo.{table}',N'U') IS NULL THROW 50002, 'Required TaskManager schema is missing.', 1; GRANT {permissions} ON OBJECT::dbo.[{table}] TO [{role}];";

    private static void Validate(SqlBootstrapPrincipal principal)
    {
        if (principal is null || string.IsNullOrEmpty(principal.Name)
            || !Regex.IsMatch(principal.Name, "^[a-z][a-z0-9_-]{2,99}$", RegexOptions.CultureInvariant)
            || principal.Name is "dbo" or "guest" or "sys" or "information_schema"
            || principal.ObjectId == Guid.Empty || principal.ClientId == Guid.Empty
            || !Enum.IsDefined(principal.Role)) throw Invalid();
    }

    private static InvalidOperationException Invalid() => new("SQL bootstrap requires three distinct, bounded managed identity mappings with explicit workload roles.");
}
