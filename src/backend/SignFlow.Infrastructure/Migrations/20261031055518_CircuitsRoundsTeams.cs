using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace SignFlow.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class CircuitsRoundsTeams : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "ConditionalExpression",
                table: "WorkflowSteps");

            migrationBuilder.AddColumn<Guid>(
                name: "ActedByUserId",
                table: "WorkflowSteps",
                type: "uniqueidentifier",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "DelegatedFromStepId",
                table: "WorkflowSteps",
                type: "uniqueidentifier",
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "EscalatedAt",
                table: "WorkflowSteps",
                type: "datetime2",
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "NextReminderAt",
                table: "WorkflowSteps",
                type: "datetime2",
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "OpenedAt",
                table: "WorkflowSteps",
                type: "datetime2",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "ReminderCount",
                table: "WorkflowSteps",
                type: "int",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<Guid>(
                name: "RoundId",
                table: "WorkflowSteps",
                type: "uniqueidentifier",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "StageRule",
                table: "WorkflowSteps",
                type: "int",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<Guid>(
                name: "TeamId",
                table: "WorkflowPresetSteps",
                type: "uniqueidentifier",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "DelegateUserId",
                table: "Vacations",
                type: "uniqueidentifier",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "UserId",
                table: "SigningSessions",
                type: "uniqueidentifier",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "RoundId",
                table: "Signatures",
                type: "uniqueidentifier",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "ActedByUserId",
                table: "Recipients",
                type: "uniqueidentifier",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "DelegatedFromRecipientId",
                table: "Recipients",
                type: "uniqueidentifier",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "DueInWorkingDays",
                table: "Recipients",
                type: "int",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "EscalateAfterDays",
                table: "Recipients",
                type: "int",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "EscalationEmail",
                table: "Recipients",
                type: "nvarchar(256)",
                maxLength: 256,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "PoolRole",
                table: "Recipients",
                type: "nvarchar(100)",
                maxLength: 100,
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "PoolTeamId",
                table: "Recipients",
                type: "uniqueidentifier",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "ReminderEveryDays",
                table: "Recipients",
                type: "int",
                nullable: false,
                defaultValue: 3);

            migrationBuilder.AddColumn<DateTime>(
                name: "RetiredAt",
                table: "Recipients",
                type: "datetime2",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "StageRule",
                table: "Recipients",
                type: "int",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.CreateTable(
                name: "ContractRounds",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    DocumentId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Number = table.Column<int>(type: "int", nullable: false),
                    VersionNumber = table.Column<int>(type: "int", nullable: false),
                    OpenedAt = table.Column<DateTime>(type: "datetime2", nullable: false),
                    ClosedAt = table.Column<DateTime>(type: "datetime2", nullable: true),
                    Outcome = table.Column<int>(type: "int", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "datetime2", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "datetime2", nullable: true),
                    CreatedBy = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: true),
                    UpdatedBy = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: true),
                    IsDeleted = table.Column<bool>(type: "bit", nullable: false),
                    OrganisationId = table.Column<Guid>(type: "uniqueidentifier", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ContractRounds", x => x.Id);
                    table.ForeignKey(
                        name: "FK_ContractRounds_Documents_DocumentId",
                        column: x => x.DocumentId,
                        principalTable: "Documents",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_ContractRounds_Organisations_OrganisationId",
                        column: x => x.OrganisationId,
                        principalTable: "Organisations",
                        principalColumn: "Id");
                });

            migrationBuilder.CreateTable(
                name: "OrganisationHolidays",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Date = table.Column<DateOnly>(type: "date", nullable: false),
                    Name = table.Column<string>(type: "nvarchar(120)", maxLength: 120, nullable: false),
                    EveryYear = table.Column<bool>(type: "bit", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "datetime2", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "datetime2", nullable: true),
                    CreatedBy = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: true),
                    UpdatedBy = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: true),
                    IsDeleted = table.Column<bool>(type: "bit", nullable: false),
                    OrganisationId = table.Column<Guid>(type: "uniqueidentifier", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_OrganisationHolidays", x => x.Id);
                    table.ForeignKey(
                        name: "FK_OrganisationHolidays_Organisations_OrganisationId",
                        column: x => x.OrganisationId,
                        principalTable: "Organisations",
                        principalColumn: "Id");
                });

            migrationBuilder.CreateTable(
                name: "Teams",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Name = table.Column<string>(type: "nvarchar(120)", maxLength: 120, nullable: false),
                    Description = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "datetime2", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "datetime2", nullable: true),
                    CreatedBy = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: true),
                    UpdatedBy = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: true),
                    IsDeleted = table.Column<bool>(type: "bit", nullable: false),
                    OrganisationId = table.Column<Guid>(type: "uniqueidentifier", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Teams", x => x.Id);
                    table.ForeignKey(
                        name: "FK_Teams_Organisations_OrganisationId",
                        column: x => x.OrganisationId,
                        principalTable: "Organisations",
                        principalColumn: "Id");
                });

            migrationBuilder.CreateTable(
                name: "TeamMembers",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    TeamId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    UserId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "datetime2", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "datetime2", nullable: true),
                    CreatedBy = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: true),
                    UpdatedBy = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: true),
                    IsDeleted = table.Column<bool>(type: "bit", nullable: false),
                    OrganisationId = table.Column<Guid>(type: "uniqueidentifier", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_TeamMembers", x => x.Id);
                    table.ForeignKey(
                        name: "FK_TeamMembers_Organisations_OrganisationId",
                        column: x => x.OrganisationId,
                        principalTable: "Organisations",
                        principalColumn: "Id");
                    table.ForeignKey(
                        name: "FK_TeamMembers_Teams_TeamId",
                        column: x => x.TeamId,
                        principalTable: "Teams",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_TeamMembers_Users_UserId",
                        column: x => x.UserId,
                        principalTable: "Users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_WorkflowSteps_RoundId",
                table: "WorkflowSteps",
                column: "RoundId");

            migrationBuilder.CreateIndex(
                name: "IX_WorkflowSteps_Status_NextReminderAt",
                table: "WorkflowSteps",
                columns: new[] { "Status", "NextReminderAt" });

            migrationBuilder.CreateIndex(
                name: "IX_WorkflowPresetSteps_TeamId",
                table: "WorkflowPresetSteps",
                column: "TeamId");

            migrationBuilder.CreateIndex(
                name: "IX_Vacations_DelegateUserId",
                table: "Vacations",
                column: "DelegateUserId");

            migrationBuilder.CreateIndex(
                name: "IX_SigningSessions_UserId",
                table: "SigningSessions",
                column: "UserId");

            migrationBuilder.CreateIndex(
                name: "IX_Recipients_PoolTeamId",
                table: "Recipients",
                column: "PoolTeamId");

            migrationBuilder.CreateIndex(
                name: "IX_ContractRounds_DocumentId_Number",
                table: "ContractRounds",
                columns: new[] { "DocumentId", "Number" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_ContractRounds_OrganisationId",
                table: "ContractRounds",
                column: "OrganisationId");

            migrationBuilder.CreateIndex(
                name: "IX_OrganisationHolidays_OrganisationId",
                table: "OrganisationHolidays",
                column: "OrganisationId");

            migrationBuilder.CreateIndex(
                name: "IX_OrganisationHolidays_OrganisationId_Date",
                table: "OrganisationHolidays",
                columns: new[] { "OrganisationId", "Date" });

            migrationBuilder.CreateIndex(
                name: "IX_TeamMembers_OrganisationId",
                table: "TeamMembers",
                column: "OrganisationId");

            migrationBuilder.CreateIndex(
                name: "IX_TeamMembers_TeamId_UserId",
                table: "TeamMembers",
                columns: new[] { "TeamId", "UserId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_TeamMembers_UserId",
                table: "TeamMembers",
                column: "UserId");

            migrationBuilder.CreateIndex(
                name: "IX_Teams_OrganisationId",
                table: "Teams",
                column: "OrganisationId");

            migrationBuilder.CreateIndex(
                name: "IX_Teams_OrganisationId_Name",
                table: "Teams",
                columns: new[] { "OrganisationId", "Name" },
                unique: true);

            migrationBuilder.AddForeignKey(
                name: "FK_Recipients_Teams_PoolTeamId",
                table: "Recipients",
                column: "PoolTeamId",
                principalTable: "Teams",
                principalColumn: "Id");

            migrationBuilder.AddForeignKey(
                name: "FK_SigningSessions_Users_UserId",
                table: "SigningSessions",
                column: "UserId",
                principalTable: "Users",
                principalColumn: "Id");

            migrationBuilder.AddForeignKey(
                name: "FK_Vacations_Users_DelegateUserId",
                table: "Vacations",
                column: "DelegateUserId",
                principalTable: "Users",
                principalColumn: "Id");

            migrationBuilder.AddForeignKey(
                name: "FK_WorkflowPresetSteps_Teams_TeamId",
                table: "WorkflowPresetSteps",
                column: "TeamId",
                principalTable: "Teams",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_WorkflowSteps_ContractRounds_RoundId",
                table: "WorkflowSteps",
                column: "RoundId",
                principalTable: "ContractRounds",
                principalColumn: "Id");

            // Data (R-M4-4): every department of the organisation's people becomes a team with those people, and so
            // does every department a workflow step names; the steps then point at their team.
            migrationBuilder.Sql(@"
INSERT INTO Teams (Id, Name, Description, CreatedAt, IsDeleted, OrganisationId)
SELECT NEWID(), d.Name, NULL, SYSUTCDATETIME(), 0, d.OrganisationId
FROM (
    SELECT a.OrganisationId, MIN(a.Name) AS Name
    FROM (
        SELECT OrganisationId, LEFT(LTRIM(RTRIM(Department)), 120) AS Name FROM Users
        WHERE Department IS NOT NULL AND LTRIM(RTRIM(Department)) <> ''
        UNION ALL
        SELECT OrganisationId, LEFT(LTRIM(RTRIM(Department)), 120) FROM WorkflowPresetSteps
        WHERE Department IS NOT NULL AND LTRIM(RTRIM(Department)) <> ''
    ) a
    GROUP BY a.OrganisationId, LOWER(a.Name)
) d
WHERE NOT EXISTS (SELECT 1 FROM Teams t WHERE t.OrganisationId = d.OrganisationId AND LOWER(t.Name) = LOWER(d.Name));

INSERT INTO TeamMembers (Id, TeamId, UserId, CreatedAt, IsDeleted, OrganisationId)
SELECT NEWID(), t.Id, u.Id, SYSUTCDATETIME(), 0, u.OrganisationId
FROM Users u
JOIN Teams t ON t.OrganisationId = u.OrganisationId AND LOWER(t.Name) = LOWER(LEFT(LTRIM(RTRIM(u.Department)), 120));

UPDATE s SET TeamId = t.Id
FROM WorkflowPresetSteps s
JOIN Teams t ON t.OrganisationId = s.OrganisationId AND LOWER(t.Name) = LOWER(LEFT(LTRIM(RTRIM(s.Department)), 120))
WHERE s.ApproverKind = 2;
");

            migrationBuilder.DropColumn(
                name: "Department",
                table: "WorkflowPresetSteps");

            // Data (R-M7-2): each contract that was sent has had one round so far; its steps and signatures belong to it.
            migrationBuilder.Sql(@"
INSERT INTO ContractRounds (Id, DocumentId, Number, VersionNumber, OpenedAt, ClosedAt, Outcome, CreatedAt, IsDeleted, OrganisationId)
SELECT NEWID(), w.DocumentId, 1, d.Version, COALESCE(w.StartedAt, w.CreatedAt),
       CASE WHEN d.Status IN (3, 4, 8) THEN COALESCE(w.CompletedAt, w.UpdatedAt, SYSUTCDATETIME()) END,
       CASE d.Status WHEN 3 THEN 1 WHEN 4 THEN 2 WHEN 8 THEN 3 ELSE 0 END,
       SYSUTCDATETIME(), 0, w.OrganisationId
FROM Workflows w
JOIN Documents d ON d.Id = w.DocumentId;

UPDATE s SET RoundId = r.Id
FROM WorkflowSteps s
JOIN Workflows w ON w.Id = s.WorkflowId
JOIN ContractRounds r ON r.DocumentId = w.DocumentId;

-- Steps already open keep running; they are reminded from the next follow-up on.
UPDATE WorkflowSteps SET OpenedAt = COALESCE(UpdatedAt, CreatedAt) WHERE Status = 1;

UPDATE g SET RoundId = r.Id
FROM Signatures g
JOIN Recipients p ON p.Id = g.RecipientId
JOIN ContractRounds r ON r.DocumentId = p.DocumentId;
");

            // Data (R-M4-10): Tunisia's fixed-date public holidays in every organisation's working calendar.
            migrationBuilder.Sql(@"
INSERT INTO OrganisationHolidays (Id, Date, Name, EveryYear, CreatedAt, IsDeleted, OrganisationId)
SELECT NEWID(), h.Date, h.Name, 1, SYSUTCDATETIME(), 0, o.Id
FROM Organisations o
CROSS JOIN (VALUES
    (CAST('2000-01-01' AS date), N'New Year''s Day / Jour de l''an'),
    (CAST('2000-03-20' AS date), N'Independence Day / Fête de l''indépendance'),
    (CAST('2000-04-09' AS date), N'Martyrs'' Day / Fête des martyrs'),
    (CAST('2000-05-01' AS date), N'Labour Day / Fête du travail'),
    (CAST('2000-07-25' AS date), N'Republic Day / Fête de la République'),
    (CAST('2000-08-13' AS date), N'Women''s Day / Fête de la femme'),
    (CAST('2000-10-15' AS date), N'Evacuation Day / Fête de l''évacuation'),
    (CAST('2000-12-17' AS date), N'Revolution Day / Fête de la révolution')
) h(Date, Name);
");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_Recipients_Teams_PoolTeamId",
                table: "Recipients");

            migrationBuilder.DropForeignKey(
                name: "FK_SigningSessions_Users_UserId",
                table: "SigningSessions");

            migrationBuilder.DropForeignKey(
                name: "FK_Vacations_Users_DelegateUserId",
                table: "Vacations");

            migrationBuilder.DropForeignKey(
                name: "FK_WorkflowPresetSteps_Teams_TeamId",
                table: "WorkflowPresetSteps");

            migrationBuilder.DropForeignKey(
                name: "FK_WorkflowSteps_ContractRounds_RoundId",
                table: "WorkflowSteps");

            migrationBuilder.DropTable(
                name: "ContractRounds");

            migrationBuilder.DropTable(
                name: "OrganisationHolidays");

            migrationBuilder.DropTable(
                name: "TeamMembers");

            migrationBuilder.DropTable(
                name: "Teams");

            migrationBuilder.DropIndex(
                name: "IX_WorkflowSteps_RoundId",
                table: "WorkflowSteps");

            migrationBuilder.DropIndex(
                name: "IX_WorkflowSteps_Status_NextReminderAt",
                table: "WorkflowSteps");

            migrationBuilder.DropIndex(
                name: "IX_WorkflowPresetSteps_TeamId",
                table: "WorkflowPresetSteps");

            migrationBuilder.DropIndex(
                name: "IX_Vacations_DelegateUserId",
                table: "Vacations");

            migrationBuilder.DropIndex(
                name: "IX_SigningSessions_UserId",
                table: "SigningSessions");

            migrationBuilder.DropIndex(
                name: "IX_Recipients_PoolTeamId",
                table: "Recipients");

            migrationBuilder.DropColumn(
                name: "ActedByUserId",
                table: "WorkflowSteps");

            migrationBuilder.DropColumn(
                name: "DelegatedFromStepId",
                table: "WorkflowSteps");

            migrationBuilder.DropColumn(
                name: "EscalatedAt",
                table: "WorkflowSteps");

            migrationBuilder.DropColumn(
                name: "NextReminderAt",
                table: "WorkflowSteps");

            migrationBuilder.DropColumn(
                name: "OpenedAt",
                table: "WorkflowSteps");

            migrationBuilder.DropColumn(
                name: "ReminderCount",
                table: "WorkflowSteps");

            migrationBuilder.DropColumn(
                name: "RoundId",
                table: "WorkflowSteps");

            migrationBuilder.DropColumn(
                name: "StageRule",
                table: "WorkflowSteps");

            migrationBuilder.DropColumn(
                name: "TeamId",
                table: "WorkflowPresetSteps");

            migrationBuilder.DropColumn(
                name: "DelegateUserId",
                table: "Vacations");

            migrationBuilder.DropColumn(
                name: "UserId",
                table: "SigningSessions");

            migrationBuilder.DropColumn(
                name: "RoundId",
                table: "Signatures");

            migrationBuilder.DropColumn(
                name: "ActedByUserId",
                table: "Recipients");

            migrationBuilder.DropColumn(
                name: "DelegatedFromRecipientId",
                table: "Recipients");

            migrationBuilder.DropColumn(
                name: "DueInWorkingDays",
                table: "Recipients");

            migrationBuilder.DropColumn(
                name: "EscalateAfterDays",
                table: "Recipients");

            migrationBuilder.DropColumn(
                name: "EscalationEmail",
                table: "Recipients");

            migrationBuilder.DropColumn(
                name: "PoolRole",
                table: "Recipients");

            migrationBuilder.DropColumn(
                name: "PoolTeamId",
                table: "Recipients");

            migrationBuilder.DropColumn(
                name: "ReminderEveryDays",
                table: "Recipients");

            migrationBuilder.DropColumn(
                name: "RetiredAt",
                table: "Recipients");

            migrationBuilder.DropColumn(
                name: "StageRule",
                table: "Recipients");

            migrationBuilder.AddColumn<string>(
                name: "ConditionalExpression",
                table: "WorkflowSteps",
                type: "nvarchar(2000)",
                maxLength: 2000,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "Department",
                table: "WorkflowPresetSteps",
                type: "nvarchar(200)",
                maxLength: 200,
                nullable: true);
        }
    }
}
