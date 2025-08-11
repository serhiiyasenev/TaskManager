using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

#pragma warning disable CA1814 // Prefer jagged arrays over multidimensional

namespace DAL.Migrations
{
    /// <inheritdoc />
    public partial class FixStaticSeed : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
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
                    Name = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "datetime2", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Teams", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "Users",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    TeamId = table.Column<int>(type: "int", nullable: true),
                    FirstName = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false),
                    LastName = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false),
                    Email = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    RegisteredAt = table.Column<DateTime>(type: "datetime2", nullable: false),
                    BirthDay = table.Column<DateTime>(type: "datetime2", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Users", x => x.Id);
                    table.ForeignKey(
                        name: "FK_Users_Teams_TeamId",
                        column: x => x.TeamId,
                        principalTable: "Teams",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "Projects",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    AuthorId = table.Column<int>(type: "int", nullable: false),
                    TeamId = table.Column<int>(type: "int", nullable: false),
                    Name = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    Description = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "datetime2", nullable: false),
                    Deadline = table.Column<DateTime>(type: "datetime2", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Projects", x => x.Id);
                    table.ForeignKey(
                        name: "FK_Projects_Teams_TeamId",
                        column: x => x.TeamId,
                        principalTable: "Teams",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_Projects_Users_AuthorId",
                        column: x => x.AuthorId,
                        principalTable: "Users",
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
                    Name = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    Description = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    State = table.Column<int>(type: "int", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "datetime2", nullable: false),
                    FinishedAt = table.Column<DateTime>(type: "datetime2", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Tasks", x => x.Id);
                    table.ForeignKey(
                        name: "FK_Tasks_Projects_ProjectId",
                        column: x => x.ProjectId,
                        principalTable: "Projects",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_Tasks_Users_PerformerId",
                        column: x => x.PerformerId,
                        principalTable: "Users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.InsertData(
                table: "Teams",
                columns: new[] { "Id", "CreatedAt", "Name" },
                values: new object[,]
                {
                    { 1, new DateTime(2025, 7, 29, 0, 0, 0, 0, DateTimeKind.Utc), "Team 1" },
                    { 2, new DateTime(2025, 7, 29, 0, 0, 0, 0, DateTimeKind.Utc), "Team 2" },
                    { 3, new DateTime(2025, 7, 29, 0, 0, 0, 0, DateTimeKind.Utc), "Team 3" }
                });

            migrationBuilder.InsertData(
                table: "Users",
                columns: new[] { "Id", "BirthDay", "Email", "FirstName", "LastName", "RegisteredAt", "TeamId" },
                values: new object[,]
                {
                    { 1, new DateTime(1991, 1, 1, 0, 0, 0, 0, DateTimeKind.Unspecified), "john.A.doe@gmail.com", "John", "A", new DateTime(2025, 7, 29, 0, 0, 0, 0, DateTimeKind.Utc), 1 },
                    { 2, new DateTime(1992, 2, 2, 0, 0, 0, 0, DateTimeKind.Unspecified), "john.B.doe@gmail.com", "John", "B", new DateTime(2025, 7, 29, 0, 0, 0, 0, DateTimeKind.Utc), 2 },
                    { 3, new DateTime(1993, 3, 3, 0, 0, 0, 0, DateTimeKind.Unspecified), "john.C.doe@gmail.com", "John", "C", new DateTime(2025, 7, 29, 0, 0, 0, 0, DateTimeKind.Utc), 3 },
                    { 4, new DateTime(1994, 4, 4, 0, 0, 0, 0, DateTimeKind.Unspecified), "john.D.doe@gmail.com", "John", "D", new DateTime(2025, 7, 29, 0, 0, 0, 0, DateTimeKind.Utc), 1 },
                    { 5, new DateTime(1995, 5, 5, 0, 0, 0, 0, DateTimeKind.Unspecified), "john.E.doe@gmail.com", "John", "E", new DateTime(2025, 7, 29, 0, 0, 0, 0, DateTimeKind.Utc), 2 }
                });

            migrationBuilder.InsertData(
                table: "Projects",
                columns: new[] { "Id", "AuthorId", "CreatedAt", "Deadline", "Description", "Name", "TeamId" },
                values: new object[,]
                {
                    { 1, 1, new DateTime(2025, 8, 1, 0, 0, 0, 0, DateTimeKind.Utc), new DateTime(2025, 8, 11, 0, 0, 0, 0, DateTimeKind.Utc), "Description 1", "Project 1", 1 },
                    { 2, 2, new DateTime(2025, 8, 1, 0, 0, 0, 0, DateTimeKind.Utc), new DateTime(2025, 8, 16, 0, 0, 0, 0, DateTimeKind.Utc), "Description 2", "Project 2", 2 },
                    { 3, 3, new DateTime(2025, 8, 1, 0, 0, 0, 0, DateTimeKind.Utc), new DateTime(2025, 8, 21, 0, 0, 0, 0, DateTimeKind.Utc), "Description 3", "Project 3", 3 }
                });

            migrationBuilder.InsertData(
                table: "Tasks",
                columns: new[] { "Id", "CreatedAt", "Description", "FinishedAt", "Name", "PerformerId", "ProjectId", "State" },
                values: new object[,]
                {
                    { 1, new DateTime(2025, 8, 1, 0, 0, 0, 0, DateTimeKind.Utc), "Task Description 1", null, "Task 1", 1, 1, 0 },
                    { 2, new DateTime(2025, 8, 1, 0, 0, 0, 0, DateTimeKind.Utc), "Task Description 2", null, "Task 2", 2, 2, 1 },
                    { 3, new DateTime(2025, 7, 29, 0, 0, 0, 0, DateTimeKind.Utc), "Task Description 3", new DateTime(2025, 8, 1, 0, 0, 0, 0, DateTimeKind.Utc), "Task 3", 3, 3, 2 },
                    { 4, new DateTime(2025, 8, 1, 0, 0, 0, 0, DateTimeKind.Utc), "Task Description 4", null, "Task 4", 1, 1, 0 },
                    { 5, new DateTime(2025, 7, 29, 0, 0, 0, 0, DateTimeKind.Utc), "Task Description 5", new DateTime(2025, 8, 1, 0, 0, 0, 0, DateTimeKind.Utc), "Task 5", 2, 2, 3 }
                });

            migrationBuilder.CreateIndex(
                name: "IX_Projects_AuthorId",
                table: "Projects",
                column: "AuthorId");

            migrationBuilder.CreateIndex(
                name: "IX_Projects_TeamId",
                table: "Projects",
                column: "TeamId");

            migrationBuilder.CreateIndex(
                name: "IX_Tasks_PerformerId",
                table: "Tasks",
                column: "PerformerId");

            migrationBuilder.CreateIndex(
                name: "IX_Tasks_ProjectId",
                table: "Tasks",
                column: "ProjectId");

            migrationBuilder.CreateIndex(
                name: "IX_Users_TeamId",
                table: "Users",
                column: "TeamId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "ExecutedTasks");

            migrationBuilder.DropTable(
                name: "Tasks");

            migrationBuilder.DropTable(
                name: "Projects");

            migrationBuilder.DropTable(
                name: "Users");

            migrationBuilder.DropTable(
                name: "Teams");
        }
    }
}
