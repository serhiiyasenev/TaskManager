using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace DAL.Migrations
{
    /// <inheritdoc />
    public partial class AddTaskReminders : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<DateTime>(
                name: "DueDate",
                table: "Tasks",
                type: "datetime2",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "EscalationDelayMinutes",
                table: "Tasks",
                type: "int",
                nullable: true);

            migrationBuilder.AddColumn<bool>(
                name: "EscalationEnabled",
                table: "Tasks",
                type: "bit",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<DateTime>(
                name: "EscalationSentAt",
                table: "Tasks",
                type: "datetime2",
                nullable: true);

            migrationBuilder.AddColumn<bool>(
                name: "ReminderEnabled",
                table: "Tasks",
                type: "bit",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<int>(
                name: "ReminderOffsetMinutes",
                table: "Tasks",
                type: "int",
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "ReminderSentAt",
                table: "Tasks",
                type: "datetime2",
                nullable: true);

            migrationBuilder.UpdateData(
                table: "Tasks",
                keyColumn: "Id",
                keyValue: 1,
                columns: new[] { "DueDate", "EscalationDelayMinutes", "EscalationEnabled", "EscalationSentAt", "ReminderEnabled", "ReminderOffsetMinutes", "ReminderSentAt" },
                values: new object[] { null, null, false, null, false, null, null });

            migrationBuilder.UpdateData(
                table: "Tasks",
                keyColumn: "Id",
                keyValue: 2,
                columns: new[] { "DueDate", "EscalationDelayMinutes", "EscalationEnabled", "EscalationSentAt", "ReminderEnabled", "ReminderOffsetMinutes", "ReminderSentAt" },
                values: new object[] { null, null, false, null, false, null, null });

            migrationBuilder.UpdateData(
                table: "Tasks",
                keyColumn: "Id",
                keyValue: 3,
                columns: new[] { "DueDate", "EscalationDelayMinutes", "EscalationEnabled", "EscalationSentAt", "ReminderEnabled", "ReminderOffsetMinutes", "ReminderSentAt" },
                values: new object[] { null, null, false, null, false, null, null });

            migrationBuilder.UpdateData(
                table: "Tasks",
                keyColumn: "Id",
                keyValue: 4,
                columns: new[] { "DueDate", "EscalationDelayMinutes", "EscalationEnabled", "EscalationSentAt", "ReminderEnabled", "ReminderOffsetMinutes", "ReminderSentAt" },
                values: new object[] { null, null, false, null, false, null, null });

            migrationBuilder.UpdateData(
                table: "Tasks",
                keyColumn: "Id",
                keyValue: 5,
                columns: new[] { "DueDate", "EscalationDelayMinutes", "EscalationEnabled", "EscalationSentAt", "ReminderEnabled", "ReminderOffsetMinutes", "ReminderSentAt" },
                values: new object[] { null, null, false, null, false, null, null });

            migrationBuilder.UpdateData(
                table: "Tasks",
                keyColumn: "Id",
                keyValue: 6,
                columns: new[] { "DueDate", "EscalationDelayMinutes", "EscalationEnabled", "EscalationSentAt", "ReminderEnabled", "ReminderOffsetMinutes", "ReminderSentAt" },
                values: new object[] { null, null, false, null, false, null, null });

            migrationBuilder.UpdateData(
                table: "Tasks",
                keyColumn: "Id",
                keyValue: 7,
                columns: new[] { "DueDate", "EscalationDelayMinutes", "EscalationEnabled", "EscalationSentAt", "ReminderEnabled", "ReminderOffsetMinutes", "ReminderSentAt" },
                values: new object[] { null, null, false, null, false, null, null });

            migrationBuilder.UpdateData(
                table: "Tasks",
                keyColumn: "Id",
                keyValue: 8,
                columns: new[] { "DueDate", "EscalationDelayMinutes", "EscalationEnabled", "EscalationSentAt", "ReminderEnabled", "ReminderOffsetMinutes", "ReminderSentAt" },
                values: new object[] { null, null, false, null, false, null, null });

            migrationBuilder.UpdateData(
                table: "Tasks",
                keyColumn: "Id",
                keyValue: 9,
                columns: new[] { "DueDate", "EscalationDelayMinutes", "EscalationEnabled", "EscalationSentAt", "ReminderEnabled", "ReminderOffsetMinutes", "ReminderSentAt" },
                values: new object[] { null, null, false, null, false, null, null });

            migrationBuilder.UpdateData(
                table: "Tasks",
                keyColumn: "Id",
                keyValue: 10,
                columns: new[] { "DueDate", "EscalationDelayMinutes", "EscalationEnabled", "EscalationSentAt", "ReminderEnabled", "ReminderOffsetMinutes", "ReminderSentAt" },
                values: new object[] { null, null, false, null, false, null, null });

            migrationBuilder.UpdateData(
                table: "Tasks",
                keyColumn: "Id",
                keyValue: 11,
                columns: new[] { "DueDate", "EscalationDelayMinutes", "EscalationEnabled", "EscalationSentAt", "ReminderEnabled", "ReminderOffsetMinutes", "ReminderSentAt" },
                values: new object[] { null, null, false, null, false, null, null });

            migrationBuilder.UpdateData(
                table: "Tasks",
                keyColumn: "Id",
                keyValue: 12,
                columns: new[] { "DueDate", "EscalationDelayMinutes", "EscalationEnabled", "EscalationSentAt", "ReminderEnabled", "ReminderOffsetMinutes", "ReminderSentAt" },
                values: new object[] { null, null, false, null, false, null, null });

            migrationBuilder.UpdateData(
                table: "Tasks",
                keyColumn: "Id",
                keyValue: 13,
                columns: new[] { "DueDate", "EscalationDelayMinutes", "EscalationEnabled", "EscalationSentAt", "ReminderEnabled", "ReminderOffsetMinutes", "ReminderSentAt" },
                values: new object[] { null, null, false, null, false, null, null });

            migrationBuilder.UpdateData(
                table: "Tasks",
                keyColumn: "Id",
                keyValue: 14,
                columns: new[] { "DueDate", "EscalationDelayMinutes", "EscalationEnabled", "EscalationSentAt", "ReminderEnabled", "ReminderOffsetMinutes", "ReminderSentAt" },
                values: new object[] { null, null, false, null, false, null, null });

            migrationBuilder.UpdateData(
                table: "Tasks",
                keyColumn: "Id",
                keyValue: 15,
                columns: new[] { "DueDate", "EscalationDelayMinutes", "EscalationEnabled", "EscalationSentAt", "ReminderEnabled", "ReminderOffsetMinutes", "ReminderSentAt" },
                values: new object[] { null, null, false, null, false, null, null });

            migrationBuilder.UpdateData(
                table: "Tasks",
                keyColumn: "Id",
                keyValue: 16,
                columns: new[] { "DueDate", "EscalationDelayMinutes", "EscalationEnabled", "EscalationSentAt", "PerformerId", "ReminderEnabled", "ReminderOffsetMinutes", "ReminderSentAt" },
                values: new object[] { null, null, false, null, 10, false, null, null });

            migrationBuilder.UpdateData(
                table: "Tasks",
                keyColumn: "Id",
                keyValue: 17,
                columns: new[] { "DueDate", "EscalationDelayMinutes", "EscalationEnabled", "EscalationSentAt", "ReminderEnabled", "ReminderOffsetMinutes", "ReminderSentAt" },
                values: new object[] { null, null, false, null, false, null, null });

            migrationBuilder.UpdateData(
                table: "Tasks",
                keyColumn: "Id",
                keyValue: 18,
                columns: new[] { "DueDate", "EscalationDelayMinutes", "EscalationEnabled", "EscalationSentAt", "ReminderEnabled", "ReminderOffsetMinutes", "ReminderSentAt" },
                values: new object[] { null, null, false, null, false, null, null });

            migrationBuilder.UpdateData(
                table: "Tasks",
                keyColumn: "Id",
                keyValue: 19,
                columns: new[] { "DueDate", "EscalationDelayMinutes", "EscalationEnabled", "EscalationSentAt", "ReminderEnabled", "ReminderOffsetMinutes", "ReminderSentAt" },
                values: new object[] { null, null, false, null, false, null, null });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "DueDate",
                table: "Tasks");

            migrationBuilder.DropColumn(
                name: "EscalationDelayMinutes",
                table: "Tasks");

            migrationBuilder.DropColumn(
                name: "EscalationEnabled",
                table: "Tasks");

            migrationBuilder.DropColumn(
                name: "EscalationSentAt",
                table: "Tasks");

            migrationBuilder.DropColumn(
                name: "ReminderEnabled",
                table: "Tasks");

            migrationBuilder.DropColumn(
                name: "ReminderOffsetMinutes",
                table: "Tasks");

            migrationBuilder.DropColumn(
                name: "ReminderSentAt",
                table: "Tasks");
        }
    }
}
