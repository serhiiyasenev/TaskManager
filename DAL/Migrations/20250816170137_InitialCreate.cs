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
                name: "ExecutedTasks",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    TaskId = table.Column<int>(type: "int", nullable: false),
                    TaskName = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "datetime2", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ExecutedTasks", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "Teams",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    Name = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "datetime2", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Teams", x => x.Id);
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
                name: "AspNetUsers",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    TeamId = table.Column<int>(type: "int", nullable: true),
                    FirstName = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false),
                    LastName = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false),
                    RegisteredAt = table.Column<DateTime>(type: "datetime2", nullable: false),
                    BirthDay = table.Column<DateTime>(type: "datetime2", nullable: false),
                    UserName = table.Column<string>(type: "nvarchar(256)", maxLength: 256, nullable: true),
                    NormalizedUserName = table.Column<string>(type: "nvarchar(256)", maxLength: 256, nullable: true),
                    Email = table.Column<string>(type: "nvarchar(256)", maxLength: 256, nullable: true),
                    NormalizedEmail = table.Column<string>(type: "nvarchar(256)", maxLength: 256, nullable: true),
                    EmailConfirmed = table.Column<bool>(type: "bit", nullable: false),
                    PasswordHash = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    SecurityStamp = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    ConcurrencyStamp = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    PhoneNumber = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    PhoneNumberConfirmed = table.Column<bool>(type: "bit", nullable: false),
                    TwoFactorEnabled = table.Column<bool>(type: "bit", nullable: false),
                    LockoutEnd = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    LockoutEnabled = table.Column<bool>(type: "bit", nullable: false),
                    AccessFailedCount = table.Column<int>(type: "int", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AspNetUsers", x => x.Id);
                    table.ForeignKey(
                        name: "FK_AspNetUsers_Teams_TeamId",
                        column: x => x.TeamId,
                        principalTable: "Teams",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
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
                name: "Projects",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    AuthorId = table.Column<int>(type: "int", nullable: false),
                    TeamId = table.Column<int>(type: "int", nullable: false),
                    Name = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false),
                    Description = table.Column<string>(type: "nvarchar(1000)", maxLength: 1000, nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "datetime2", nullable: false),
                    Deadline = table.Column<DateTime>(type: "datetime2", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Projects", x => x.Id);
                    table.ForeignKey(
                        name: "FK_Projects_AspNetUsers_AuthorId",
                        column: x => x.AuthorId,
                        principalTable: "AspNetUsers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_Projects_Teams_TeamId",
                        column: x => x.TeamId,
                        principalTable: "Teams",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "Tasks",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    ProjectId = table.Column<int>(type: "int", nullable: false),
                    PerformerId = table.Column<int>(type: "int", nullable: false),
                    Name = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false),
                    Description = table.Column<string>(type: "nvarchar(1000)", maxLength: 1000, nullable: false),
                    State = table.Column<int>(type: "int", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "datetime2", nullable: false),
                    FinishedAt = table.Column<DateTime>(type: "datetime2", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Tasks", x => x.Id);
                    table.ForeignKey(
                        name: "FK_Tasks_AspNetUsers_PerformerId",
                        column: x => x.PerformerId,
                        principalTable: "AspNetUsers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_Tasks_Projects_ProjectId",
                        column: x => x.ProjectId,
                        principalTable: "Projects",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.InsertData(
                table: "AspNetRoles",
                columns: new[] { "Id", "ConcurrencyStamp", "Name", "NormalizedName" },
                values: new object[] { 1, "77777777-7777-7777-7777-777777777777", "admin", "ADMIN" });

            migrationBuilder.InsertData(
                table: "AspNetUsers",
                columns: new[] { "Id", "AccessFailedCount", "BirthDay", "ConcurrencyStamp", "Email", "EmailConfirmed", "FirstName", "LastName", "LockoutEnabled", "LockoutEnd", "NormalizedEmail", "NormalizedUserName", "PasswordHash", "PhoneNumber", "PhoneNumberConfirmed", "RegisteredAt", "SecurityStamp", "TeamId", "TwoFactorEnabled", "UserName" },
                values: new object[,]
                {
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

            migrationBuilder.InsertData(
                table: "Teams",
                columns: new[] { "Id", "CreatedAt", "Name" },
                values: new object[,]
                {
                    { 1, new DateTime(2025, 7, 29, 0, 0, 0, 0, DateTimeKind.Utc), "Team 1" },
                    { 2, new DateTime(2025, 7, 29, 0, 0, 0, 0, DateTimeKind.Utc), "Team 2" },
                    { 3, new DateTime(2025, 7, 29, 0, 0, 0, 0, DateTimeKind.Utc), "Team 3" },
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
                    { 1, 0, new DateTime(1991, 1, 1, 0, 0, 0, 0, DateTimeKind.Unspecified), "66666666-6666-6666-6666-666666666666", "john.A.doe@gmail.com", true, "John", "A", false, null, "JOHN.A.DOE@GMAIL.COM", "JOHN.A", "AQAAAAIAAYagAAAAENb2U6m9bq0y7D8qg3Y0r8JqzY3v1kq1nJx3e2nqkQe3y5wB7wqk0pZr4h3Hc0c5w==", null, false, new DateTime(2025, 7, 29, 0, 0, 0, 0, DateTimeKind.Utc), "55555555-5555-5555-5555-555555555555", 1, false, "john.a" },
                    { 2, 0, new DateTime(1990, 5, 10, 0, 0, 0, 0, DateTimeKind.Unspecified), "66666666-6666-6666-6666-666666666666", "kate.a@example.com", true, "Kate", "A", false, null, "KATE.A@EXAMPLE.COM", "KATE.A", "AQAAAAIAAYagAAAAENb2U6m9bq0y7D8qg3Y0r8JqzY3v1kq1nJx3e2nqkQe3y5wB7wqk0pZr4h3Hc0c5w==", null, false, new DateTime(2025, 7, 29, 0, 0, 0, 0, DateTimeKind.Utc), "55555555-5555-5555-5555-555555555555", 1, false, "kate.a" },
                    { 3, 0, new DateTime(1992, 2, 2, 0, 0, 0, 0, DateTimeKind.Unspecified), "66666666-6666-6666-6666-666666666666", "john.b.doe@gmail.com", true, "John", "B", false, null, "JOHN.B.DOE@GMAIL.COM", "JOHN.B", "AQAAAAIAAYagAAAAENb2U6m9bq0y7D8qg3Y0r8JqzY3v1kq1nJx3e2nqkQe3y5wB7wqk0pZr4h3Hc0c5w==", null, false, new DateTime(2025, 7, 29, 0, 0, 0, 0, DateTimeKind.Utc), "55555555-5555-5555-5555-555555555555", 1, false, "john.b" },
                    { 4, 0, new DateTime(1991, 6, 15, 0, 0, 0, 0, DateTimeKind.Unspecified), "66666666-6666-6666-6666-666666666666", "kate.b@example.com", true, "Kate", "B", false, null, "KATE.B@EXAMPLE.COM", "KATE.B", "AQAAAAIAAYagAAAAENb2U6m9bq0y7D8qg3Y0r8JqzY3v1kq1nJx3e2nqkQe3y5wB7wqk0pZr4h3Hc0c5w==", null, false, new DateTime(2025, 7, 29, 0, 0, 0, 0, DateTimeKind.Utc), "55555555-5555-5555-5555-555555555555", 2, false, "kate.b" },
                    { 5, 0, new DateTime(1993, 3, 3, 0, 0, 0, 0, DateTimeKind.Unspecified), "66666666-6666-6666-6666-666666666666", "john.c.doe@gmail.com", true, "John", "C", false, null, "JOHN.C.DOE@GMAIL.COM", "JOHN.C", "AQAAAAIAAYagAAAAENb2U6m9bq0y7D8qg3Y0r8JqzY3v1kq1nJx3e2nqkQe3y5wB7wqk0pZr4h3Hc0c5w==", null, false, new DateTime(2025, 7, 29, 0, 0, 0, 0, DateTimeKind.Utc), "55555555-5555-5555-5555-555555555555", 3, false, "john.c" },
                    { 6, 0, new DateTime(1989, 8, 20, 0, 0, 0, 0, DateTimeKind.Unspecified), "66666666-6666-6666-6666-666666666666", "kate.c@example.com", true, "Kate", "C", false, null, "KATE.C@EXAMPLE.COM", "KATE.C", "AQAAAAIAAYagAAAAENb2U6m9bq0y7D8qg3Y0r8JqzY3v1kq1nJx3e2nqkQe3y5wB7wqk0pZr4h3Hc0c5w==", null, false, new DateTime(2025, 7, 29, 0, 0, 0, 0, DateTimeKind.Utc), "55555555-5555-5555-5555-555555555555", 3, false, "kate.c" },
                    { 7, 0, new DateTime(1994, 4, 4, 0, 0, 0, 0, DateTimeKind.Unspecified), "66666666-6666-6666-6666-666666666666", "john.d.doe@gmail.com", true, "John", "D", false, null, "JOHN.D.DOE@GMAIL.COM", "JOHN.D", "AQAAAAIAAYagAAAAENb2U6m9bq0y7D8qg3Y0r8JqzY3v1kq1nJx3e2nqkQe3y5wB7wqk0pZr4h3Hc0c5w==", null, false, new DateTime(2025, 7, 26, 0, 0, 0, 0, DateTimeKind.Utc), "55555555-5555-5555-5555-555555555555", 4, false, "john.d" },
                    { 8, 0, new DateTime(1990, 12, 2, 0, 0, 0, 0, DateTimeKind.Unspecified), "66666666-6666-6666-6666-666666666666", "kate.d@example.com", true, "Kate", "D", false, null, "KATE.D@EXAMPLE.COM", "KATE.D", "AQAAAAIAAYagAAAAENb2U6m9bq0y7D8qg3Y0r8JqzY3v1kq1nJx3e2nqkQe3y5wB7wqk0pZr4h3Hc0c5w==", null, false, new DateTime(2025, 7, 26, 0, 0, 0, 0, DateTimeKind.Utc), "55555555-5555-5555-5555-555555555555", 4, false, "kate.d" },
                    { 9, 0, new DateTime(1995, 5, 5, 0, 0, 0, 0, DateTimeKind.Unspecified), "66666666-6666-6666-6666-666666666666", "john.e.doe@gmail.com", true, "John", "E", false, null, "JOHN.E.DOE@GMAIL.COM", "JOHN.E", "AQAAAAIAAYagAAAAENb2U6m9bq0y7D8qg3Y0r8JqzY3v1kq1nJx3e2nqkQe3y5wB7wqk0pZr4h3Hc0c5w==", null, false, new DateTime(2025, 7, 26, 0, 0, 0, 0, DateTimeKind.Utc), "55555555-5555-5555-5555-555555555555", 5, false, "john.e" },
                    { 10, 0, new DateTime(1988, 7, 7, 0, 0, 0, 0, DateTimeKind.Unspecified), "66666666-6666-6666-6666-666666666666", "kate.e@example.com", true, "Kate", "E", false, null, "KATE.E@EXAMPLE.COM", "KATE.E", "AQAAAAIAAYagAAAAENb2U6m9bq0y7D8qg3Y0r8JqzY3v1kq1nJx3e2nqkQe3y5wB7wqk0pZr4h3Hc0c5w==", null, false, new DateTime(2025, 7, 26, 0, 0, 0, 0, DateTimeKind.Utc), "55555555-5555-5555-5555-555555555555", 5, false, "kate.e" }
                });

            migrationBuilder.InsertData(
                table: "Projects",
                columns: new[] { "Id", "AuthorId", "CreatedAt", "Deadline", "Description", "Name", "TeamId" },
                values: new object[,]
                {
                    { 1, 1, new DateTime(2025, 8, 1, 0, 0, 0, 0, DateTimeKind.Utc), new DateTime(2025, 8, 21, 0, 0, 0, 0, DateTimeKind.Utc), "Core Web API", "Task Manager API", 1 },
                    { 2, 3, new DateTime(2025, 8, 2, 0, 0, 0, 0, DateTimeKind.Utc), new DateTime(2025, 8, 26, 0, 0, 0, 0, DateTimeKind.Utc), "SignalR hubs & clients", "Realtime Hub", 2 },
                    { 3, 5, new DateTime(2025, 8, 3, 0, 0, 0, 0, DateTimeKind.Utc), new DateTime(2025, 8, 31, 0, 0, 0, 0, DateTimeKind.Utc), "RabbitMQ publishers/consumers", "Broker Layer", 3 },
                    { 4, 7, new DateTime(2025, 7, 30, 0, 0, 0, 0, DateTimeKind.Utc), new DateTime(2025, 8, 16, 0, 0, 0, 0, DateTimeKind.Utc), "JWT, refresh tokens", "Auth & Identity", 4 },
                    { 5, 9, new DateTime(2025, 7, 31, 0, 0, 0, 0, DateTimeKind.Utc), new DateTime(2025, 8, 19, 0, 0, 0, 0, DateTimeKind.Utc), "OTel, tracing, metrics", "Observability", 5 },
                    { 6, 2, new DateTime(2025, 7, 29, 0, 0, 0, 0, DateTimeKind.Utc), new DateTime(2025, 8, 23, 0, 0, 0, 0, DateTimeKind.Utc), "Backoffice UI", "Admin Panel", 1 }
                });

            migrationBuilder.InsertData(
                table: "Tasks",
                columns: new[] { "Id", "CreatedAt", "Description", "FinishedAt", "Name", "PerformerId", "ProjectId", "State" },
                values: new object[,]
                {
                    { 1, new DateTime(2025, 7, 29, 0, 0, 0, 0, DateTimeKind.Utc), "Initial ERD + migrations", new DateTime(2025, 8, 1, 0, 0, 0, 0, DateTimeKind.Utc), "Design DB schema", 1, 1, 2 },
                    { 2, new DateTime(2025, 7, 31, 0, 0, 0, 0, DateTimeKind.Utc), "EF Core, repositories", null, "Implement DAL", 2, 1, 1 },
                    { 3, new DateTime(2025, 8, 1, 0, 0, 0, 0, DateTimeKind.Utc), "Teams/Users/Projects/Tasks", null, "Seed data", 3, 1, 0 },
                    { 4, new DateTime(2025, 7, 30, 0, 0, 0, 0, DateTimeKind.Utc), "User + project groups", null, "Create SignalR hub", 3, 2, 1 },
                    { 5, new DateTime(2025, 8, 1, 0, 0, 0, 0, DateTimeKind.Utc), "JWT bearer auth", null, "Auth for hubs", 4, 2, 0 },
                    { 6, new DateTime(2025, 8, 2, 0, 0, 0, 0, DateTimeKind.Utc), "Redis backplane", null, "Backplane setup", 4, 2, 0 },
                    { 7, new DateTime(2025, 7, 31, 0, 0, 0, 0, DateTimeKind.Utc), "Outbox pattern", null, "Publishers", 5, 3, 0 },
                    { 8, new DateTime(2025, 8, 1, 0, 0, 0, 0, DateTimeKind.Utc), "Idempotency, DLQ", null, "Consumers", 6, 3, 0 },
                    { 9, new DateTime(2025, 8, 2, 0, 0, 0, 0, DateTimeKind.Utc), "Exponential backoff", null, "Retry policy", 6, 3, 0 },
                    { 10, new DateTime(2025, 7, 30, 0, 0, 0, 0, DateTimeKind.Utc), "Login/refresh/roles", null, "JWT endpoints", 7, 4, 1 },
                    { 11, new DateTime(2025, 8, 1, 0, 0, 0, 0, DateTimeKind.Utc), "Strong defaults", null, "Password policies", 8, 4, 0 },
                    { 12, new DateTime(2025, 8, 2, 0, 0, 0, 0, DateTimeKind.Utc), "RBAC", null, "Seed admin role", 8, 4, 0 },
                    { 13, new DateTime(2025, 7, 31, 0, 0, 0, 0, DateTimeKind.Utc), "ActivitySource", null, "OTel tracing", 9, 5, 0 },
                    { 14, new DateTime(2025, 8, 1, 0, 0, 0, 0, DateTimeKind.Utc), "Prometheus", null, "Metrics exporter", 10, 5, 0 },
                    { 15, new DateTime(2025, 8, 3, 0, 0, 0, 0, DateTimeKind.Utc), "Serilog + sinks", null, "Logs pipeline", 10, 5, 0 },
                    { 16, new DateTime(2025, 7, 29, 0, 0, 0, 0, DateTimeKind.Utc), "Users/Teams grid", null, "Scaffold admin UI", 11, 6, 1 },
                    { 17, new DateTime(2025, 8, 1, 0, 0, 0, 0, DateTimeKind.Utc), "Guarded routes", null, "RBAC in UI", 12, 6, 0 },
                    { 18, new DateTime(2025, 8, 2, 0, 0, 0, 0, DateTimeKind.Utc), "Filters & export", null, "Audit logs view", 12, 6, 0 }
                });

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
                name: "IX_AspNetUsers_TeamId",
                table: "AspNetUsers",
                column: "TeamId");

            migrationBuilder.CreateIndex(
                name: "UserNameIndex",
                table: "AspNetUsers",
                column: "NormalizedUserName",
                unique: true,
                filter: "[NormalizedUserName] IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "IX_Projects_AuthorId",
                table: "Projects",
                column: "AuthorId");

            migrationBuilder.CreateIndex(
                name: "IX_Projects_TeamId_Name",
                table: "Projects",
                columns: new[] { "TeamId", "Name" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_Tasks_PerformerId",
                table: "Tasks",
                column: "PerformerId");

            migrationBuilder.CreateIndex(
                name: "IX_Tasks_ProjectId",
                table: "Tasks",
                column: "ProjectId");

            migrationBuilder.CreateIndex(
                name: "IX_Teams_Name",
                table: "Teams",
                column: "Name",
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
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
                name: "ExecutedTasks");

            migrationBuilder.DropTable(
                name: "Tasks");

            migrationBuilder.DropTable(
                name: "AspNetRoles");

            migrationBuilder.DropTable(
                name: "Projects");

            migrationBuilder.DropTable(
                name: "AspNetUsers");

            migrationBuilder.DropTable(
                name: "Teams");
        }
    }
}
