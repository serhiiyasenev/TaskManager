using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace DAL.Migrations;

// Forward repair: older RemoveLegacySeededAdmin was not discoverable. Keep
// users, password hashes, roles and all task/project foreign keys intact.
public partial class SecureLegacyAccounts : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.Sql(
            """
            DECLARE @demoHash nvarchar(4000) = N'AQAAAAIAAYagAAAAEOjCnYyBCMuMtY1qgTRLchq6EiTni+db7W81eRUOtKM3y49VHFJoWToNDjvD2OeHJQ==';

            -- Match the historical seed fingerprint, never merely an ID/email.
            -- Changed human-created credentials/stamps are left untouched.
            DELETE [ur]
            FROM [AspNetUserRoles] AS [ur]
            INNER JOIN [AspNetUsers] AS [u] ON [u].[Id] = [ur].[UserId]
            INNER JOIN [AspNetRoles] AS [r] ON [r].[Id] = [ur].[RoleId]
            WHERE [r].[NormalizedName] = N'ADMIN'
              AND [u].[PasswordHash] = @demoHash
              AND [u].[SecurityStamp] IN (
                  N'55555555-5555-5555-5555-555555555555',
                  N'11111111-1111-1111-1111-111111111111',
                  N'33333333-3333-3333-3333-333333333333');

            UPDATE [AspNetUsers]
            SET [LockoutEnabled] = 1,
                [LockoutEnd] = CAST('9999-12-30T00:00:00+00:00' AS datetimeoffset),
                [EmailConfirmed] = 0,
                [SecurityStamp] = CONVERT(nvarchar(36), NEWID()),
                [ConcurrencyStamp] = CONVERT(nvarchar(36), NEWID())
            WHERE [PasswordHash] = @demoHash
              AND [SecurityStamp] IN (
                  N'55555555-5555-5555-5555-555555555555',
                  N'11111111-1111-1111-1111-111111111111',
                  N'33333333-3333-3333-3333-333333333333');
            """);
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        // Application rollback may use the previous image with these safe
        // accounts. Never restore known passwords or seeded admin privileges.
        throw new InvalidOperationException(
            "SecureLegacyAccounts is a forward security repair. Roll application revisions forward or restore a reviewed backup; known seed credentials/privileges cannot be re-enabled by a migration downgrade.");
    }
}
