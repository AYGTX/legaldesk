using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace SignFlow.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class ReviewRuns : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "AiComplianceAnalysisGeneratedAt",
                table: "Documents");

            migrationBuilder.DropColumn(
                name: "AiComplianceAnalysisJson",
                table: "Documents");

            migrationBuilder.DropColumn(
                name: "AiRiskAnalysisGeneratedAt",
                table: "Documents");

            migrationBuilder.DropColumn(
                name: "AiRiskAnalysisJson",
                table: "Documents");

            migrationBuilder.RenameColumn(
                name: "ResolvedByUserId",
                table: "DocumentReadinessItems",
                newName: "DecidedByUserId");

            migrationBuilder.RenameColumn(
                name: "ResolvedAt",
                table: "DocumentReadinessItems",
                newName: "DecidedAt");

            migrationBuilder.RenameColumn(
                name: "ResolutionNote",
                table: "DocumentReadinessItems",
                newName: "Reason");

            migrationBuilder.AddColumn<Guid>(
                name: "LastReviewRunId",
                table: "Documents",
                type: "uniqueidentifier",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "RiskScore",
                table: "Documents",
                type: "int",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "ArticlesJson",
                table: "DocumentReadinessItems",
                type: "nvarchar(max)",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "ClauseReference",
                table: "DocumentReadinessItems",
                type: "nvarchar(300)",
                maxLength: 300,
                nullable: true);

            migrationBuilder.AddColumn<double>(
                name: "Confidence",
                table: "DocumentReadinessItems",
                type: "float",
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "RuleApprovedAt",
                table: "DocumentReadinessItems",
                type: "datetime2",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "LastSeenRunId",
                table: "DocumentReadinessItems",
                type: "uniqueidentifier",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "DetectionKind",
                table: "DocumentReadinessItems",
                type: "nvarchar(32)",
                maxLength: 32,
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "FirstFoundInVersion",
                table: "DocumentReadinessItems",
                type: "int",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "FixedInVersion",
                table: "DocumentReadinessItems",
                type: "int",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "LastSeenVersion",
                table: "DocumentReadinessItems",
                type: "int",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "QuotesJson",
                table: "DocumentReadinessItems",
                type: "nvarchar(max)",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "RuleApprovedBy",
                table: "DocumentReadinessItems",
                type: "nvarchar(300)",
                maxLength: 300,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "RuleKey",
                table: "DocumentReadinessItems",
                type: "nvarchar(80)",
                maxLength: 80,
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "RuleVersion",
                table: "DocumentReadinessItems",
                type: "int",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "OrganisationRuleSettings",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    RuleKey = table.Column<string>(type: "nvarchar(80)", maxLength: 80, nullable: false),
                    Enabled = table.Column<bool>(type: "bit", nullable: false),
                    SeverityOverride = table.Column<string>(type: "nvarchar(32)", maxLength: 32, nullable: true),
                    ChangedByUserId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    ChangedAt = table.Column<DateTime>(type: "datetime2", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "datetime2", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "datetime2", nullable: true),
                    CreatedBy = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: true),
                    UpdatedBy = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: true),
                    IsDeleted = table.Column<bool>(type: "bit", nullable: false),
                    OrganisationId = table.Column<Guid>(type: "uniqueidentifier", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_OrganisationRuleSettings", x => x.Id);
                    table.ForeignKey(
                        name: "FK_OrganisationRuleSettings_Organisations_OrganisationId",
                        column: x => x.OrganisationId,
                        principalTable: "Organisations",
                        principalColumn: "Id");
                });

            migrationBuilder.CreateTable(
                name: "ReviewRuns",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    DocumentId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    VersionNumber = table.Column<int>(type: "int", nullable: false),
                    Status = table.Column<int>(type: "int", nullable: false),
                    RequestedByUserId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    AsOfDate = table.Column<DateTime>(type: "datetime2", nullable: false),
                    StartedAt = table.Column<DateTime>(type: "datetime2", nullable: true),
                    FinishedAt = table.Column<DateTime>(type: "datetime2", nullable: true),
                    GridVersion = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: true),
                    FindingCount = table.Column<int>(type: "int", nullable: false),
                    ErrorCode = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: true),
                    Error = table.Column<string>(type: "nvarchar(1000)", maxLength: 1000, nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "datetime2", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "datetime2", nullable: true),
                    CreatedBy = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: true),
                    UpdatedBy = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: true),
                    IsDeleted = table.Column<bool>(type: "bit", nullable: false),
                    OrganisationId = table.Column<Guid>(type: "uniqueidentifier", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ReviewRuns", x => x.Id);
                    table.ForeignKey(
                        name: "FK_ReviewRuns_Documents_DocumentId",
                        column: x => x.DocumentId,
                        principalTable: "Documents",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_ReviewRuns_Organisations_OrganisationId",
                        column: x => x.OrganisationId,
                        principalTable: "Organisations",
                        principalColumn: "Id");
                });

            migrationBuilder.CreateIndex(
                name: "IX_DocumentReadinessItems_LastSeenRunId",
                table: "DocumentReadinessItems",
                column: "LastSeenRunId");

            migrationBuilder.CreateIndex(
                name: "IX_OrganisationRuleSettings_OrganisationId",
                table: "OrganisationRuleSettings",
                column: "OrganisationId");

            migrationBuilder.CreateIndex(
                name: "IX_OrganisationRuleSettings_OrganisationId_RuleKey",
                table: "OrganisationRuleSettings",
                columns: new[] { "OrganisationId", "RuleKey" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_ReviewRuns_DocumentId_CreatedAt",
                table: "ReviewRuns",
                columns: new[] { "DocumentId", "CreatedAt" });

            migrationBuilder.CreateIndex(
                name: "IX_ReviewRuns_OrganisationId",
                table: "ReviewRuns",
                column: "OrganisationId");

            // Item states (R-M8-4): Dismissed (2) becomes NotApplicable (3); Resolved (1) becomes Fixed (1) when the
            // review closed it, else Accepted (2): an owner's "resolved" without a new version was a risk taken.
            migrationBuilder.Sql("UPDATE DocumentReadinessItems SET Status = 3 WHERE Status = 2;");
            migrationBuilder.Sql("UPDATE DocumentReadinessItems SET Status = 2 WHERE Status = 1 AND (Reason IS NULL OR Reason <> N'No longer reported by the latest review');");
            migrationBuilder.Sql("UPDATE DocumentReadinessItems SET Reason = N'Decided before the review rework; no reason was recorded.' WHERE Status IN (2, 3) AND (Reason IS NULL OR LTRIM(RTRIM(Reason)) = N'');");

            // Earlier verdicts came from a model or from unapproved rules (E1): every contract is "not reviewed" until
            // Harvey reviews it with approved rules, and a draft cannot be sent before that (R-M8-8).
            migrationBuilder.Sql("UPDATE Documents SET ReadinessVerdict = 0, ReadinessScore = NULL, ReadinessEvaluatedAt = NULL;");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "OrganisationRuleSettings");

            migrationBuilder.DropTable(
                name: "ReviewRuns");

            migrationBuilder.DropIndex(
                name: "IX_DocumentReadinessItems_LastSeenRunId",
                table: "DocumentReadinessItems");

            migrationBuilder.DropColumn(
                name: "LastReviewRunId",
                table: "Documents");

            migrationBuilder.DropColumn(
                name: "RiskScore",
                table: "Documents");

            migrationBuilder.DropColumn(
                name: "ArticlesJson",
                table: "DocumentReadinessItems");

            migrationBuilder.DropColumn(
                name: "ClauseReference",
                table: "DocumentReadinessItems");

            migrationBuilder.DropColumn(
                name: "Confidence",
                table: "DocumentReadinessItems");

            migrationBuilder.DropColumn(
                name: "RuleApprovedAt",
                table: "DocumentReadinessItems");

            migrationBuilder.DropColumn(
                name: "LastSeenRunId",
                table: "DocumentReadinessItems");

            migrationBuilder.DropColumn(
                name: "DetectionKind",
                table: "DocumentReadinessItems");

            migrationBuilder.DropColumn(
                name: "FirstFoundInVersion",
                table: "DocumentReadinessItems");

            migrationBuilder.DropColumn(
                name: "FixedInVersion",
                table: "DocumentReadinessItems");

            migrationBuilder.DropColumn(
                name: "LastSeenVersion",
                table: "DocumentReadinessItems");

            migrationBuilder.DropColumn(
                name: "QuotesJson",
                table: "DocumentReadinessItems");

            migrationBuilder.DropColumn(
                name: "RuleApprovedBy",
                table: "DocumentReadinessItems");

            migrationBuilder.DropColumn(
                name: "RuleKey",
                table: "DocumentReadinessItems");

            migrationBuilder.DropColumn(
                name: "RuleVersion",
                table: "DocumentReadinessItems");

            migrationBuilder.RenameColumn(
                name: "DecidedAt",
                table: "DocumentReadinessItems",
                newName: "ResolvedAt");

            migrationBuilder.RenameColumn(
                name: "Reason",
                table: "DocumentReadinessItems",
                newName: "ResolutionNote");

            migrationBuilder.RenameColumn(
                name: "DecidedByUserId",
                table: "DocumentReadinessItems",
                newName: "ResolvedByUserId");

            migrationBuilder.AddColumn<DateTime>(
                name: "AiComplianceAnalysisGeneratedAt",
                table: "Documents",
                type: "datetime2",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "AiComplianceAnalysisJson",
                table: "Documents",
                type: "nvarchar(max)",
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "AiRiskAnalysisGeneratedAt",
                table: "Documents",
                type: "datetime2",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "AiRiskAnalysisJson",
                table: "Documents",
                type: "nvarchar(max)",
                nullable: true);
        }
    }
}
