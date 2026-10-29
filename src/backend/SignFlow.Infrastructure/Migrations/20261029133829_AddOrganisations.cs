using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace SignFlow.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddOrganisations : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // Every existing row moves into the default organisation (Organisation.DefaultId): the column default
            // fills them, and the row below makes the foreign keys valid. New rows get their organisation from
            // the application (ApplicationDbContext.StampOrganisation), never from this default.
            migrationBuilder.AddColumn<Guid>(
                name: "OrganisationId",
                table: "WorkflowSteps",
                type: "uniqueidentifier",
                nullable: false,
                defaultValue: new Guid("6a1d5f3e-0c4b-4e8e-9f3a-2b7d1c0e5a11"));

            migrationBuilder.AddColumn<Guid>(
                name: "OrganisationId",
                table: "Workflows",
                type: "uniqueidentifier",
                nullable: false,
                defaultValue: new Guid("6a1d5f3e-0c4b-4e8e-9f3a-2b7d1c0e5a11"));

            migrationBuilder.AddColumn<Guid>(
                name: "OrganisationId",
                table: "WorkflowPresetSteps",
                type: "uniqueidentifier",
                nullable: false,
                defaultValue: new Guid("6a1d5f3e-0c4b-4e8e-9f3a-2b7d1c0e5a11"));

            migrationBuilder.AddColumn<Guid>(
                name: "OrganisationId",
                table: "WorkflowPresets",
                type: "uniqueidentifier",
                nullable: false,
                defaultValue: new Guid("6a1d5f3e-0c4b-4e8e-9f3a-2b7d1c0e5a11"));

            migrationBuilder.AddColumn<Guid>(
                name: "OrganisationId",
                table: "Vacations",
                type: "uniqueidentifier",
                nullable: false,
                defaultValue: new Guid("6a1d5f3e-0c4b-4e8e-9f3a-2b7d1c0e5a11"));

            migrationBuilder.AddColumn<Guid>(
                name: "OrganisationId",
                table: "Users",
                type: "uniqueidentifier",
                nullable: false,
                defaultValue: new Guid("6a1d5f3e-0c4b-4e8e-9f3a-2b7d1c0e5a11"));

            migrationBuilder.AddColumn<Guid>(
                name: "OrganisationId",
                table: "Templates",
                type: "uniqueidentifier",
                nullable: false,
                defaultValue: new Guid("6a1d5f3e-0c4b-4e8e-9f3a-2b7d1c0e5a11"));

            migrationBuilder.AddColumn<Guid>(
                name: "OrganisationId",
                table: "Signatures",
                type: "uniqueidentifier",
                nullable: false,
                defaultValue: new Guid("6a1d5f3e-0c4b-4e8e-9f3a-2b7d1c0e5a11"));

            migrationBuilder.AddColumn<Guid>(
                name: "OrganisationId",
                table: "SignatureFields",
                type: "uniqueidentifier",
                nullable: false,
                defaultValue: new Guid("6a1d5f3e-0c4b-4e8e-9f3a-2b7d1c0e5a11"));

            migrationBuilder.AddColumn<Guid>(
                name: "OrganisationId",
                table: "RequestTypes",
                type: "uniqueidentifier",
                nullable: false,
                defaultValue: new Guid("6a1d5f3e-0c4b-4e8e-9f3a-2b7d1c0e5a11"));

            migrationBuilder.AddColumn<Guid>(
                name: "OrganisationId",
                table: "RegulatoryTexts",
                type: "uniqueidentifier",
                nullable: false,
                defaultValue: new Guid("6a1d5f3e-0c4b-4e8e-9f3a-2b7d1c0e5a11"));

            migrationBuilder.AddColumn<Guid>(
                name: "OrganisationId",
                table: "RegulatoryArticles",
                type: "uniqueidentifier",
                nullable: false,
                defaultValue: new Guid("6a1d5f3e-0c4b-4e8e-9f3a-2b7d1c0e5a11"));

            migrationBuilder.AddColumn<Guid>(
                name: "OrganisationId",
                table: "Recipients",
                type: "uniqueidentifier",
                nullable: false,
                defaultValue: new Guid("6a1d5f3e-0c4b-4e8e-9f3a-2b7d1c0e5a11"));

            migrationBuilder.AddColumn<Guid>(
                name: "OrganisationId",
                table: "PdfTemplates",
                type: "uniqueidentifier",
                nullable: false,
                defaultValue: new Guid("6a1d5f3e-0c4b-4e8e-9f3a-2b7d1c0e5a11"));

            migrationBuilder.AddColumn<Guid>(
                name: "OrganisationId",
                table: "PdfTemplateFields",
                type: "uniqueidentifier",
                nullable: false,
                defaultValue: new Guid("6a1d5f3e-0c4b-4e8e-9f3a-2b7d1c0e5a11"));

            migrationBuilder.AddColumn<Guid>(
                name: "OrganisationId",
                table: "Notifications",
                type: "uniqueidentifier",
                nullable: false,
                defaultValue: new Guid("6a1d5f3e-0c4b-4e8e-9f3a-2b7d1c0e5a11"));

            migrationBuilder.AddColumn<Guid>(
                name: "OrganisationId",
                table: "HtmlTemplates",
                type: "uniqueidentifier",
                nullable: false,
                defaultValue: new Guid("6a1d5f3e-0c4b-4e8e-9f3a-2b7d1c0e5a11"));

            migrationBuilder.AddColumn<Guid>(
                name: "OrganisationId",
                table: "HtmlTemplateFields",
                type: "uniqueidentifier",
                nullable: false,
                defaultValue: new Guid("6a1d5f3e-0c4b-4e8e-9f3a-2b7d1c0e5a11"));

            migrationBuilder.AddColumn<Guid>(
                name: "OrganisationId",
                table: "DocumentVersions",
                type: "uniqueidentifier",
                nullable: false,
                defaultValue: new Guid("6a1d5f3e-0c4b-4e8e-9f3a-2b7d1c0e5a11"));

            migrationBuilder.AddColumn<Guid>(
                name: "OrganisationId",
                table: "Documents",
                type: "uniqueidentifier",
                nullable: false,
                defaultValue: new Guid("6a1d5f3e-0c4b-4e8e-9f3a-2b7d1c0e5a11"));

            migrationBuilder.AddColumn<Guid>(
                name: "OrganisationId",
                table: "DocumentReadinessItems",
                type: "uniqueidentifier",
                nullable: false,
                defaultValue: new Guid("6a1d5f3e-0c4b-4e8e-9f3a-2b7d1c0e5a11"));

            migrationBuilder.AddColumn<Guid>(
                name: "OrganisationId",
                table: "DataSubjectRequests",
                type: "uniqueidentifier",
                nullable: false,
                defaultValue: new Guid("6a1d5f3e-0c4b-4e8e-9f3a-2b7d1c0e5a11"));

            migrationBuilder.AddColumn<Guid>(
                name: "OrganisationId",
                table: "ConsentLogs",
                type: "uniqueidentifier",
                nullable: false,
                defaultValue: new Guid("6a1d5f3e-0c4b-4e8e-9f3a-2b7d1c0e5a11"));

            migrationBuilder.AddColumn<Guid>(
                name: "OrganisationId",
                table: "Comments",
                type: "uniqueidentifier",
                nullable: false,
                defaultValue: new Guid("6a1d5f3e-0c4b-4e8e-9f3a-2b7d1c0e5a11"));

            migrationBuilder.AddColumn<Guid>(
                name: "OrganisationId",
                table: "Clauses",
                type: "uniqueidentifier",
                nullable: false,
                defaultValue: new Guid("6a1d5f3e-0c4b-4e8e-9f3a-2b7d1c0e5a11"));

            migrationBuilder.AddColumn<Guid>(
                name: "OrganisationId",
                table: "ChecklistItems",
                type: "uniqueidentifier",
                nullable: false,
                defaultValue: new Guid("6a1d5f3e-0c4b-4e8e-9f3a-2b7d1c0e5a11"));

            migrationBuilder.AddColumn<Guid>(
                name: "OrganisationId",
                table: "AuditLogs",
                type: "uniqueidentifier",
                nullable: false,
                defaultValue: new Guid("6a1d5f3e-0c4b-4e8e-9f3a-2b7d1c0e5a11"));

            migrationBuilder.AddColumn<Guid>(
                name: "OrganisationId",
                table: "Attachments",
                type: "uniqueidentifier",
                nullable: false,
                defaultValue: new Guid("6a1d5f3e-0c4b-4e8e-9f3a-2b7d1c0e5a11"));

            migrationBuilder.CreateTable(
                name: "Organisations",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Name = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                    Slug = table.Column<string>(type: "nvarchar(80)", maxLength: 80, nullable: false),
                    Country = table.Column<string>(type: "nvarchar(2)", maxLength: 2, nullable: false),
                    DefaultLanguage = table.Column<string>(type: "nvarchar(5)", maxLength: 5, nullable: false),
                    ModelMode = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: false),
                    RetentionYears = table.Column<int>(type: "int", nullable: false),
                    IsActive = table.Column<bool>(type: "bit", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "datetime2", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "datetime2", nullable: true),
                    CreatedBy = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    UpdatedBy = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    IsDeleted = table.Column<bool>(type: "bit", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Organisations", x => x.Id);
                });

            migrationBuilder.InsertData(
                table: "Organisations",
                columns: ["Id", "Name", "Slug", "Country", "DefaultLanguage", "ModelMode", "RetentionYears", "IsActive", "CreatedAt", "IsDeleted"],
                values: new object[] { new Guid("6a1d5f3e-0c4b-4e8e-9f3a-2b7d1c0e5a11"), "Consultim IT", "consultim-it", "TN", "fr", "Off", 10, true, new DateTime(2026, 10, 29, 0, 0, 0, DateTimeKind.Utc), false });

            migrationBuilder.CreateIndex(
                name: "IX_WorkflowSteps_OrganisationId",
                table: "WorkflowSteps",
                column: "OrganisationId");

            migrationBuilder.CreateIndex(
                name: "IX_Workflows_OrganisationId",
                table: "Workflows",
                column: "OrganisationId");

            migrationBuilder.CreateIndex(
                name: "IX_WorkflowPresetSteps_OrganisationId",
                table: "WorkflowPresetSteps",
                column: "OrganisationId");

            migrationBuilder.CreateIndex(
                name: "IX_WorkflowPresets_OrganisationId",
                table: "WorkflowPresets",
                column: "OrganisationId");

            migrationBuilder.CreateIndex(
                name: "IX_Vacations_OrganisationId",
                table: "Vacations",
                column: "OrganisationId");

            migrationBuilder.CreateIndex(
                name: "IX_Users_OrganisationId",
                table: "Users",
                column: "OrganisationId");

            migrationBuilder.CreateIndex(
                name: "IX_Templates_OrganisationId",
                table: "Templates",
                column: "OrganisationId");

            migrationBuilder.CreateIndex(
                name: "IX_Signatures_OrganisationId",
                table: "Signatures",
                column: "OrganisationId");

            migrationBuilder.CreateIndex(
                name: "IX_SignatureFields_OrganisationId",
                table: "SignatureFields",
                column: "OrganisationId");

            migrationBuilder.CreateIndex(
                name: "IX_RequestTypes_OrganisationId",
                table: "RequestTypes",
                column: "OrganisationId");

            migrationBuilder.CreateIndex(
                name: "IX_RegulatoryTexts_OrganisationId",
                table: "RegulatoryTexts",
                column: "OrganisationId");

            migrationBuilder.CreateIndex(
                name: "IX_RegulatoryArticles_OrganisationId",
                table: "RegulatoryArticles",
                column: "OrganisationId");

            migrationBuilder.CreateIndex(
                name: "IX_Recipients_OrganisationId",
                table: "Recipients",
                column: "OrganisationId");

            migrationBuilder.CreateIndex(
                name: "IX_PdfTemplates_OrganisationId",
                table: "PdfTemplates",
                column: "OrganisationId");

            migrationBuilder.CreateIndex(
                name: "IX_PdfTemplateFields_OrganisationId",
                table: "PdfTemplateFields",
                column: "OrganisationId");

            migrationBuilder.CreateIndex(
                name: "IX_Notifications_OrganisationId",
                table: "Notifications",
                column: "OrganisationId");

            migrationBuilder.CreateIndex(
                name: "IX_HtmlTemplates_OrganisationId",
                table: "HtmlTemplates",
                column: "OrganisationId");

            migrationBuilder.CreateIndex(
                name: "IX_HtmlTemplateFields_OrganisationId",
                table: "HtmlTemplateFields",
                column: "OrganisationId");

            migrationBuilder.CreateIndex(
                name: "IX_DocumentVersions_OrganisationId",
                table: "DocumentVersions",
                column: "OrganisationId");

            migrationBuilder.CreateIndex(
                name: "IX_Documents_OrganisationId",
                table: "Documents",
                column: "OrganisationId");

            migrationBuilder.CreateIndex(
                name: "IX_DocumentReadinessItems_OrganisationId",
                table: "DocumentReadinessItems",
                column: "OrganisationId");

            migrationBuilder.CreateIndex(
                name: "IX_DataSubjectRequests_OrganisationId",
                table: "DataSubjectRequests",
                column: "OrganisationId");

            migrationBuilder.CreateIndex(
                name: "IX_ConsentLogs_OrganisationId",
                table: "ConsentLogs",
                column: "OrganisationId");

            migrationBuilder.CreateIndex(
                name: "IX_Comments_OrganisationId",
                table: "Comments",
                column: "OrganisationId");

            migrationBuilder.CreateIndex(
                name: "IX_Clauses_OrganisationId",
                table: "Clauses",
                column: "OrganisationId");

            migrationBuilder.CreateIndex(
                name: "IX_ChecklistItems_OrganisationId",
                table: "ChecklistItems",
                column: "OrganisationId");

            migrationBuilder.CreateIndex(
                name: "IX_AuditLogs_OrganisationId",
                table: "AuditLogs",
                column: "OrganisationId");

            migrationBuilder.CreateIndex(
                name: "IX_Attachments_OrganisationId",
                table: "Attachments",
                column: "OrganisationId");

            migrationBuilder.CreateIndex(
                name: "IX_Organisations_Slug",
                table: "Organisations",
                column: "Slug",
                unique: true);

            migrationBuilder.AddForeignKey(
                name: "FK_Attachments_Organisations_OrganisationId",
                table: "Attachments",
                column: "OrganisationId",
                principalTable: "Organisations",
                principalColumn: "Id");

            migrationBuilder.AddForeignKey(
                name: "FK_AuditLogs_Organisations_OrganisationId",
                table: "AuditLogs",
                column: "OrganisationId",
                principalTable: "Organisations",
                principalColumn: "Id");

            migrationBuilder.AddForeignKey(
                name: "FK_ChecklistItems_Organisations_OrganisationId",
                table: "ChecklistItems",
                column: "OrganisationId",
                principalTable: "Organisations",
                principalColumn: "Id");

            migrationBuilder.AddForeignKey(
                name: "FK_Clauses_Organisations_OrganisationId",
                table: "Clauses",
                column: "OrganisationId",
                principalTable: "Organisations",
                principalColumn: "Id");

            migrationBuilder.AddForeignKey(
                name: "FK_Comments_Organisations_OrganisationId",
                table: "Comments",
                column: "OrganisationId",
                principalTable: "Organisations",
                principalColumn: "Id");

            migrationBuilder.AddForeignKey(
                name: "FK_ConsentLogs_Organisations_OrganisationId",
                table: "ConsentLogs",
                column: "OrganisationId",
                principalTable: "Organisations",
                principalColumn: "Id");

            migrationBuilder.AddForeignKey(
                name: "FK_DataSubjectRequests_Organisations_OrganisationId",
                table: "DataSubjectRequests",
                column: "OrganisationId",
                principalTable: "Organisations",
                principalColumn: "Id");

            migrationBuilder.AddForeignKey(
                name: "FK_DocumentReadinessItems_Organisations_OrganisationId",
                table: "DocumentReadinessItems",
                column: "OrganisationId",
                principalTable: "Organisations",
                principalColumn: "Id");

            migrationBuilder.AddForeignKey(
                name: "FK_Documents_Organisations_OrganisationId",
                table: "Documents",
                column: "OrganisationId",
                principalTable: "Organisations",
                principalColumn: "Id");

            migrationBuilder.AddForeignKey(
                name: "FK_DocumentVersions_Organisations_OrganisationId",
                table: "DocumentVersions",
                column: "OrganisationId",
                principalTable: "Organisations",
                principalColumn: "Id");

            migrationBuilder.AddForeignKey(
                name: "FK_HtmlTemplateFields_Organisations_OrganisationId",
                table: "HtmlTemplateFields",
                column: "OrganisationId",
                principalTable: "Organisations",
                principalColumn: "Id");

            migrationBuilder.AddForeignKey(
                name: "FK_HtmlTemplates_Organisations_OrganisationId",
                table: "HtmlTemplates",
                column: "OrganisationId",
                principalTable: "Organisations",
                principalColumn: "Id");

            migrationBuilder.AddForeignKey(
                name: "FK_Notifications_Organisations_OrganisationId",
                table: "Notifications",
                column: "OrganisationId",
                principalTable: "Organisations",
                principalColumn: "Id");

            migrationBuilder.AddForeignKey(
                name: "FK_PdfTemplateFields_Organisations_OrganisationId",
                table: "PdfTemplateFields",
                column: "OrganisationId",
                principalTable: "Organisations",
                principalColumn: "Id");

            migrationBuilder.AddForeignKey(
                name: "FK_PdfTemplates_Organisations_OrganisationId",
                table: "PdfTemplates",
                column: "OrganisationId",
                principalTable: "Organisations",
                principalColumn: "Id");

            migrationBuilder.AddForeignKey(
                name: "FK_Recipients_Organisations_OrganisationId",
                table: "Recipients",
                column: "OrganisationId",
                principalTable: "Organisations",
                principalColumn: "Id");

            migrationBuilder.AddForeignKey(
                name: "FK_RegulatoryArticles_Organisations_OrganisationId",
                table: "RegulatoryArticles",
                column: "OrganisationId",
                principalTable: "Organisations",
                principalColumn: "Id");

            migrationBuilder.AddForeignKey(
                name: "FK_RegulatoryTexts_Organisations_OrganisationId",
                table: "RegulatoryTexts",
                column: "OrganisationId",
                principalTable: "Organisations",
                principalColumn: "Id");

            migrationBuilder.AddForeignKey(
                name: "FK_RequestTypes_Organisations_OrganisationId",
                table: "RequestTypes",
                column: "OrganisationId",
                principalTable: "Organisations",
                principalColumn: "Id");

            migrationBuilder.AddForeignKey(
                name: "FK_SignatureFields_Organisations_OrganisationId",
                table: "SignatureFields",
                column: "OrganisationId",
                principalTable: "Organisations",
                principalColumn: "Id");

            migrationBuilder.AddForeignKey(
                name: "FK_Signatures_Organisations_OrganisationId",
                table: "Signatures",
                column: "OrganisationId",
                principalTable: "Organisations",
                principalColumn: "Id");

            migrationBuilder.AddForeignKey(
                name: "FK_Templates_Organisations_OrganisationId",
                table: "Templates",
                column: "OrganisationId",
                principalTable: "Organisations",
                principalColumn: "Id");

            migrationBuilder.AddForeignKey(
                name: "FK_Users_Organisations_OrganisationId",
                table: "Users",
                column: "OrganisationId",
                principalTable: "Organisations",
                principalColumn: "Id");

            migrationBuilder.AddForeignKey(
                name: "FK_Vacations_Organisations_OrganisationId",
                table: "Vacations",
                column: "OrganisationId",
                principalTable: "Organisations",
                principalColumn: "Id");

            migrationBuilder.AddForeignKey(
                name: "FK_WorkflowPresets_Organisations_OrganisationId",
                table: "WorkflowPresets",
                column: "OrganisationId",
                principalTable: "Organisations",
                principalColumn: "Id");

            migrationBuilder.AddForeignKey(
                name: "FK_WorkflowPresetSteps_Organisations_OrganisationId",
                table: "WorkflowPresetSteps",
                column: "OrganisationId",
                principalTable: "Organisations",
                principalColumn: "Id");

            migrationBuilder.AddForeignKey(
                name: "FK_Workflows_Organisations_OrganisationId",
                table: "Workflows",
                column: "OrganisationId",
                principalTable: "Organisations",
                principalColumn: "Id");

            migrationBuilder.AddForeignKey(
                name: "FK_WorkflowSteps_Organisations_OrganisationId",
                table: "WorkflowSteps",
                column: "OrganisationId",
                principalTable: "Organisations",
                principalColumn: "Id");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_Attachments_Organisations_OrganisationId",
                table: "Attachments");

            migrationBuilder.DropForeignKey(
                name: "FK_AuditLogs_Organisations_OrganisationId",
                table: "AuditLogs");

            migrationBuilder.DropForeignKey(
                name: "FK_ChecklistItems_Organisations_OrganisationId",
                table: "ChecklistItems");

            migrationBuilder.DropForeignKey(
                name: "FK_Clauses_Organisations_OrganisationId",
                table: "Clauses");

            migrationBuilder.DropForeignKey(
                name: "FK_Comments_Organisations_OrganisationId",
                table: "Comments");

            migrationBuilder.DropForeignKey(
                name: "FK_ConsentLogs_Organisations_OrganisationId",
                table: "ConsentLogs");

            migrationBuilder.DropForeignKey(
                name: "FK_DataSubjectRequests_Organisations_OrganisationId",
                table: "DataSubjectRequests");

            migrationBuilder.DropForeignKey(
                name: "FK_DocumentReadinessItems_Organisations_OrganisationId",
                table: "DocumentReadinessItems");

            migrationBuilder.DropForeignKey(
                name: "FK_Documents_Organisations_OrganisationId",
                table: "Documents");

            migrationBuilder.DropForeignKey(
                name: "FK_DocumentVersions_Organisations_OrganisationId",
                table: "DocumentVersions");

            migrationBuilder.DropForeignKey(
                name: "FK_HtmlTemplateFields_Organisations_OrganisationId",
                table: "HtmlTemplateFields");

            migrationBuilder.DropForeignKey(
                name: "FK_HtmlTemplates_Organisations_OrganisationId",
                table: "HtmlTemplates");

            migrationBuilder.DropForeignKey(
                name: "FK_Notifications_Organisations_OrganisationId",
                table: "Notifications");

            migrationBuilder.DropForeignKey(
                name: "FK_PdfTemplateFields_Organisations_OrganisationId",
                table: "PdfTemplateFields");

            migrationBuilder.DropForeignKey(
                name: "FK_PdfTemplates_Organisations_OrganisationId",
                table: "PdfTemplates");

            migrationBuilder.DropForeignKey(
                name: "FK_Recipients_Organisations_OrganisationId",
                table: "Recipients");

            migrationBuilder.DropForeignKey(
                name: "FK_RegulatoryArticles_Organisations_OrganisationId",
                table: "RegulatoryArticles");

            migrationBuilder.DropForeignKey(
                name: "FK_RegulatoryTexts_Organisations_OrganisationId",
                table: "RegulatoryTexts");

            migrationBuilder.DropForeignKey(
                name: "FK_RequestTypes_Organisations_OrganisationId",
                table: "RequestTypes");

            migrationBuilder.DropForeignKey(
                name: "FK_SignatureFields_Organisations_OrganisationId",
                table: "SignatureFields");

            migrationBuilder.DropForeignKey(
                name: "FK_Signatures_Organisations_OrganisationId",
                table: "Signatures");

            migrationBuilder.DropForeignKey(
                name: "FK_Templates_Organisations_OrganisationId",
                table: "Templates");

            migrationBuilder.DropForeignKey(
                name: "FK_Users_Organisations_OrganisationId",
                table: "Users");

            migrationBuilder.DropForeignKey(
                name: "FK_Vacations_Organisations_OrganisationId",
                table: "Vacations");

            migrationBuilder.DropForeignKey(
                name: "FK_WorkflowPresets_Organisations_OrganisationId",
                table: "WorkflowPresets");

            migrationBuilder.DropForeignKey(
                name: "FK_WorkflowPresetSteps_Organisations_OrganisationId",
                table: "WorkflowPresetSteps");

            migrationBuilder.DropForeignKey(
                name: "FK_Workflows_Organisations_OrganisationId",
                table: "Workflows");

            migrationBuilder.DropForeignKey(
                name: "FK_WorkflowSteps_Organisations_OrganisationId",
                table: "WorkflowSteps");

            migrationBuilder.DropTable(
                name: "Organisations");

            migrationBuilder.DropIndex(
                name: "IX_WorkflowSteps_OrganisationId",
                table: "WorkflowSteps");

            migrationBuilder.DropIndex(
                name: "IX_Workflows_OrganisationId",
                table: "Workflows");

            migrationBuilder.DropIndex(
                name: "IX_WorkflowPresetSteps_OrganisationId",
                table: "WorkflowPresetSteps");

            migrationBuilder.DropIndex(
                name: "IX_WorkflowPresets_OrganisationId",
                table: "WorkflowPresets");

            migrationBuilder.DropIndex(
                name: "IX_Vacations_OrganisationId",
                table: "Vacations");

            migrationBuilder.DropIndex(
                name: "IX_Users_OrganisationId",
                table: "Users");

            migrationBuilder.DropIndex(
                name: "IX_Templates_OrganisationId",
                table: "Templates");

            migrationBuilder.DropIndex(
                name: "IX_Signatures_OrganisationId",
                table: "Signatures");

            migrationBuilder.DropIndex(
                name: "IX_SignatureFields_OrganisationId",
                table: "SignatureFields");

            migrationBuilder.DropIndex(
                name: "IX_RequestTypes_OrganisationId",
                table: "RequestTypes");

            migrationBuilder.DropIndex(
                name: "IX_RegulatoryTexts_OrganisationId",
                table: "RegulatoryTexts");

            migrationBuilder.DropIndex(
                name: "IX_RegulatoryArticles_OrganisationId",
                table: "RegulatoryArticles");

            migrationBuilder.DropIndex(
                name: "IX_Recipients_OrganisationId",
                table: "Recipients");

            migrationBuilder.DropIndex(
                name: "IX_PdfTemplates_OrganisationId",
                table: "PdfTemplates");

            migrationBuilder.DropIndex(
                name: "IX_PdfTemplateFields_OrganisationId",
                table: "PdfTemplateFields");

            migrationBuilder.DropIndex(
                name: "IX_Notifications_OrganisationId",
                table: "Notifications");

            migrationBuilder.DropIndex(
                name: "IX_HtmlTemplates_OrganisationId",
                table: "HtmlTemplates");

            migrationBuilder.DropIndex(
                name: "IX_HtmlTemplateFields_OrganisationId",
                table: "HtmlTemplateFields");

            migrationBuilder.DropIndex(
                name: "IX_DocumentVersions_OrganisationId",
                table: "DocumentVersions");

            migrationBuilder.DropIndex(
                name: "IX_Documents_OrganisationId",
                table: "Documents");

            migrationBuilder.DropIndex(
                name: "IX_DocumentReadinessItems_OrganisationId",
                table: "DocumentReadinessItems");

            migrationBuilder.DropIndex(
                name: "IX_DataSubjectRequests_OrganisationId",
                table: "DataSubjectRequests");

            migrationBuilder.DropIndex(
                name: "IX_ConsentLogs_OrganisationId",
                table: "ConsentLogs");

            migrationBuilder.DropIndex(
                name: "IX_Comments_OrganisationId",
                table: "Comments");

            migrationBuilder.DropIndex(
                name: "IX_Clauses_OrganisationId",
                table: "Clauses");

            migrationBuilder.DropIndex(
                name: "IX_ChecklistItems_OrganisationId",
                table: "ChecklistItems");

            migrationBuilder.DropIndex(
                name: "IX_AuditLogs_OrganisationId",
                table: "AuditLogs");

            migrationBuilder.DropIndex(
                name: "IX_Attachments_OrganisationId",
                table: "Attachments");

            migrationBuilder.DropColumn(
                name: "OrganisationId",
                table: "WorkflowSteps");

            migrationBuilder.DropColumn(
                name: "OrganisationId",
                table: "Workflows");

            migrationBuilder.DropColumn(
                name: "OrganisationId",
                table: "WorkflowPresetSteps");

            migrationBuilder.DropColumn(
                name: "OrganisationId",
                table: "WorkflowPresets");

            migrationBuilder.DropColumn(
                name: "OrganisationId",
                table: "Vacations");

            migrationBuilder.DropColumn(
                name: "OrganisationId",
                table: "Users");

            migrationBuilder.DropColumn(
                name: "OrganisationId",
                table: "Templates");

            migrationBuilder.DropColumn(
                name: "OrganisationId",
                table: "Signatures");

            migrationBuilder.DropColumn(
                name: "OrganisationId",
                table: "SignatureFields");

            migrationBuilder.DropColumn(
                name: "OrganisationId",
                table: "RequestTypes");

            migrationBuilder.DropColumn(
                name: "OrganisationId",
                table: "RegulatoryTexts");

            migrationBuilder.DropColumn(
                name: "OrganisationId",
                table: "RegulatoryArticles");

            migrationBuilder.DropColumn(
                name: "OrganisationId",
                table: "Recipients");

            migrationBuilder.DropColumn(
                name: "OrganisationId",
                table: "PdfTemplates");

            migrationBuilder.DropColumn(
                name: "OrganisationId",
                table: "PdfTemplateFields");

            migrationBuilder.DropColumn(
                name: "OrganisationId",
                table: "Notifications");

            migrationBuilder.DropColumn(
                name: "OrganisationId",
                table: "HtmlTemplates");

            migrationBuilder.DropColumn(
                name: "OrganisationId",
                table: "HtmlTemplateFields");

            migrationBuilder.DropColumn(
                name: "OrganisationId",
                table: "DocumentVersions");

            migrationBuilder.DropColumn(
                name: "OrganisationId",
                table: "Documents");

            migrationBuilder.DropColumn(
                name: "OrganisationId",
                table: "DocumentReadinessItems");

            migrationBuilder.DropColumn(
                name: "OrganisationId",
                table: "DataSubjectRequests");

            migrationBuilder.DropColumn(
                name: "OrganisationId",
                table: "ConsentLogs");

            migrationBuilder.DropColumn(
                name: "OrganisationId",
                table: "Comments");

            migrationBuilder.DropColumn(
                name: "OrganisationId",
                table: "Clauses");

            migrationBuilder.DropColumn(
                name: "OrganisationId",
                table: "ChecklistItems");

            migrationBuilder.DropColumn(
                name: "OrganisationId",
                table: "AuditLogs");

            migrationBuilder.DropColumn(
                name: "OrganisationId",
                table: "Attachments");
        }
    }
}
