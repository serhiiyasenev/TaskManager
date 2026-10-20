using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace DAL.Migrations;

public partial class AddNotificationOutbox : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.AddColumn<Guid>("ReminderNotificationId", "Tasks", type: "uniqueidentifier", nullable: true);
        migrationBuilder.AddColumn<Guid>("EscalationNotificationId", "Tasks", type: "uniqueidentifier", nullable: true);
        migrationBuilder.CreateTable(
            name: "NotificationOutbox",
            columns: table => new
            {
                Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                TaskId = table.Column<int>(type: "int", nullable: false),
                IsEscalation = table.Column<bool>(type: "bit", nullable: false),
                QueueName = table.Column<string>(type: "nvarchar(256)", maxLength: 256, nullable: false),
                Payload = table.Column<string>(type: "nvarchar(max)", nullable: false),
                CreatedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: false),
                PublishedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: true),
                Status = table.Column<int>(type: "int", nullable: false),
                LockId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                LockedUntilUtc = table.Column<DateTime>(type: "datetime2", nullable: true)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_NotificationOutbox", x => x.Id);
                table.ForeignKey("FK_NotificationOutbox_Tasks_TaskId", x => x.TaskId,
                    "Tasks", "Id", onDelete: ReferentialAction.Cascade);
            });
        migrationBuilder.CreateIndex("IX_NotificationOutbox_TaskId", "NotificationOutbox", "TaskId");
        migrationBuilder.CreateIndex("IX_NotificationOutbox_Status_LockedUntilUtc_CreatedAtUtc", "NotificationOutbox",
            new[] { "Status", "LockedUntilUtc", "CreatedAtUtc" });
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        // Drain pending entries before rollback: dropping this table discards them.
        migrationBuilder.DropTable("NotificationOutbox");
        migrationBuilder.DropColumn("ReminderNotificationId", "Tasks");
        migrationBuilder.DropColumn("EscalationNotificationId", "Tasks");
    }
}
