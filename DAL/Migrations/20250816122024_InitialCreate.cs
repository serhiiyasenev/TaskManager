using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

#pragma warning disable CA1814 // Prefer jagged arrays over multidimensional

namespace DAL.Migrations
{
    /// <inheritdoc />
    public partial class InitialCreate : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_Projects_Users_AuthorId",
                table: "Projects");

            migrationBuilder.DropForeignKey(
                name: "FK_Tasks_Users_PerformerId",
                table: "Tasks");

            migrationBuilder.DropForeignKey(
                name: "FK_Users_Teams_TeamId",
                table: "Users");

            migrationBuilder.DropIndex(
                name: "IX_Projects_TeamId",
                table: "Projects");

            migrationBuilder.DropPrimaryKey(
                name: "PK_Users",
                table: "Users");

            migrationBuilder.RenameTable(
                name: "Users",
                newName: "AspNetUsers");

            migrationBuilder.RenameIndex(
                name: "IX_Users_TeamId",
                table: "AspNetUsers",
                newName: "IX_AspNetUsers_TeamId");

            migrationBuilder.AlterColumn<string>(
                name: "Name",
                table: "Teams",
                type: "nvarchar(100)",
                maxLength: 100,
                nullable: false,
                oldClrType: typeof(string),
                oldType: "nvarchar(max)");

            migrationBuilder.AlterColumn<string>(
                name: "Name",
                table: "Tasks",
                type: "nvarchar(100)",
                maxLength: 100,
                nullable: false,
                oldClrType: typeof(string),
                oldType: "nvarchar(max)");

            migrationBuilder.AlterColumn<string>(
                name: "Description",
                table: "Tasks",
                type: "nvarchar(1000)",
                maxLength: 1000,
                nullable: false,
                oldClrType: typeof(string),
                oldType: "nvarchar(max)");

            migrationBuilder.AlterColumn<string>(
                name: "Name",
                table: "Projects",
                type: "nvarchar(100)",
                maxLength: 100,
                nullable: false,
                oldClrType: typeof(string),
                oldType: "nvarchar(max)");

            migrationBuilder.AlterColumn<string>(
                name: "Description",
                table: "Projects",
                type: "nvarchar(1000)",
                maxLength: 1000,
                nullable: false,
                oldClrType: typeof(string),
                oldType: "nvarchar(max)");

            migrationBuilder.AlterColumn<string>(
                name: "Email",
                table: "AspNetUsers",
                type: "nvarchar(256)",
                maxLength: 256,
                nullable: true,
                oldClrType: typeof(string),
                oldType: "nvarchar(max)");

            migrationBuilder.AddColumn<int>(
                name: "AccessFailedCount",
                table: "AspNetUsers",
                type: "int",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<string>(
                name: "ConcurrencyStamp",
                table: "AspNetUsers",
                type: "nvarchar(max)",
                nullable: true);

            migrationBuilder.AddColumn<bool>(
                name: "EmailConfirmed",
                table: "AspNetUsers",
                type: "bit",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<bool>(
                name: "LockoutEnabled",
                table: "AspNetUsers",
                type: "bit",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "LockoutEnd",
                table: "AspNetUsers",
                type: "datetimeoffset",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "NormalizedEmail",
                table: "AspNetUsers",
                type: "nvarchar(256)",
                maxLength: 256,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "NormalizedUserName",
                table: "AspNetUsers",
                type: "nvarchar(256)",
                maxLength: 256,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "PasswordHash",
                table: "AspNetUsers",
                type: "nvarchar(max)",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "PhoneNumber",
                table: "AspNetUsers",
                type: "nvarchar(max)",
                nullable: true);

            migrationBuilder.AddColumn<bool>(
                name: "PhoneNumberConfirmed",
                table: "AspNetUsers",
                type: "bit",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<string>(
                name: "SecurityStamp",
                table: "AspNetUsers",
                type: "nvarchar(max)",
                nullable: true);

            migrationBuilder.AddColumn<bool>(
                name: "TwoFactorEnabled",
                table: "AspNetUsers",
                type: "bit",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<string>(
                name: "UserName",
                table: "AspNetUsers",
                type: "nvarchar(256)",
                maxLength: 256,
                nullable: true);

            migrationBuilder.AddPrimaryKey(
                name: "PK_AspNetUsers",
                table: "AspNetUsers",
                column: "Id");

            migrationBuilder.CreateTable(
                name: "AspNetRoles",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    Name = table.Column<string>(type: "nvarchar(256)", maxLength: 256, nullable: true),
                    NormalizedName = table.Column<string>(type: "nvarchar(256)", maxLength: 256, nullable: true),
                    ConcurrencyStamp = table.Column<string>(type: "nvarchar(max)", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AspNetRoles", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "AspNetUserClaims",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    UserId = table.Column<int>(type: "int", nullable: false),
                    ClaimType = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    ClaimValue = table.Column<string>(type: "nvarchar(max)", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AspNetUserClaims", x => x.Id);
                    table.ForeignKey(
                        name: "FK_AspNetUserClaims_AspNetUsers_UserId",
                        column: x => x.UserId,
                        principalTable: "AspNetUsers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "AspNetUserLogins",
                columns: table => new
                {
                    LoginProvider = table.Column<string>(type: "nvarchar(450)", nullable: false),
                    ProviderKey = table.Column<string>(type: "nvarchar(450)", nullable: false),
                    ProviderDisplayName = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    UserId = table.Column<int>(type: "int", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AspNetUserLogins", x => new { x.LoginProvider, x.ProviderKey });
                    table.ForeignKey(
                        name: "FK_AspNetUserLogins_AspNetUsers_UserId",
                        column: x => x.UserId,
                        principalTable: "AspNetUsers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "AspNetUserTokens",
                columns: table => new
                {
                    UserId = table.Column<int>(type: "int", nullable: false),
                    LoginProvider = table.Column<string>(type: "nvarchar(450)", nullable: false),
                    Name = table.Column<string>(type: "nvarchar(450)", nullable: false),
                    Value = table.Column<string>(type: "nvarchar(max)", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AspNetUserTokens", x => new { x.UserId, x.LoginProvider, x.Name });
                    table.ForeignKey(
                        name: "FK_AspNetUserTokens_AspNetUsers_UserId",
                        column: x => x.UserId,
                        principalTable: "AspNetUsers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "AspNetRoleClaims",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    RoleId = table.Column<int>(type: "int", nullable: false),
                    ClaimType = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    ClaimValue = table.Column<string>(type: "nvarchar(max)", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AspNetRoleClaims", x => x.Id);
                    table.ForeignKey(
                        name: "FK_AspNetRoleClaims_AspNetRoles_RoleId",
                        column: x => x.RoleId,
                        principalTable: "AspNetRoles",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "AspNetUserRoles",
                columns: table => new
                {
                    UserId = table.Column<int>(type: "int", nullable: false),
                    RoleId = table.Column<int>(type: "int", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AspNetUserRoles", x => new { x.UserId, x.RoleId });
                    table.ForeignKey(
                        name: "FK_AspNetUserRoles_AspNetRoles_RoleId",
                        column: x => x.RoleId,
                        principalTable: "AspNetRoles",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_AspNetUserRoles_AspNetUsers_UserId",
                        column: x => x.UserId,
                        principalTable: "AspNetUsers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.InsertData(
                table: "AspNetRoles",
                columns: new[] { "Id", "ConcurrencyStamp", "Name", "NormalizedName" },
                values: new object[] { 1, "77777777-7777-7777-7777-777777777777", "admin", "ADMIN" });

            migrationBuilder.UpdateData(
                table: "AspNetUsers",
                keyColumn: "Id",
                keyValue: 1,
                columns: new[] { "AccessFailedCount", "ConcurrencyStamp", "EmailConfirmed", "LockoutEnabled", "LockoutEnd", "NormalizedEmail", "NormalizedUserName", "PasswordHash", "PhoneNumber", "PhoneNumberConfirmed", "SecurityStamp", "TwoFactorEnabled", "UserName" },
                values: new object[] { 0, "66666666-6666-6666-6666-666666666666", true, false, null, "JOHN.A.DOE@GMAIL.COM", "JOHN.A", "AQAAAAIAAYagAAAAENb2U6m9bq0y7D8qg3Y0r8JqzY3v1kq1nJx3e2nqkQe3y5wB7wqk0pZr4h3Hc0c5w==", null, false, "55555555-5555-5555-5555-555555555555", false, "john.a" });

            migrationBuilder.UpdateData(
                table: "AspNetUsers",
                keyColumn: "Id",
                keyValue: 2,
                columns: new[] { "AccessFailedCount", "BirthDay", "ConcurrencyStamp", "Email", "EmailConfirmed", "FirstName", "LastName", "LockoutEnabled", "LockoutEnd", "NormalizedEmail", "NormalizedUserName", "PasswordHash", "PhoneNumber", "PhoneNumberConfirmed", "SecurityStamp", "TeamId", "TwoFactorEnabled", "UserName" },
                values: new object[] { 0, new DateTime(1990, 5, 10, 0, 0, 0, 0, DateTimeKind.Unspecified), "66666666-6666-6666-6666-666666666666", "kate.a@example.com", true, "Kate", "A", false, null, "KATE.A@EXAMPLE.COM", "KATE.A", "AQAAAAIAAYagAAAAENb2U6m9bq0y7D8qg3Y0r8JqzY3v1kq1nJx3e2nqkQe3y5wB7wqk0pZr4h3Hc0c5w==", null, false, "55555555-5555-5555-5555-555555555555", 1, false, "kate.a" });

            migrationBuilder.UpdateData(
                table: "AspNetUsers",
                keyColumn: "Id",
                keyValue: 3,
                columns: new[] { "AccessFailedCount", "BirthDay", "ConcurrencyStamp", "Email", "EmailConfirmed", "LastName", "LockoutEnabled", "LockoutEnd", "NormalizedEmail", "NormalizedUserName", "PasswordHash", "PhoneNumber", "PhoneNumberConfirmed", "SecurityStamp", "TeamId", "TwoFactorEnabled", "UserName" },
                values: new object[] { 0, new DateTime(1992, 2, 2, 0, 0, 0, 0, DateTimeKind.Unspecified), "66666666-6666-6666-6666-666666666666", "john.b.doe@gmail.com", true, "B", false, null, "JOHN.B.DOE@GMAIL.COM", "JOHN.B", "AQAAAAIAAYagAAAAENb2U6m9bq0y7D8qg3Y0r8JqzY3v1kq1nJx3e2nqkQe3y5wB7wqk0pZr4h3Hc0c5w==", null, false, "55555555-5555-5555-5555-555555555555", 2, false, "john.b" });

            migrationBuilder.UpdateData(
                table: "AspNetUsers",
                keyColumn: "Id",
                keyValue: 4,
                columns: new[] { "AccessFailedCount", "BirthDay", "ConcurrencyStamp", "Email", "EmailConfirmed", "FirstName", "LastName", "LockoutEnabled", "LockoutEnd", "NormalizedEmail", "NormalizedUserName", "PasswordHash", "PhoneNumber", "PhoneNumberConfirmed", "SecurityStamp", "TeamId", "TwoFactorEnabled", "UserName" },
                values: new object[] { 0, new DateTime(1991, 6, 15, 0, 0, 0, 0, DateTimeKind.Unspecified), "66666666-6666-6666-6666-666666666666", "kate.b@example.com", true, "Kate", "B", false, null, "KATE.B@EXAMPLE.COM", "KATE.B", "AQAAAAIAAYagAAAAENb2U6m9bq0y7D8qg3Y0r8JqzY3v1kq1nJx3e2nqkQe3y5wB7wqk0pZr4h3Hc0c5w==", null, false, "55555555-5555-5555-5555-555555555555", 2, false, "kate.b" });

            migrationBuilder.UpdateData(
                table: "AspNetUsers",
                keyColumn: "Id",
                keyValue: 5,
                columns: new[] { "AccessFailedCount", "BirthDay", "ConcurrencyStamp", "Email", "EmailConfirmed", "LastName", "LockoutEnabled", "LockoutEnd", "NormalizedEmail", "NormalizedUserName", "PasswordHash", "PhoneNumber", "PhoneNumberConfirmed", "SecurityStamp", "TeamId", "TwoFactorEnabled", "UserName" },
                values: new object[] { 0, new DateTime(1993, 3, 3, 0, 0, 0, 0, DateTimeKind.Unspecified), "66666666-6666-6666-6666-666666666666", "john.c.doe@gmail.com", true, "C", false, null, "JOHN.C.DOE@GMAIL.COM", "JOHN.C", "AQAAAAIAAYagAAAAENb2U6m9bq0y7D8qg3Y0r8JqzY3v1kq1nJx3e2nqkQe3y5wB7wqk0pZr4h3Hc0c5w==", null, false, "55555555-5555-5555-5555-555555555555", 3, false, "john.c" });

            migrationBuilder.InsertData(
                table: "AspNetUsers",
                columns: new[] { "Id", "AccessFailedCount", "BirthDay", "ConcurrencyStamp", "Email", "EmailConfirmed", "FirstName", "LastName", "LockoutEnabled", "LockoutEnd", "NormalizedEmail", "NormalizedUserName", "PasswordHash", "PhoneNumber", "PhoneNumberConfirmed", "RegisteredAt", "SecurityStamp", "TeamId", "TwoFactorEnabled", "UserName" },
                values: new object[,]
                {
                    { 6, 0, new DateTime(1989, 8, 20, 0, 0, 0, 0, DateTimeKind.Unspecified), "66666666-6666-6666-6666-666666666666", "kate.c@example.com", true, "Kate", "C", false, null, "KATE.C@EXAMPLE.COM", "KATE.C", "AQAAAAIAAYagAAAAENb2U6m9bq0y7D8qg3Y0r8JqzY3v1kq1nJx3e2nqkQe3y5wB7wqk0pZr4h3Hc0c5w==", null, false, new DateTime(2025, 7, 29, 0, 0, 0, 0, DateTimeKind.Utc), "55555555-5555-5555-5555-555555555555", 3, false, "kate.c" },
                    { 11, 0, new DateTime(1985, 1, 1, 0, 0, 0, 0, DateTimeKind.Unspecified), "22222222-2222-2222-2222-222222222222", "admin@example.com", true, "System", "Admin", false, null, "ADMIN@EXAMPLE.COM", "ADMIN", "AQAAAAIAAYagAAAAENb2U6m9bq0y7D8qg3Y0r8JqzY3v1kq1nJx3e2nqkQe3y5wB7wqk0pZr4h3Hc0c5w==", null, false, new DateTime(2025, 7, 22, 0, 0, 0, 0, DateTimeKind.Utc), "11111111-1111-1111-1111-111111111111", null, false, "admin" },
                    { 12, 0, new DateTime(2000, 1, 1, 0, 0, 0, 0, DateTimeKind.Unspecified), "44444444-4444-4444-4444-444444444444", "service.bot@example.com", true, "Service", "Bot", false, null, "SERVICE.BOT@EXAMPLE.COM", "SERVICE.BOT", "AQAAAAIAAYagAAAAENb2U6m9bq0y7D8qg3Y0r8JqzY3v1kq1nJx3e2nqkQe3y5wB7wqk0pZr4h3Hc0c5w==", null, false, new DateTime(2025, 7, 22, 0, 0, 0, 0, DateTimeKind.Utc), "33333333-3333-3333-3333-333333333333", null, false, "service.bot" }
                });

            migrationBuilder.InsertData(
                table: "ExecutedTasks",
                columns: new[] { "Id", "CreatedAt", "TaskId", "TaskName" },
                values: new object[,]
                {
                    { 1, new DateTime(2025, 7, 29, 0, 0, 0, 0, DateTimeKind.Utc), 1, "Design DB schema" },
                    { 2, new DateTime(2025, 7, 31, 0, 0, 0, 0, DateTimeKind.Utc), 2, "Implement DAL" },
                    { 3, new DateTime(2025, 7, 30, 0, 0, 0, 0, DateTimeKind.Utc), 3, "Seed data" },
                    { 4, new DateTime(2025, 7, 30, 0, 0, 0, 0, DateTimeKind.Utc), 10, "JWT endpoints" },
                    { 5, new DateTime(2025, 7, 29, 0, 0, 0, 0, DateTimeKind.Utc), 11, "Password policies" },
                    { 6, new DateTime(2025, 7, 31, 0, 0, 0, 0, DateTimeKind.Utc), 12, "Publishers" },
                    { 7, new DateTime(2025, 7, 31, 0, 0, 0, 0, DateTimeKind.Utc), 13, "OTel tracing" },
                    { 8, new DateTime(2025, 8, 1, 0, 0, 0, 0, DateTimeKind.Utc), 14, "Metrics exporter" }
                });

            migrationBuilder.UpdateData(
                table: "Projects",
                keyColumn: "Id",
                keyValue: 1,
                columns: new[] { "Deadline", "Description", "Name" },
                values: new object[] { new DateTime(2025, 8, 21, 0, 0, 0, 0, DateTimeKind.Utc), "Core Web API", "Task Manager API" });

            migrationBuilder.UpdateData(
                table: "Projects",
                keyColumn: "Id",
                keyValue: 2,
                columns: new[] { "AuthorId", "CreatedAt", "Deadline", "Description", "Name" },
                values: new object[] { 3, new DateTime(2025, 8, 2, 0, 0, 0, 0, DateTimeKind.Utc), new DateTime(2025, 8, 26, 0, 0, 0, 0, DateTimeKind.Utc), "SignalR hubs & clients", "Realtime Hub" });

            migrationBuilder.UpdateData(
                table: "Projects",
                keyColumn: "Id",
                keyValue: 3,
                columns: new[] { "AuthorId", "CreatedAt", "Deadline", "Description", "Name" },
                values: new object[] { 5, new DateTime(2025, 8, 3, 0, 0, 0, 0, DateTimeKind.Utc), new DateTime(2025, 8, 31, 0, 0, 0, 0, DateTimeKind.Utc), "RabbitMQ publishers/consumers", "Broker Layer" });

            migrationBuilder.InsertData(
                table: "Projects",
                columns: new[] { "Id", "AuthorId", "CreatedAt", "Deadline", "Description", "Name", "TeamId" },
                values: new object[] { 6, 2, new DateTime(2025, 7, 29, 0, 0, 0, 0, DateTimeKind.Utc), new DateTime(2025, 8, 23, 0, 0, 0, 0, DateTimeKind.Utc), "Backoffice UI", "Admin Panel", 1 });

            migrationBuilder.UpdateData(
                table: "Tasks",
                keyColumn: "Id",
                keyValue: 1,
                columns: new[] { "CreatedAt", "Description", "FinishedAt", "Name", "State" },
                values: new object[] { new DateTime(2025, 7, 29, 0, 0, 0, 0, DateTimeKind.Utc), "Initial ERD + migrations", new DateTime(2025, 8, 1, 0, 0, 0, 0, DateTimeKind.Utc), "Design DB schema", 2 });

            migrationBuilder.UpdateData(
                table: "Tasks",
                keyColumn: "Id",
                keyValue: 2,
                columns: new[] { "CreatedAt", "Description", "Name", "ProjectId" },
                values: new object[] { new DateTime(2025, 7, 31, 0, 0, 0, 0, DateTimeKind.Utc), "EF Core, repositories", "Implement DAL", 1 });

            migrationBuilder.UpdateData(
                table: "Tasks",
                keyColumn: "Id",
                keyValue: 3,
                columns: new[] { "CreatedAt", "Description", "FinishedAt", "Name", "PerformerId", "ProjectId", "State" },
                values: new object[] { new DateTime(2025, 8, 1, 0, 0, 0, 0, DateTimeKind.Utc), "Teams/Users/Projects/Tasks", null, "Seed data", 2, 1, 0 });

            migrationBuilder.UpdateData(
                table: "Tasks",
                keyColumn: "Id",
                keyValue: 4,
                columns: new[] { "CreatedAt", "Description", "Name", "PerformerId", "ProjectId", "State" },
                values: new object[] { new DateTime(2025, 7, 30, 0, 0, 0, 0, DateTimeKind.Utc), "User + project groups", "Create SignalR hub", 3, 2, 1 });

            migrationBuilder.UpdateData(
                table: "Tasks",
                keyColumn: "Id",
                keyValue: 5,
                columns: new[] { "CreatedAt", "Description", "FinishedAt", "Name", "PerformerId", "State" },
                values: new object[] { new DateTime(2025, 8, 1, 0, 0, 0, 0, DateTimeKind.Utc), "JWT bearer auth", null, "Auth for hubs", 4, 0 });

            migrationBuilder.InsertData(
                table: "Tasks",
                columns: new[] { "Id", "CreatedAt", "Description", "FinishedAt", "Name", "PerformerId", "ProjectId", "State" },
                values: new object[,]
                {
                    { 6, new DateTime(2025, 8, 2, 0, 0, 0, 0, DateTimeKind.Utc), "Redis backplane", null, "Backplane setup", 4, 2, 0 },
                    { 7, new DateTime(2025, 7, 31, 0, 0, 0, 0, DateTimeKind.Utc), "Outbox pattern", null, "Publishers", 5, 3, 0 }
                });

            migrationBuilder.InsertData(
                table: "Teams",
                columns: new[] { "Id", "CreatedAt", "Name" },
                values: new object[,]
                {
                    { 4, new DateTime(2025, 7, 26, 0, 0, 0, 0, DateTimeKind.Utc), "Team 4" },
                    { 5, new DateTime(2025, 7, 26, 0, 0, 0, 0, DateTimeKind.Utc), "Team 5" }
                });

            migrationBuilder.InsertData(
                table: "AspNetUserRoles",
                columns: new[] { "RoleId", "UserId" },
                values: new object[] { 1, 11 });

            migrationBuilder.InsertData(
                table: "AspNetUsers",
                columns: new[] { "Id", "AccessFailedCount", "BirthDay", "ConcurrencyStamp", "Email", "EmailConfirmed", "FirstName", "LastName", "LockoutEnabled", "LockoutEnd", "NormalizedEmail", "NormalizedUserName", "PasswordHash", "PhoneNumber", "PhoneNumberConfirmed", "RegisteredAt", "SecurityStamp", "TeamId", "TwoFactorEnabled", "UserName" },
                values: new object[,]
                {
                    { 7, 0, new DateTime(1994, 4, 4, 0, 0, 0, 0, DateTimeKind.Unspecified), "66666666-6666-6666-6666-666666666666", "john.d.doe@gmail.com", true, "John", "D", false, null, "JOHN.D.DOE@GMAIL.COM", "JOHN.D", "AQAAAAIAAYagAAAAENb2U6m9bq0y7D8qg3Y0r8JqzY3v1kq1nJx3e2nqkQe3y5wB7wqk0pZr4h3Hc0c5w==", null, false, new DateTime(2025, 7, 26, 0, 0, 0, 0, DateTimeKind.Utc), "55555555-5555-5555-5555-555555555555", 4, false, "john.d" },
                    { 8, 0, new DateTime(1990, 12, 2, 0, 0, 0, 0, DateTimeKind.Unspecified), "66666666-6666-6666-6666-666666666666", "kate.d@example.com", true, "Kate", "D", false, null, "KATE.D@EXAMPLE.COM", "KATE.D", "AQAAAAIAAYagAAAAENb2U6m9bq0y7D8qg3Y0r8JqzY3v1kq1nJx3e2nqkQe3y5wB7wqk0pZr4h3Hc0c5w==", null, false, new DateTime(2025, 7, 26, 0, 0, 0, 0, DateTimeKind.Utc), "55555555-5555-5555-5555-555555555555", 4, false, "kate.d" },
                    { 9, 0, new DateTime(1995, 5, 5, 0, 0, 0, 0, DateTimeKind.Unspecified), "66666666-6666-6666-6666-666666666666", "john.e.doe@gmail.com", true, "John", "E", false, null, "JOHN.E.DOE@GMAIL.COM", "JOHN.E", "AQAAAAIAAYagAAAAENb2U6m9bq0y7D8qg3Y0r8JqzY3v1kq1nJx3e2nqkQe3y5wB7wqk0pZr4h3Hc0c5w==", null, false, new DateTime(2025, 7, 26, 0, 0, 0, 0, DateTimeKind.Utc), "55555555-5555-5555-5555-555555555555", 5, false, "john.e" },
                    { 10, 0, new DateTime(1988, 7, 7, 0, 0, 0, 0, DateTimeKind.Unspecified), "66666666-6666-6666-6666-666666666666", "kate.e@example.com", true, "Kate", "E", false, null, "KATE.E@EXAMPLE.COM", "KATE.E", "AQAAAAIAAYagAAAAENb2U6m9bq0y7D8qg3Y0r8JqzY3v1kq1nJx3e2nqkQe3y5wB7wqk0pZr4h3Hc0c5w==", null, false, new DateTime(2025, 7, 26, 0, 0, 0, 0, DateTimeKind.Utc), "55555555-5555-5555-5555-555555555555", 5, false, "kate.e" }
                });

            migrationBuilder.InsertData(
                table: "Tasks",
                columns: new[] { "Id", "CreatedAt", "Description", "FinishedAt", "Name", "PerformerId", "ProjectId", "State" },
                values: new object[,]
                {
                    { 8, new DateTime(2025, 8, 1, 0, 0, 0, 0, DateTimeKind.Utc), "Idempotency, DLQ", null, "Consumers", 6, 3, 0 },
                    { 9, new DateTime(2025, 8, 2, 0, 0, 0, 0, DateTimeKind.Utc), "Exponential backoff", null, "Retry policy", 6, 3, 0 },
                    { 16, new DateTime(2025, 7, 29, 0, 0, 0, 0, DateTimeKind.Utc), "Users/Teams grid", null, "Scaffold admin UI", 11, 6, 1 },
                    { 17, new DateTime(2025, 8, 1, 0, 0, 0, 0, DateTimeKind.Utc), "Guarded routes", null, "RBAC in UI", 12, 6, 0 },
                    { 18, new DateTime(2025, 8, 2, 0, 0, 0, 0, DateTimeKind.Utc), "Filters & export", null, "Audit logs view", 12, 6, 0 }
                });

            migrationBuilder.InsertData(
                table: "Projects",
                columns: new[] { "Id", "AuthorId", "CreatedAt", "Deadline", "Description", "Name", "TeamId" },
                values: new object[,]
                {
                    { 4, 7, new DateTime(2025, 7, 30, 0, 0, 0, 0, DateTimeKind.Utc), new DateTime(2025, 8, 16, 0, 0, 0, 0, DateTimeKind.Utc), "JWT, refresh tokens", "Auth & Identity", 4 },
                    { 5, 9, new DateTime(2025, 7, 31, 0, 0, 0, 0, DateTimeKind.Utc), new DateTime(2025, 8, 19, 0, 0, 0, 0, DateTimeKind.Utc), "OTel, tracing, metrics", "Observability", 5 }
                });

            migrationBuilder.InsertData(
                table: "Tasks",
                columns: new[] { "Id", "CreatedAt", "Description", "FinishedAt", "Name", "PerformerId", "ProjectId", "State" },
                values: new object[,]
                {
                    { 10, new DateTime(2025, 7, 30, 0, 0, 0, 0, DateTimeKind.Utc), "Login/refresh/roles", null, "JWT endpoints", 7, 4, 1 },
                    { 11, new DateTime(2025, 8, 1, 0, 0, 0, 0, DateTimeKind.Utc), "Strong defaults", null, "Password policies", 8, 4, 0 },
                    { 12, new DateTime(2025, 8, 2, 0, 0, 0, 0, DateTimeKind.Utc), "RBAC", null, "Seed admin role", 8, 4, 0 },
                    { 13, new DateTime(2025, 7, 31, 0, 0, 0, 0, DateTimeKind.Utc), "ActivitySource", null, "OTel tracing", 9, 5, 0 },
                    { 14, new DateTime(2025, 8, 1, 0, 0, 0, 0, DateTimeKind.Utc), "Prometheus", null, "Metrics exporter", 10, 5, 0 },
                    { 15, new DateTime(2025, 8, 3, 0, 0, 0, 0, DateTimeKind.Utc), "Serilog + sinks", null, "Logs pipeline", 10, 5, 0 }
                });

            migrationBuilder.CreateIndex(
                name: "IX_Teams_Name",
                table: "Teams",
                column: "Name",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_Projects_TeamId_Name",
                table: "Projects",
                columns: new[] { "TeamId", "Name" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "EmailIndex",
                table: "AspNetUsers",
                column: "NormalizedEmail");

            migrationBuilder.CreateIndex(
                name: "IX_AspNetUsers_Email",
                table: "AspNetUsers",
                column: "Email",
                unique: true,
                filter: "[Email] IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "UserNameIndex",
                table: "AspNetUsers",
                column: "NormalizedUserName",
                unique: true,
                filter: "[NormalizedUserName] IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "IX_AspNetRoleClaims_RoleId",
                table: "AspNetRoleClaims",
                column: "RoleId");

            migrationBuilder.CreateIndex(
                name: "RoleNameIndex",
                table: "AspNetRoles",
                column: "NormalizedName",
                unique: true,
                filter: "[NormalizedName] IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "IX_AspNetUserClaims_UserId",
                table: "AspNetUserClaims",
                column: "UserId");

            migrationBuilder.CreateIndex(
                name: "IX_AspNetUserLogins_UserId",
                table: "AspNetUserLogins",
                column: "UserId");

            migrationBuilder.CreateIndex(
                name: "IX_AspNetUserRoles_RoleId",
                table: "AspNetUserRoles",
                column: "RoleId");

            migrationBuilder.AddForeignKey(
                name: "FK_AspNetUsers_Teams_TeamId",
                table: "AspNetUsers",
                column: "TeamId",
                principalTable: "Teams",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_Projects_AspNetUsers_AuthorId",
                table: "Projects",
                column: "AuthorId",
                principalTable: "AspNetUsers",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_Tasks_AspNetUsers_PerformerId",
                table: "Tasks",
                column: "PerformerId",
                principalTable: "AspNetUsers",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_AspNetUsers_Teams_TeamId",
                table: "AspNetUsers");

            migrationBuilder.DropForeignKey(
                name: "FK_Projects_AspNetUsers_AuthorId",
                table: "Projects");

            migrationBuilder.DropForeignKey(
                name: "FK_Tasks_AspNetUsers_PerformerId",
                table: "Tasks");

            migrationBuilder.DropTable(
                name: "AspNetRoleClaims");

            migrationBuilder.DropTable(
                name: "AspNetUserClaims");

            migrationBuilder.DropTable(
                name: "AspNetUserLogins");

            migrationBuilder.DropTable(
                name: "AspNetUserRoles");

            migrationBuilder.DropTable(
                name: "AspNetUserTokens");

            migrationBuilder.DropTable(
                name: "AspNetRoles");

            migrationBuilder.DropIndex(
                name: "IX_Teams_Name",
                table: "Teams");

            migrationBuilder.DropIndex(
                name: "IX_Projects_TeamId_Name",
                table: "Projects");

            migrationBuilder.DropPrimaryKey(
                name: "PK_AspNetUsers",
                table: "AspNetUsers");

            migrationBuilder.DropIndex(
                name: "EmailIndex",
                table: "AspNetUsers");

            migrationBuilder.DropIndex(
                name: "IX_AspNetUsers_Email",
                table: "AspNetUsers");

            migrationBuilder.DropIndex(
                name: "UserNameIndex",
                table: "AspNetUsers");

            migrationBuilder.DeleteData(
                table: "ExecutedTasks",
                keyColumn: "Id",
                keyValue: 1);

            migrationBuilder.DeleteData(
                table: "ExecutedTasks",
                keyColumn: "Id",
                keyValue: 2);

            migrationBuilder.DeleteData(
                table: "ExecutedTasks",
                keyColumn: "Id",
                keyValue: 3);

            migrationBuilder.DeleteData(
                table: "ExecutedTasks",
                keyColumn: "Id",
                keyValue: 4);

            migrationBuilder.DeleteData(
                table: "ExecutedTasks",
                keyColumn: "Id",
                keyValue: 5);

            migrationBuilder.DeleteData(
                table: "ExecutedTasks",
                keyColumn: "Id",
                keyValue: 6);

            migrationBuilder.DeleteData(
                table: "ExecutedTasks",
                keyColumn: "Id",
                keyValue: 7);

            migrationBuilder.DeleteData(
                table: "ExecutedTasks",
                keyColumn: "Id",
                keyValue: 8);

            migrationBuilder.DeleteData(
                table: "Tasks",
                keyColumn: "Id",
                keyValue: 6);

            migrationBuilder.DeleteData(
                table: "Tasks",
                keyColumn: "Id",
                keyValue: 7);

            migrationBuilder.DeleteData(
                table: "Tasks",
                keyColumn: "Id",
                keyValue: 8);

            migrationBuilder.DeleteData(
                table: "Tasks",
                keyColumn: "Id",
                keyValue: 9);

            migrationBuilder.DeleteData(
                table: "Tasks",
                keyColumn: "Id",
                keyValue: 10);

            migrationBuilder.DeleteData(
                table: "Tasks",
                keyColumn: "Id",
                keyValue: 11);

            migrationBuilder.DeleteData(
                table: "Tasks",
                keyColumn: "Id",
                keyValue: 12);

            migrationBuilder.DeleteData(
                table: "Tasks",
                keyColumn: "Id",
                keyValue: 13);

            migrationBuilder.DeleteData(
                table: "Tasks",
                keyColumn: "Id",
                keyValue: 14);

            migrationBuilder.DeleteData(
                table: "Tasks",
                keyColumn: "Id",
                keyValue: 15);

            migrationBuilder.DeleteData(
                table: "Tasks",
                keyColumn: "Id",
                keyValue: 16);

            migrationBuilder.DeleteData(
                table: "Tasks",
                keyColumn: "Id",
                keyValue: 17);

            migrationBuilder.DeleteData(
                table: "Tasks",
                keyColumn: "Id",
                keyValue: 18);

            migrationBuilder.DeleteData(
                table: "AspNetUsers",
                keyColumn: "Id",
                keyValue: 6);

            migrationBuilder.DeleteData(
                table: "AspNetUsers",
                keyColumn: "Id",
                keyValue: 8);

            migrationBuilder.DeleteData(
                table: "AspNetUsers",
                keyColumn: "Id",
                keyValue: 10);

            migrationBuilder.DeleteData(
                table: "AspNetUsers",
                keyColumn: "Id",
                keyValue: 11);

            migrationBuilder.DeleteData(
                table: "AspNetUsers",
                keyColumn: "Id",
                keyValue: 12);

            migrationBuilder.DeleteData(
                table: "Projects",
                keyColumn: "Id",
                keyValue: 4);

            migrationBuilder.DeleteData(
                table: "Projects",
                keyColumn: "Id",
                keyValue: 5);

            migrationBuilder.DeleteData(
                table: "Projects",
                keyColumn: "Id",
                keyValue: 6);

            migrationBuilder.DeleteData(
                table: "AspNetUsers",
                keyColumn: "Id",
                keyValue: 7);

            migrationBuilder.DeleteData(
                table: "AspNetUsers",
                keyColumn: "Id",
                keyValue: 9);

            migrationBuilder.DeleteData(
                table: "Teams",
                keyColumn: "Id",
                keyValue: 4);

            migrationBuilder.DeleteData(
                table: "Teams",
                keyColumn: "Id",
                keyValue: 5);

            migrationBuilder.DropColumn(
                name: "AccessFailedCount",
                table: "AspNetUsers");

            migrationBuilder.DropColumn(
                name: "ConcurrencyStamp",
                table: "AspNetUsers");

            migrationBuilder.DropColumn(
                name: "EmailConfirmed",
                table: "AspNetUsers");

            migrationBuilder.DropColumn(
                name: "LockoutEnabled",
                table: "AspNetUsers");

            migrationBuilder.DropColumn(
                name: "LockoutEnd",
                table: "AspNetUsers");

            migrationBuilder.DropColumn(
                name: "NormalizedEmail",
                table: "AspNetUsers");

            migrationBuilder.DropColumn(
                name: "NormalizedUserName",
                table: "AspNetUsers");

            migrationBuilder.DropColumn(
                name: "PasswordHash",
                table: "AspNetUsers");

            migrationBuilder.DropColumn(
                name: "PhoneNumber",
                table: "AspNetUsers");

            migrationBuilder.DropColumn(
                name: "PhoneNumberConfirmed",
                table: "AspNetUsers");

            migrationBuilder.DropColumn(
                name: "SecurityStamp",
                table: "AspNetUsers");

            migrationBuilder.DropColumn(
                name: "TwoFactorEnabled",
                table: "AspNetUsers");

            migrationBuilder.DropColumn(
                name: "UserName",
                table: "AspNetUsers");

            migrationBuilder.RenameTable(
                name: "AspNetUsers",
                newName: "Users");

            migrationBuilder.RenameIndex(
                name: "IX_AspNetUsers_TeamId",
                table: "Users",
                newName: "IX_Users_TeamId");

            migrationBuilder.AlterColumn<string>(
                name: "Name",
                table: "Teams",
                type: "nvarchar(max)",
                nullable: false,
                oldClrType: typeof(string),
                oldType: "nvarchar(100)",
                oldMaxLength: 100);

            migrationBuilder.AlterColumn<string>(
                name: "Name",
                table: "Tasks",
                type: "nvarchar(max)",
                nullable: false,
                oldClrType: typeof(string),
                oldType: "nvarchar(100)",
                oldMaxLength: 100);

            migrationBuilder.AlterColumn<string>(
                name: "Description",
                table: "Tasks",
                type: "nvarchar(max)",
                nullable: false,
                oldClrType: typeof(string),
                oldType: "nvarchar(1000)",
                oldMaxLength: 1000);

            migrationBuilder.AlterColumn<string>(
                name: "Name",
                table: "Projects",
                type: "nvarchar(max)",
                nullable: false,
                oldClrType: typeof(string),
                oldType: "nvarchar(100)",
                oldMaxLength: 100);

            migrationBuilder.AlterColumn<string>(
                name: "Description",
                table: "Projects",
                type: "nvarchar(max)",
                nullable: false,
                oldClrType: typeof(string),
                oldType: "nvarchar(1000)",
                oldMaxLength: 1000);

            migrationBuilder.AlterColumn<string>(
                name: "Email",
                table: "Users",
                type: "nvarchar(max)",
                nullable: false,
                defaultValue: "",
                oldClrType: typeof(string),
                oldType: "nvarchar(256)",
                oldMaxLength: 256,
                oldNullable: true);

            migrationBuilder.AddPrimaryKey(
                name: "PK_Users",
                table: "Users",
                column: "Id");

            migrationBuilder.UpdateData(
                table: "Projects",
                keyColumn: "Id",
                keyValue: 1,
                columns: new[] { "Deadline", "Description", "Name" },
                values: new object[] { new DateTime(2025, 8, 11, 0, 0, 0, 0, DateTimeKind.Utc), "Description 1", "Project 1" });

            migrationBuilder.UpdateData(
                table: "Projects",
                keyColumn: "Id",
                keyValue: 2,
                columns: new[] { "AuthorId", "CreatedAt", "Deadline", "Description", "Name" },
                values: new object[] { 2, new DateTime(2025, 8, 1, 0, 0, 0, 0, DateTimeKind.Utc), new DateTime(2025, 8, 16, 0, 0, 0, 0, DateTimeKind.Utc), "Description 2", "Project 2" });

            migrationBuilder.UpdateData(
                table: "Projects",
                keyColumn: "Id",
                keyValue: 3,
                columns: new[] { "AuthorId", "CreatedAt", "Deadline", "Description", "Name" },
                values: new object[] { 3, new DateTime(2025, 8, 1, 0, 0, 0, 0, DateTimeKind.Utc), new DateTime(2025, 8, 21, 0, 0, 0, 0, DateTimeKind.Utc), "Description 3", "Project 3" });

            migrationBuilder.UpdateData(
                table: "Tasks",
                keyColumn: "Id",
                keyValue: 1,
                columns: new[] { "CreatedAt", "Description", "FinishedAt", "Name", "State" },
                values: new object[] { new DateTime(2025, 8, 1, 0, 0, 0, 0, DateTimeKind.Utc), "Task Description 1", null, "Task 1", 0 });

            migrationBuilder.UpdateData(
                table: "Tasks",
                keyColumn: "Id",
                keyValue: 2,
                columns: new[] { "CreatedAt", "Description", "Name", "ProjectId" },
                values: new object[] { new DateTime(2025, 8, 1, 0, 0, 0, 0, DateTimeKind.Utc), "Task Description 2", "Task 2", 2 });

            migrationBuilder.UpdateData(
                table: "Tasks",
                keyColumn: "Id",
                keyValue: 3,
                columns: new[] { "CreatedAt", "Description", "FinishedAt", "Name", "PerformerId", "ProjectId", "State" },
                values: new object[] { new DateTime(2025, 7, 29, 0, 0, 0, 0, DateTimeKind.Utc), "Task Description 3", new DateTime(2025, 8, 1, 0, 0, 0, 0, DateTimeKind.Utc), "Task 3", 3, 3, 2 });

            migrationBuilder.UpdateData(
                table: "Tasks",
                keyColumn: "Id",
                keyValue: 4,
                columns: new[] { "CreatedAt", "Description", "Name", "PerformerId", "ProjectId", "State" },
                values: new object[] { new DateTime(2025, 8, 1, 0, 0, 0, 0, DateTimeKind.Utc), "Task Description 4", "Task 4", 1, 1, 0 });

            migrationBuilder.UpdateData(
                table: "Tasks",
                keyColumn: "Id",
                keyValue: 5,
                columns: new[] { "CreatedAt", "Description", "FinishedAt", "Name", "PerformerId", "State" },
                values: new object[] { new DateTime(2025, 7, 29, 0, 0, 0, 0, DateTimeKind.Utc), "Task Description 5", new DateTime(2025, 8, 1, 0, 0, 0, 0, DateTimeKind.Utc), "Task 5", 2, 3 });

            migrationBuilder.UpdateData(
                table: "Users",
                keyColumn: "Id",
                keyValue: 2,
                columns: new[] { "BirthDay", "Email", "FirstName", "LastName", "TeamId" },
                values: new object[] { new DateTime(1992, 2, 2, 0, 0, 0, 0, DateTimeKind.Unspecified), "john.B.doe@gmail.com", "John", "B", 2 });

            migrationBuilder.UpdateData(
                table: "Users",
                keyColumn: "Id",
                keyValue: 3,
                columns: new[] { "BirthDay", "Email", "LastName", "TeamId" },
                values: new object[] { new DateTime(1993, 3, 3, 0, 0, 0, 0, DateTimeKind.Unspecified), "john.C.doe@gmail.com", "C", 3 });

            migrationBuilder.UpdateData(
                table: "Users",
                keyColumn: "Id",
                keyValue: 4,
                columns: new[] { "BirthDay", "Email", "FirstName", "LastName", "TeamId" },
                values: new object[] { new DateTime(1994, 4, 4, 0, 0, 0, 0, DateTimeKind.Unspecified), "john.D.doe@gmail.com", "John", "D", 1 });

            migrationBuilder.UpdateData(
                table: "Users",
                keyColumn: "Id",
                keyValue: 5,
                columns: new[] { "BirthDay", "Email", "LastName", "TeamId" },
                values: new object[] { new DateTime(1995, 5, 5, 0, 0, 0, 0, DateTimeKind.Unspecified), "john.E.doe@gmail.com", "E", 2 });

            migrationBuilder.CreateIndex(
                name: "IX_Projects_TeamId",
                table: "Projects",
                column: "TeamId");

            migrationBuilder.AddForeignKey(
                name: "FK_Projects_Users_AuthorId",
                table: "Projects",
                column: "AuthorId",
                principalTable: "Users",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_Tasks_Users_PerformerId",
                table: "Tasks",
                column: "PerformerId",
                principalTable: "Users",
                principalColumn: "Id",
                onDelete: ReferentialAction.Cascade);

            migrationBuilder.AddForeignKey(
                name: "FK_Users_Teams_TeamId",
                table: "Users",
                column: "TeamId",
                principalTable: "Teams",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);
        }
    }
}
