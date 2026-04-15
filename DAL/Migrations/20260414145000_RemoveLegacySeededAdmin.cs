using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace DAL.Migrations
{
    /// <inheritdoc />
    public partial class RemoveLegacySeededAdmin : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(
                """
                IF OBJECT_ID(N'[dbo].[__LegacyAdminTaskReassignments]', N'U') IS NULL
                BEGIN
                    CREATE TABLE [dbo].[__LegacyAdminTaskReassignments](
                        [TaskId] INT NOT NULL PRIMARY KEY
                    );
                END;

                IF OBJECT_ID(N'[dbo].[__LegacyAdminProjectReassignments]', N'U') IS NULL
                BEGIN
                    CREATE TABLE [dbo].[__LegacyAdminProjectReassignments](
                        [ProjectId] INT NOT NULL PRIMARY KEY
                    );
                END;

                INSERT INTO [dbo].[__LegacyAdminTaskReassignments]([TaskId])
                SELECT [t].[Id]
                FROM [Tasks] AS [t]
                WHERE [t].[PerformerId] = 11
                  AND NOT EXISTS (
                      SELECT 1
                      FROM [dbo].[__LegacyAdminTaskReassignments] AS [r]
                      WHERE [r].[TaskId] = [t].[Id]
                  );

                INSERT INTO [dbo].[__LegacyAdminProjectReassignments]([ProjectId])
                SELECT [p].[Id]
                FROM [Projects] AS [p]
                WHERE [p].[AuthorId] = 11
                  AND NOT EXISTS (
                      SELECT 1
                      FROM [dbo].[__LegacyAdminProjectReassignments] AS [r]
                      WHERE [r].[ProjectId] = [p].[Id]
                  );

                UPDATE [Tasks]
                SET [PerformerId] = 10
                WHERE [Id] IN (SELECT [TaskId] FROM [dbo].[__LegacyAdminTaskReassignments]);

                UPDATE [Projects]
                SET [AuthorId] = 10
                WHERE [Id] IN (SELECT [ProjectId] FROM [dbo].[__LegacyAdminProjectReassignments]);
                """);

            migrationBuilder.DeleteData(
                table: "AspNetUserRoles",
                keyColumns: new[] { "UserId", "RoleId" },
                keyValues: new object[] { 11, 1 });

            migrationBuilder.DeleteData(
                table: "AspNetUsers",
                keyColumn: "Id",
                keyValue: 11);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.InsertData(
                table: "AspNetUsers",
                columns: new[] { "Id", "AccessFailedCount", "BirthDay", "ConcurrencyStamp", "Email", "EmailConfirmed", "FirstName", "LastName", "LockoutEnabled", "LockoutEnd", "NormalizedEmail", "NormalizedUserName", "PasswordHash", "PhoneNumber", "PhoneNumberConfirmed", "RegisteredAt", "SecurityStamp", "TeamId", "TwoFactorEnabled", "UserName" },
                values: new object[] { 11, 0, new DateTime(1985, 1, 1, 0, 0, 0, 0, DateTimeKind.Unspecified), "22222222-2222-2222-2222-222222222222", "admin@example.com", true, "System", "Admin", false, null, "ADMIN@EXAMPLE.COM", "ADMIN", "AQAAAAIAAYagAAAAEOjCnYyBCMuMtY1qgTRLchq6EiTni+db7W81eRUOtKM3y49VHFJoWToNDjvD2OeHJQ==", null, false, new DateTime(2025, 7, 22, 0, 0, 0, 0, DateTimeKind.Utc), "11111111-1111-1111-1111-111111111111", null, false, "admin" });

            migrationBuilder.InsertData(
                table: "AspNetUserRoles",
                columns: new[] { "UserId", "RoleId" },
                values: new object[] { 11, 1 });

            migrationBuilder.Sql(
                """
                IF OBJECT_ID(N'[dbo].[__LegacyAdminTaskReassignments]', N'U') IS NOT NULL
                BEGIN
                    UPDATE [t]
                    SET [t].[PerformerId] = 11
                    FROM [Tasks] AS [t]
                    INNER JOIN [dbo].[__LegacyAdminTaskReassignments] AS [r] ON [r].[TaskId] = [t].[Id]
                    WHERE [t].[PerformerId] = 10;
                END;

                IF OBJECT_ID(N'[dbo].[__LegacyAdminProjectReassignments]', N'U') IS NOT NULL
                BEGIN
                    UPDATE [p]
                    SET [p].[AuthorId] = 11
                    FROM [Projects] AS [p]
                    INNER JOIN [dbo].[__LegacyAdminProjectReassignments] AS [r] ON [r].[ProjectId] = [p].[Id]
                    WHERE [p].[AuthorId] = 10;
                END;

                IF OBJECT_ID(N'[dbo].[__LegacyAdminTaskReassignments]', N'U') IS NOT NULL
                BEGIN
                    DROP TABLE [dbo].[__LegacyAdminTaskReassignments];
                END;

                IF OBJECT_ID(N'[dbo].[__LegacyAdminProjectReassignments]', N'U') IS NOT NULL
                BEGIN
                    DROP TABLE [dbo].[__LegacyAdminProjectReassignments];
                END;
                """);
        }
    }
}
