using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace DAL.Migrations
{
    /// <inheritdoc />
    public partial class AddAiTaskDrafts : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "Kind",
                table: "NotificationOutbox",
                type: "int",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.CreateTable(
                name: "AiDailyUsage",
                columns: table => new
                {
                    ScopeKey = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: false),
                    DayUtc = table.Column<DateOnly>(type: "date", nullable: false),
                    ReservedTokens = table.Column<long>(type: "bigint", nullable: false),
                    Version = table.Column<Guid>(type: "uniqueidentifier", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AiDailyUsage", x => new { x.ScopeKey, x.DayUtc });
                });

            migrationBuilder.CreateTable(
                name: "AiTaskCommitReceipts",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    OwnerId = table.Column<int>(type: "int", nullable: false),
                    KeyHash = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: false),
                    PayloadHash = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: false),
                    SourceDraftId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    ProjectId = table.Column<int>(type: "int", nullable: false),
                    ExpectedDraftVersion = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    ResultDraftVersion = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    ResponseJson = table.Column<string>(type: "nvarchar(max)", maxLength: 65536, nullable: false),
                    CreatedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: false),
                    ReplayExpiresAtUtc = table.Column<DateTime>(type: "datetime2", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AiTaskCommitReceipts", x => x.Id);
                    table.ForeignKey(
                        name: "FK_AiTaskCommitReceipts_AspNetUsers_OwnerId",
                        column: x => x.OwnerId,
                        principalTable: "AspNetUsers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "AiTaskDrafts",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    OwnerId = table.Column<int>(type: "int", nullable: false),
                    ProjectId = table.Column<int>(type: "int", nullable: false),
                    Version = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    NotesText = table.Column<string>(type: "nvarchar(max)", maxLength: 20000, nullable: true),
                    Locale = table.Column<string>(type: "nvarchar(8)", maxLength: 8, nullable: false),
                    TimeZoneId = table.Column<string>(type: "nvarchar(128)", maxLength: 128, nullable: false),
                    ReferenceDate = table.Column<DateOnly>(type: "date", nullable: false),
                    CreatedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: false),
                    ExpiresAtUtc = table.Column<DateTime>(type: "datetime2", nullable: false),
                    RetainUntilUtc = table.Column<DateTime>(type: "datetime2", nullable: false),
                    DeletedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AiTaskDrafts", x => x.Id);
                    table.ForeignKey(
                        name: "FK_AiTaskDrafts_AspNetUsers_OwnerId",
                        column: x => x.OwnerId,
                        principalTable: "AspNetUsers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_AiTaskDrafts_Projects_ProjectId",
                        column: x => x.ProjectId,
                        principalTable: "Projects",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "AiTaskDraftItems",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    DraftId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Ordinal = table.Column<int>(type: "int", nullable: false),
                    CandidateJson = table.Column<string>(type: "nvarchar(max)", maxLength: 131072, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AiTaskDraftItems", x => x.Id);
                    table.ForeignKey(
                        name: "FK_AiTaskDraftItems_AiTaskDrafts_DraftId",
                        column: x => x.DraftId,
                        principalTable: "AiTaskDrafts",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "AiTaskMaterializations",
                columns: table => new
                {
                    DraftItemId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    OriginalTaskId = table.Column<int>(type: "int", nullable: false),
                    LiveTaskId = table.Column<int>(type: "int", nullable: true),
                    OutboxMessageId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    CreatedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AiTaskMaterializations", x => x.DraftItemId);
                    table.ForeignKey(
                        name: "FK_AiTaskMaterializations_AiTaskDraftItems_DraftItemId",
                        column: x => x.DraftItemId,
                        principalTable: "AiTaskDraftItems",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_AiTaskMaterializations_Tasks_LiveTaskId",
                        column: x => x.LiveTaskId,
                        principalTable: "Tasks",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.SetNull);
                });

            migrationBuilder.CreateIndex(
                name: "IX_AiTaskCommitReceipts_OwnerId_KeyHash",
                table: "AiTaskCommitReceipts",
                columns: new[] { "OwnerId", "KeyHash" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_AiTaskCommitReceipts_ReplayExpiresAtUtc",
                table: "AiTaskCommitReceipts",
                column: "ReplayExpiresAtUtc");

            migrationBuilder.CreateIndex(
                name: "IX_AiTaskDraftItems_DraftId_Ordinal",
                table: "AiTaskDraftItems",
                columns: new[] { "DraftId", "Ordinal" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_AiTaskDrafts_ExpiresAtUtc",
                table: "AiTaskDrafts",
                column: "ExpiresAtUtc");

            migrationBuilder.CreateIndex(
                name: "IX_AiTaskDrafts_OwnerId_CreatedAtUtc",
                table: "AiTaskDrafts",
                columns: new[] { "OwnerId", "CreatedAtUtc" });

            migrationBuilder.CreateIndex(
                name: "IX_AiTaskDrafts_ProjectId",
                table: "AiTaskDrafts",
                column: "ProjectId");

            migrationBuilder.CreateIndex(
                name: "IX_AiTaskDrafts_RetainUntilUtc",
                table: "AiTaskDrafts",
                column: "RetainUntilUtc");

            migrationBuilder.CreateIndex(
                name: "IX_AiTaskMaterializations_LiveTaskId",
                table: "AiTaskMaterializations",
                column: "LiveTaskId");

            migrationBuilder.CreateIndex(
                name: "IX_AiTaskMaterializations_OutboxMessageId",
                table: "AiTaskMaterializations",
                column: "OutboxMessageId",
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "AiDailyUsage");

            migrationBuilder.DropTable(
                name: "AiTaskCommitReceipts");

            migrationBuilder.DropTable(
                name: "AiTaskMaterializations");

            migrationBuilder.DropTable(
                name: "AiTaskDraftItems");

            migrationBuilder.DropTable(
                name: "AiTaskDrafts");

            migrationBuilder.DropColumn(
                name: "Kind",
                table: "NotificationOutbox");
        }
    }
}
