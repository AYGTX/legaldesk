using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace SignFlow.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class ContractLifecycle : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "CalendarTokenHash",
                table: "Users",
                type: "nvarchar(max)",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "AlertOffsets",
                table: "Organisations",
                type: "nvarchar(64)",
                maxLength: 64,
                nullable: false,
                defaultValue: "60,30,14,7,0");

            migrationBuilder.AddColumn<DateTime>(
                name: "LastLifecycleSweepOn",
                table: "Organisations",
                type: "datetime2",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "ContractEvents",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    DocumentId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Kind = table.Column<int>(type: "int", nullable: false),
                    At = table.Column<DateTime>(type: "datetime2", nullable: false),
                    ById = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    Details = table.Column<string>(type: "nvarchar(1000)", maxLength: 1000, nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "datetime2", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "datetime2", nullable: true),
                    CreatedBy = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: true),
                    UpdatedBy = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: true),
                    IsDeleted = table.Column<bool>(type: "bit", nullable: false),
                    OrganisationId = table.Column<Guid>(type: "uniqueidentifier", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ContractEvents", x => x.Id);
                    table.ForeignKey(
                        name: "FK_ContractEvents_Documents_DocumentId",
                        column: x => x.DocumentId,
                        principalTable: "Documents",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_ContractEvents_Organisations_OrganisationId",
                        column: x => x.OrganisationId,
                        principalTable: "Organisations",
                        principalColumn: "Id");
                });

            migrationBuilder.CreateTable(
                name: "ContractTerms",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    DocumentId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    StartDate = table.Column<DateTime>(type: "datetime2", nullable: true),
                    InitialEndDate = table.Column<DateTime>(type: "datetime2", nullable: true),
                    CurrentEndDate = table.Column<DateTime>(type: "datetime2", nullable: true),
                    RenewalMode = table.Column<int>(type: "int", nullable: false),
                    RenewalMonths = table.Column<int>(type: "int", nullable: true),
                    NoticeAmount = table.Column<int>(type: "int", nullable: true),
                    NoticeUnit = table.Column<int>(type: "int", nullable: false),
                    NoticeForm = table.Column<int>(type: "int", nullable: true),
                    ConfirmedById = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    ConfirmedAt = table.Column<DateTime>(type: "datetime2", nullable: true),
                    LastAlertAnchor = table.Column<DateTime>(type: "datetime2", nullable: true),
                    LastAlertOffset = table.Column<int>(type: "int", nullable: true),
                    EndAlertSent = table.Column<bool>(type: "bit", nullable: false),
                    Decision = table.Column<int>(type: "int", nullable: false),
                    DecisionAt = table.Column<DateTime>(type: "datetime2", nullable: true),
                    DecisionById = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    NoticeSentAt = table.Column<DateTime>(type: "datetime2", nullable: true),
                    NoticeProofPath = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "datetime2", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "datetime2", nullable: true),
                    CreatedBy = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: true),
                    UpdatedBy = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: true),
                    IsDeleted = table.Column<bool>(type: "bit", nullable: false),
                    OrganisationId = table.Column<Guid>(type: "uniqueidentifier", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ContractTerms", x => x.Id);
                    table.ForeignKey(
                        name: "FK_ContractTerms_Documents_DocumentId",
                        column: x => x.DocumentId,
                        principalTable: "Documents",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_ContractTerms_Organisations_OrganisationId",
                        column: x => x.OrganisationId,
                        principalTable: "Organisations",
                        principalColumn: "Id");
                });

            migrationBuilder.CreateTable(
                name: "Obligations",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    DocumentId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Title = table.Column<string>(type: "nvarchar(300)", maxLength: 300, nullable: false),
                    Kind = table.Column<int>(type: "int", nullable: false),
                    DueDate = table.Column<DateTime>(type: "datetime2", nullable: true),
                    Recurrence = table.Column<int>(type: "int", nullable: false),
                    OwnerId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    Quote = table.Column<string>(type: "nvarchar(4000)", maxLength: 4000, nullable: true),
                    Source = table.Column<int>(type: "int", nullable: false),
                    Status = table.Column<int>(type: "int", nullable: false),
                    Confirmed = table.Column<bool>(type: "bit", nullable: false),
                    LastReminderFor = table.Column<DateTime>(type: "datetime2", nullable: true),
                    LastReminderOffset = table.Column<int>(type: "int", nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "datetime2", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "datetime2", nullable: true),
                    CreatedBy = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: true),
                    UpdatedBy = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: true),
                    IsDeleted = table.Column<bool>(type: "bit", nullable: false),
                    OrganisationId = table.Column<Guid>(type: "uniqueidentifier", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Obligations", x => x.Id);
                    table.ForeignKey(
                        name: "FK_Obligations_Documents_DocumentId",
                        column: x => x.DocumentId,
                        principalTable: "Documents",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_Obligations_Organisations_OrganisationId",
                        column: x => x.OrganisationId,
                        principalTable: "Organisations",
                        principalColumn: "Id");
                });

            migrationBuilder.CreateIndex(
                name: "IX_ContractEvents_DocumentId_At",
                table: "ContractEvents",
                columns: new[] { "DocumentId", "At" });

            migrationBuilder.CreateIndex(
                name: "IX_ContractEvents_OrganisationId",
                table: "ContractEvents",
                column: "OrganisationId");

            migrationBuilder.CreateIndex(
                name: "IX_ContractTerms_CurrentEndDate",
                table: "ContractTerms",
                column: "CurrentEndDate");

            migrationBuilder.CreateIndex(
                name: "IX_ContractTerms_DocumentId",
                table: "ContractTerms",
                column: "DocumentId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_ContractTerms_OrganisationId",
                table: "ContractTerms",
                column: "OrganisationId");

            migrationBuilder.CreateIndex(
                name: "IX_Obligations_DocumentId_DueDate",
                table: "Obligations",
                columns: new[] { "DocumentId", "DueDate" });

            migrationBuilder.CreateIndex(
                name: "IX_Obligations_OrganisationId",
                table: "Obligations",
                column: "OrganisationId");

            // Data (M10): each recorded term gets its own record; a confirmed obligation joins the list. An obligation date
            // nobody confirmed was the model's guess and is not kept (R-M10-6). An automatic renewal renews for the length
            // of the original term (or a year when its start is unknown); the rung already sent is carried over so nobody
            // is alerted twice.
            migrationBuilder.Sql(@"
INSERT INTO ContractTerms (Id, DocumentId, StartDate, InitialEndDate, CurrentEndDate, RenewalMode, RenewalMonths, NoticeAmount, NoticeUnit,
    NoticeForm, ConfirmedById, ConfirmedAt, LastAlertAnchor, LastAlertOffset, EndAlertSent, Decision, DecisionAt, DecisionById,
    NoticeSentAt, NoticeProofPath, CreatedAt, IsDeleted, OrganisationId)
SELECT NEWID(), d.Id, d.ContractStartDate, d.ContractEndDate, d.ContractEndDate,
       CASE WHEN d.AutoRenews = 1 THEN 1 ELSE 0 END,
       CASE WHEN d.AutoRenews = 1 THEN COALESCE(NULLIF(DATEDIFF(month, d.ContractStartDate, d.ContractEndDate), 0), 12) END,
       NULLIF(d.NoticePeriodDays, 0), 0, NULL,
       CASE WHEN d.TermConfirmed = 1 THEN d.OwnerId END,
       CASE WHEN d.TermConfirmed = 1 THEN COALESCE(d.UpdatedAt, d.CreatedAt) END,
       CASE WHEN d.LastTermAlertStage IN ('T90','T60','T30','T7','NoticeDeadline') AND d.ContractEndDate IS NOT NULL
            THEN DATEADD(day, -COALESCE(d.NoticePeriodDays, 0), d.ContractEndDate) END,
       CASE d.LastTermAlertStage WHEN 'T90' THEN 60 WHEN 'T60' THEN 60 WHEN 'T30' THEN 30 WHEN 'T7' THEN 7 WHEN 'NoticeDeadline' THEN 0 END,
       CASE WHEN d.LastTermAlertStage = 'Expired' THEN 1 ELSE 0 END,
       0, NULL, NULL, NULL, NULL, SYSUTCDATETIME(), 0, d.OrganisationId
FROM Documents d
WHERE d.ContractEndDate IS NOT NULL OR d.ContractStartDate IS NOT NULL;

INSERT INTO Obligations (Id, DocumentId, Title, Kind, DueDate, Recurrence, OwnerId, Quote, Source, Status, Confirmed,
    LastReminderFor, LastReminderOffset, CreatedAt, IsDeleted, OrganisationId)
SELECT NEWID(), d.Id, COALESCE(NULLIF(d.ObligationType, ''), 'Obligation'),
       CASE LOWER(d.ObligationType) WHEN 'payment' THEN 0 WHEN 'report' THEN 1 WHEN 'audit' THEN 3 WHEN 'renewal' THEN 4 ELSE 5 END,
       d.ObligationDueDate, 0, d.OwnerId, NULL, 0, 0, 1, NULL, NULL, SYSUTCDATETIME(), 0, d.OrganisationId
FROM Documents d
WHERE d.ObligationConfirmed = 1 AND d.ObligationDueDate IS NOT NULL;
");

            migrationBuilder.DropColumn(
                name: "AutoRenews",
                table: "Documents");

            migrationBuilder.DropColumn(
                name: "ContractEndDate",
                table: "Documents");

            migrationBuilder.DropColumn(
                name: "ContractStartDate",
                table: "Documents");

            migrationBuilder.DropColumn(
                name: "LastObligationReminderSentAt",
                table: "Documents");

            migrationBuilder.DropColumn(
                name: "LastTermAlertSentAt",
                table: "Documents");

            migrationBuilder.DropColumn(
                name: "LastTermAlertStage",
                table: "Documents");

            migrationBuilder.DropColumn(
                name: "NoticePeriodDays",
                table: "Documents");

            migrationBuilder.DropColumn(
                name: "ObligationConfirmed",
                table: "Documents");

            migrationBuilder.DropColumn(
                name: "ObligationDueDate",
                table: "Documents");

            migrationBuilder.DropColumn(
                name: "ObligationRationale",
                table: "Documents");

            migrationBuilder.DropColumn(
                name: "ObligationType",
                table: "Documents");

            migrationBuilder.DropColumn(
                name: "TermConfirmed",
                table: "Documents");

        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "CalendarTokenHash",
                table: "Users");

            migrationBuilder.DropColumn(
                name: "AlertOffsets",
                table: "Organisations");

            migrationBuilder.DropColumn(
                name: "LastLifecycleSweepOn",
                table: "Organisations");

            migrationBuilder.AddColumn<bool>(
                name: "AutoRenews",
                table: "Documents",
                type: "bit",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<DateTime>(
                name: "ContractEndDate",
                table: "Documents",
                type: "datetime2",
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "ContractStartDate",
                table: "Documents",
                type: "datetime2",
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "LastObligationReminderSentAt",
                table: "Documents",
                type: "datetime2",
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "LastTermAlertSentAt",
                table: "Documents",
                type: "datetime2",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "LastTermAlertStage",
                table: "Documents",
                type: "nvarchar(32)",
                maxLength: 32,
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "NoticePeriodDays",
                table: "Documents",
                type: "int",
                nullable: true);

            migrationBuilder.AddColumn<bool>(
                name: "ObligationConfirmed",
                table: "Documents",
                type: "bit",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<DateTime>(
                name: "ObligationDueDate",
                table: "Documents",
                type: "datetime2",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "ObligationRationale",
                table: "Documents",
                type: "nvarchar(max)",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "ObligationType",
                table: "Documents",
                type: "nvarchar(max)",
                nullable: true);

            migrationBuilder.AddColumn<bool>(
                name: "TermConfirmed",
                table: "Documents",
                type: "bit",
                nullable: false,
                defaultValue: false);

            // Back to the columns on the contract: the term as recorded, and the first upcoming confirmed obligation.
            migrationBuilder.Sql(@"
UPDATE d SET ContractStartDate = t.StartDate, ContractEndDate = t.CurrentEndDate,
    AutoRenews = CASE WHEN t.RenewalMode = 1 THEN 1 ELSE 0 END,
    NoticePeriodDays = CASE WHEN t.NoticeUnit = 1 THEN t.NoticeAmount * 30 ELSE t.NoticeAmount END,
    TermConfirmed = CASE WHEN t.ConfirmedAt IS NULL THEN 0 ELSE 1 END
FROM Documents d JOIN ContractTerms t ON t.DocumentId = d.Id;

UPDATE d SET ObligationDueDate = o.DueDate, ObligationType = o.Title, ObligationConfirmed = 1
FROM Documents d
CROSS APPLY (SELECT TOP 1 * FROM Obligations x WHERE x.DocumentId = d.Id AND x.Confirmed = 1 AND x.IsDeleted = 0 ORDER BY x.DueDate) o;
");

            migrationBuilder.DropTable(
                name: "ContractEvents");

            migrationBuilder.DropTable(
                name: "ContractTerms");

            migrationBuilder.DropTable(
                name: "Obligations");

        }
    }
}
