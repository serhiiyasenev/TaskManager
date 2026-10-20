using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace DAL.Migrations
{
    /// <inheritdoc />
    public partial class AddDurableNotificationInbox : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<Guid>(
                name: "NotificationMessageId",
                table: "ExecutedTasks",
                type: "uniqueidentifier",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "NotificationProcessingMarkers",
                columns: table => new
                {
                    MessageId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    CanonicalPayloadHash = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: false),
                    ProcessedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: false),
                    Disposition = table.Column<int>(type: "int", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_NotificationProcessingMarkers", x => x.MessageId);
                    table.CheckConstraint("CK_NotificationProcessingMarkers_Disposition", "[Disposition] IN (0, 1)");
                });

            migrationBuilder.CreateTable(
                name: "UserNotifications",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    OwnerId = table.Column<int>(type: "int", nullable: false),
                    Kind = table.Column<int>(type: "int", nullable: false),
                    TaskId = table.Column<int>(type: "int", nullable: false),
                    ProjectId = table.Column<int>(type: "int", nullable: false),
                    TaskName = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false),
                    ProjectName = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false),
                    OccurredAtUtc = table.Column<DateTime>(type: "datetime2", nullable: false),
                    ReadAtUtc = table.Column<DateTime>(type: "datetime2", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_UserNotifications", x => x.Id);
                    table.CheckConstraint("CK_UserNotifications_Kind", "[Kind] IN (0, 1, 2, 3)");
                    table.ForeignKey(
                        name: "FK_UserNotifications_AspNetUsers_OwnerId",
                        column: x => x.OwnerId,
                        principalTable: "AspNetUsers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_UserNotifications_NotificationProcessingMarkers_Id",
                        column: x => x.Id,
                        principalTable: "NotificationProcessingMarkers",
                        principalColumn: "MessageId",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_ExecutedTasks_NotificationMessageId",
                table: "ExecutedTasks",
                column: "NotificationMessageId",
                unique: true,
                filter: "[NotificationMessageId] IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "IX_NotificationProcessingMarkers_ProcessedAtUtc",
                table: "NotificationProcessingMarkers",
                column: "ProcessedAtUtc");

            migrationBuilder.CreateIndex(
                name: "IX_UserNotifications_OwnerId_OccurredAtUtc_Id",
                table: "UserNotifications",
                columns: new[] { "OwnerId", "OccurredAtUtc", "Id" });

            migrationBuilder.CreateIndex(
                name: "IX_UserNotifications_OwnerId_ReadAtUtc",
                table: "UserNotifications",
                columns: new[] { "OwnerId", "ReadAtUtc" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "UserNotifications");

            migrationBuilder.DropTable(
                name: "NotificationProcessingMarkers");

            migrationBuilder.DropIndex(
                name: "IX_ExecutedTasks_NotificationMessageId",
                table: "ExecutedTasks");

            migrationBuilder.DropColumn(
                name: "NotificationMessageId",
                table: "ExecutedTasks");
        }
    }
}
