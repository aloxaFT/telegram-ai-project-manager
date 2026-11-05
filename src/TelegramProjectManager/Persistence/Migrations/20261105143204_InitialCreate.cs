using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace TelegramProjectManager.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class InitialCreate : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "PollingCursors",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false),
                    NextOffset = table.Column<int>(type: "integer", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_PollingCursors", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "ProcessedUpdates",
                columns: table => new
                {
                    Id = table.Column<long>(type: "bigint", nullable: false),
                    ProcessedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ProcessedUpdates", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "Projects",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    ChatId = table.Column<long>(type: "bigint", nullable: false),
                    Name = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    LastAnalyzedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    Milestones = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Projects", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "AiSuggestions",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    ProjectId = table.Column<Guid>(type: "uuid", nullable: false),
                    ValidatedJson = table.Column<string>(type: "jsonb", nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AiSuggestions", x => x.Id);
                    table.ForeignKey(
                        name: "FK_AiSuggestions_Projects_ProjectId",
                        column: x => x.ProjectId,
                        principalTable: "Projects",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "Decisions",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    ProjectId = table.Column<Guid>(type: "uuid", nullable: false),
                    Text = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: false),
                    SourceMessageId = table.Column<long>(type: "bigint", nullable: false),
                    Fingerprint = table.Column<string>(type: "text", nullable: false),
                    IsConfirmed = table.Column<bool>(type: "boolean", nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Decisions", x => x.Id);
                    table.ForeignKey(
                        name: "FK_Decisions_Projects_ProjectId",
                        column: x => x.ProjectId,
                        principalTable: "Projects",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "Members",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    ProjectId = table.Column<Guid>(type: "uuid", nullable: false),
                    TelegramUserId = table.Column<long>(type: "bigint", nullable: false),
                    DisplayName = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    Username = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: true),
                    IsActive = table.Column<bool>(type: "boolean", nullable: false),
                    JoinedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Members", x => x.Id);
                    table.UniqueConstraint("AK_Members_ProjectId_Id", x => new { x.ProjectId, x.Id });
                    table.ForeignKey(
                        name: "FK_Members_Projects_ProjectId",
                        column: x => x.ProjectId,
                        principalTable: "Projects",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "Messages",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    ProjectId = table.Column<Guid>(type: "uuid", nullable: false),
                    TelegramMessageId = table.Column<long>(type: "bigint", nullable: false),
                    TelegramUserId = table.Column<long>(type: "bigint", nullable: false),
                    Text = table.Column<string>(type: "character varying(4000)", maxLength: 4000, nullable: false),
                    SentAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Messages", x => x.Id);
                    table.ForeignKey(
                        name: "FK_Messages_Projects_ProjectId",
                        column: x => x.ProjectId,
                        principalTable: "Projects",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "Tasks",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    ProjectId = table.Column<Guid>(type: "uuid", nullable: false),
                    Number = table.Column<int>(type: "integer", nullable: false),
                    Title = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    Description = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: false),
                    Fingerprint = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    Status = table.Column<int>(type: "integer", nullable: false),
                    Priority = table.Column<int>(type: "integer", nullable: false),
                    ProposedOwnerId = table.Column<Guid>(type: "uuid", nullable: true),
                    OwnerId = table.Column<Guid>(type: "uuid", nullable: true),
                    DueAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    SourceMessageId = table.Column<long>(type: "bigint", nullable: false),
                    Confidence = table.Column<double>(type: "double precision", nullable: false),
                    MissingInformation = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Tasks", x => x.Id);
                    table.UniqueConstraint("AK_Tasks_ProjectId_Id", x => new { x.ProjectId, x.Id });
                    table.ForeignKey(
                        name: "FK_Tasks_Members_ProjectId_OwnerId",
                        columns: x => new { x.ProjectId, x.OwnerId },
                        principalTable: "Members",
                        principalColumns: new[] { "ProjectId", "Id" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_Tasks_Members_ProjectId_ProposedOwnerId",
                        columns: x => new { x.ProjectId, x.ProposedOwnerId },
                        principalTable: "Members",
                        principalColumns: new[] { "ProjectId", "Id" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_Tasks_Projects_ProjectId",
                        column: x => x.ProjectId,
                        principalTable: "Projects",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "Blockers",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    ProjectId = table.Column<Guid>(type: "uuid", nullable: false),
                    WorkItemId = table.Column<Guid>(type: "uuid", nullable: true),
                    Text = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: false),
                    SourceMessageId = table.Column<long>(type: "bigint", nullable: false),
                    Fingerprint = table.Column<string>(type: "text", nullable: false),
                    IsResolved = table.Column<bool>(type: "boolean", nullable: false),
                    IsConfirmed = table.Column<bool>(type: "boolean", nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Blockers", x => x.Id);
                    table.ForeignKey(
                        name: "FK_Blockers_Projects_ProjectId",
                        column: x => x.ProjectId,
                        principalTable: "Projects",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_Blockers_Tasks_ProjectId_WorkItemId",
                        columns: x => new { x.ProjectId, x.WorkItemId },
                        principalTable: "Tasks",
                        principalColumns: new[] { "ProjectId", "Id" },
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "Notifications",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    ProjectId = table.Column<Guid>(type: "uuid", nullable: false),
                    DeduplicationKey = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    Kind = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    Text = table.Column<string>(type: "character varying(4000)", maxLength: 4000, nullable: false),
                    WorkItemId = table.Column<Guid>(type: "uuid", nullable: true),
                    State = table.Column<int>(type: "integer", nullable: false),
                    Attempts = table.Column<int>(type: "integer", nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    NotBefore = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    SentAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    TelegramMessageId = table.Column<long>(type: "bigint", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Notifications", x => x.Id);
                    table.ForeignKey(
                        name: "FK_Notifications_Projects_ProjectId",
                        column: x => x.ProjectId,
                        principalTable: "Projects",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_Notifications_Tasks_ProjectId_WorkItemId",
                        columns: x => new { x.ProjectId, x.WorkItemId },
                        principalTable: "Tasks",
                        principalColumns: new[] { "ProjectId", "Id" },
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "TaskUpdates",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    ProjectId = table.Column<Guid>(type: "uuid", nullable: false),
                    WorkItemId = table.Column<Guid>(type: "uuid", nullable: false),
                    ActorUserId = table.Column<long>(type: "bigint", nullable: true),
                    PreviousStatus = table.Column<int>(type: "integer", nullable: true),
                    Status = table.Column<int>(type: "integer", nullable: false),
                    Note = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_TaskUpdates", x => x.Id);
                    table.ForeignKey(
                        name: "FK_TaskUpdates_Projects_ProjectId",
                        column: x => x.ProjectId,
                        principalTable: "Projects",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_TaskUpdates_Tasks_ProjectId_WorkItemId",
                        columns: x => new { x.ProjectId, x.WorkItemId },
                        principalTable: "Tasks",
                        principalColumns: new[] { "ProjectId", "Id" },
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_AiSuggestions_CreatedAt",
                table: "AiSuggestions",
                column: "CreatedAt");

            migrationBuilder.CreateIndex(
                name: "IX_AiSuggestions_ProjectId",
                table: "AiSuggestions",
                column: "ProjectId");

            migrationBuilder.CreateIndex(
                name: "IX_Blockers_ProjectId_Fingerprint",
                table: "Blockers",
                columns: new[] { "ProjectId", "Fingerprint" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_Blockers_ProjectId_WorkItemId",
                table: "Blockers",
                columns: new[] { "ProjectId", "WorkItemId" });

            migrationBuilder.CreateIndex(
                name: "IX_Decisions_ProjectId_Fingerprint",
                table: "Decisions",
                columns: new[] { "ProjectId", "Fingerprint" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_Members_ProjectId_TelegramUserId",
                table: "Members",
                columns: new[] { "ProjectId", "TelegramUserId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_Messages_ProjectId_SentAt",
                table: "Messages",
                columns: new[] { "ProjectId", "SentAt" });

            migrationBuilder.CreateIndex(
                name: "IX_Messages_ProjectId_TelegramMessageId",
                table: "Messages",
                columns: new[] { "ProjectId", "TelegramMessageId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_Notifications_ProjectId_DeduplicationKey",
                table: "Notifications",
                columns: new[] { "ProjectId", "DeduplicationKey" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_Notifications_ProjectId_WorkItemId",
                table: "Notifications",
                columns: new[] { "ProjectId", "WorkItemId" });

            migrationBuilder.CreateIndex(
                name: "IX_Notifications_State_NotBefore",
                table: "Notifications",
                columns: new[] { "State", "NotBefore" });

            migrationBuilder.CreateIndex(
                name: "IX_Projects_ChatId",
                table: "Projects",
                column: "ChatId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_Tasks_ProjectId_Fingerprint",
                table: "Tasks",
                columns: new[] { "ProjectId", "Fingerprint" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_Tasks_ProjectId_Number",
                table: "Tasks",
                columns: new[] { "ProjectId", "Number" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_Tasks_ProjectId_OwnerId",
                table: "Tasks",
                columns: new[] { "ProjectId", "OwnerId" });

            migrationBuilder.CreateIndex(
                name: "IX_Tasks_ProjectId_ProposedOwnerId",
                table: "Tasks",
                columns: new[] { "ProjectId", "ProposedOwnerId" });

            migrationBuilder.CreateIndex(
                name: "IX_Tasks_ProjectId_Status_DueAt",
                table: "Tasks",
                columns: new[] { "ProjectId", "Status", "DueAt" });

            migrationBuilder.CreateIndex(
                name: "IX_TaskUpdates_ProjectId_CreatedAt",
                table: "TaskUpdates",
                columns: new[] { "ProjectId", "CreatedAt" });

            migrationBuilder.CreateIndex(
                name: "IX_TaskUpdates_ProjectId_WorkItemId",
                table: "TaskUpdates",
                columns: new[] { "ProjectId", "WorkItemId" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "AiSuggestions");

            migrationBuilder.DropTable(
                name: "Blockers");

            migrationBuilder.DropTable(
                name: "Decisions");

            migrationBuilder.DropTable(
                name: "Messages");

            migrationBuilder.DropTable(
                name: "Notifications");

            migrationBuilder.DropTable(
                name: "PollingCursors");

            migrationBuilder.DropTable(
                name: "ProcessedUpdates");

            migrationBuilder.DropTable(
                name: "TaskUpdates");

            migrationBuilder.DropTable(
                name: "Tasks");

            migrationBuilder.DropTable(
                name: "Members");

            migrationBuilder.DropTable(
                name: "Projects");
        }
    }
}
