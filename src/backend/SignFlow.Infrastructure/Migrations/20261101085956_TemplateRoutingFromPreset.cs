using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace SignFlow.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class TemplateRoutingFromPreset : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // Data (R-M6-2): routing comes only from a template's workflow. A template that fixed its approvers itself
            // (a default approver, or a person locked on a field) gets a workflow with those people as its steps, so its
            // requests keep going where they went.
            migrationBuilder.Sql(@"
-- HTML templates
CREATE TABLE #RoutingHtml (TemplateId uniqueidentifier, PresetId uniqueidentifier, OrganisationId uniqueidentifier, Name nvarchar(200), WorkflowType int);
INSERT INTO #RoutingHtml
SELECT t.Id, NEWID(), t.OrganisationId, LEFT(t.Name + N' — circuit', 200), t.WorkflowType
FROM HtmlTemplates t
WHERE t.WorkflowPresetId IS NULL AND t.IsDeleted = 0
  AND (t.DefaultApproverId IS NOT NULL OR EXISTS (SELECT 1 FROM HtmlTemplateFields f WHERE f.HtmlTemplateId = t.Id AND f.PreAssignedApproverId IS NOT NULL));

INSERT INTO WorkflowPresets (Id, Name, Description, WorkflowType, IsActive, CreatedAt, IsDeleted, OrganisationId)
SELECT PresetId, Name, N'Created from the template''s former fixed approvers.', WorkflowType, 1, SYSUTCDATETIME(), 0, OrganisationId FROM #RoutingHtml;

-- One step per approver signature field, in order, while a person was fixed for it; the rest stay the requester's choice.
INSERT INTO WorkflowPresetSteps (Id, WorkflowPresetId, [Order], Stage, Label, ApproverKind, ApproverUserId, CreatedAt, IsDeleted, OrganisationId)
SELECT NEWID(), r.PresetId, x.N, x.Stage, x.Label, 0, x.UserId, SYSUTCDATETIME(), 0, r.OrganisationId
FROM #RoutingHtml r
CROSS APPLY (
    SELECT ROW_NUMBER() OVER (ORDER BY f.[Order]) AS N, f.WorkflowStage AS Stage, f.Label,
           COALESCE(f.PreAssignedApproverId, t.DefaultApproverId) AS UserId
    FROM HtmlTemplateFields f JOIN HtmlTemplates t ON t.Id = f.HtmlTemplateId
    WHERE f.HtmlTemplateId = r.TemplateId AND f.Type = 5 AND f.Role = 1 AND f.IsDeleted = 0
) x
WHERE x.UserId IS NOT NULL
  AND x.N <= (SELECT COUNT(*) FROM (SELECT ROW_NUMBER() OVER (ORDER BY f2.[Order]) AS N2,
                                           COALESCE(f2.PreAssignedApproverId, t2.DefaultApproverId) AS U2
                                    FROM HtmlTemplateFields f2 JOIN HtmlTemplates t2 ON t2.Id = f2.HtmlTemplateId
                                    WHERE f2.HtmlTemplateId = r.TemplateId AND f2.Type = 5 AND f2.Role = 1 AND f2.IsDeleted = 0) p
              WHERE p.U2 IS NOT NULL AND p.N2 <= x.N);

UPDATE t SET WorkflowPresetId = r.PresetId FROM HtmlTemplates t JOIN #RoutingHtml r ON r.TemplateId = t.Id;
DROP TABLE #RoutingHtml;

-- PDF templates
CREATE TABLE #RoutingPdf (TemplateId uniqueidentifier, PresetId uniqueidentifier, OrganisationId uniqueidentifier, Name nvarchar(200), WorkflowType int);
INSERT INTO #RoutingPdf
SELECT t.Id, NEWID(), t.OrganisationId, LEFT(t.Name + N' — circuit', 200), t.WorkflowType
FROM PdfTemplates t
WHERE t.WorkflowPresetId IS NULL AND t.IsDeleted = 0
  AND (t.DefaultApproverId IS NOT NULL OR EXISTS (SELECT 1 FROM PdfTemplateFields f WHERE f.PdfTemplateId = t.Id AND f.PreAssignedApproverId IS NOT NULL));

INSERT INTO WorkflowPresets (Id, Name, Description, WorkflowType, IsActive, CreatedAt, IsDeleted, OrganisationId)
SELECT PresetId, Name, N'Created from the template''s former fixed approvers.', WorkflowType, 1, SYSUTCDATETIME(), 0, OrganisationId FROM #RoutingPdf;

-- One step per approver signature field, in order, while a person was fixed for it; the rest stay the requester's choice.
INSERT INTO WorkflowPresetSteps (Id, WorkflowPresetId, [Order], Stage, Label, ApproverKind, ApproverUserId, CreatedAt, IsDeleted, OrganisationId)
SELECT NEWID(), r.PresetId, x.N, x.Stage, x.Label, 0, x.UserId, SYSUTCDATETIME(), 0, r.OrganisationId
FROM #RoutingPdf r
CROSS APPLY (
    SELECT ROW_NUMBER() OVER (ORDER BY f.[Order]) AS N, f.WorkflowStage AS Stage, f.Label,
           COALESCE(f.PreAssignedApproverId, t.DefaultApproverId) AS UserId
    FROM PdfTemplateFields f JOIN PdfTemplates t ON t.Id = f.PdfTemplateId
    WHERE f.PdfTemplateId = r.TemplateId AND f.Type = 4 AND f.FillRole = 1 AND f.IsDeleted = 0
) x
WHERE x.UserId IS NOT NULL
  AND x.N <= (SELECT COUNT(*) FROM (SELECT ROW_NUMBER() OVER (ORDER BY f2.[Order]) AS N2,
                                           COALESCE(f2.PreAssignedApproverId, t2.DefaultApproverId) AS U2
                                    FROM PdfTemplateFields f2 JOIN PdfTemplates t2 ON t2.Id = f2.PdfTemplateId
                                    WHERE f2.PdfTemplateId = r.TemplateId AND f2.Type = 4 AND f2.FillRole = 1 AND f2.IsDeleted = 0) p
              WHERE p.U2 IS NOT NULL AND p.N2 <= x.N);

UPDATE t SET WorkflowPresetId = r.PresetId FROM PdfTemplates t JOIN #RoutingPdf r ON r.TemplateId = t.Id;
DROP TABLE #RoutingPdf;
");

            migrationBuilder.DropForeignKey(
                name: "FK_HtmlTemplateFields_Users_PreAssignedApproverId",
                table: "HtmlTemplateFields");

            migrationBuilder.DropForeignKey(
                name: "FK_HtmlTemplates_Users_DefaultApproverId",
                table: "HtmlTemplates");

            migrationBuilder.DropForeignKey(
                name: "FK_PdfTemplateFields_Users_PreAssignedApproverId",
                table: "PdfTemplateFields");

            migrationBuilder.DropForeignKey(
                name: "FK_PdfTemplates_Users_DefaultApproverId",
                table: "PdfTemplates");

            migrationBuilder.DropIndex(
                name: "IX_PdfTemplates_DefaultApproverId",
                table: "PdfTemplates");

            migrationBuilder.DropIndex(
                name: "IX_PdfTemplateFields_PreAssignedApproverId",
                table: "PdfTemplateFields");

            migrationBuilder.DropIndex(
                name: "IX_HtmlTemplates_DefaultApproverId",
                table: "HtmlTemplates");

            migrationBuilder.DropIndex(
                name: "IX_HtmlTemplateFields_PreAssignedApproverId",
                table: "HtmlTemplateFields");

            migrationBuilder.DropColumn(
                name: "DefaultApproverId",
                table: "PdfTemplates");

            migrationBuilder.DropColumn(
                name: "PreAssignedApproverId",
                table: "PdfTemplateFields");

            migrationBuilder.DropColumn(
                name: "DefaultApproverId",
                table: "HtmlTemplates");

            migrationBuilder.DropColumn(
                name: "PreAssignedApproverId",
                table: "HtmlTemplateFields");

            migrationBuilder.RenameColumn(
                name: "WorkflowStage",
                table: "PdfTemplateFields",
                newName: "FilledByStep");

            migrationBuilder.RenameColumn(
                name: "WorkflowStage",
                table: "HtmlTemplateFields",
                newName: "FilledByStep");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.RenameColumn(
                name: "FilledByStep",
                table: "PdfTemplateFields",
                newName: "WorkflowStage");

            migrationBuilder.RenameColumn(
                name: "FilledByStep",
                table: "HtmlTemplateFields",
                newName: "WorkflowStage");

            migrationBuilder.AddColumn<Guid>(
                name: "DefaultApproverId",
                table: "PdfTemplates",
                type: "uniqueidentifier",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "PreAssignedApproverId",
                table: "PdfTemplateFields",
                type: "uniqueidentifier",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "DefaultApproverId",
                table: "HtmlTemplates",
                type: "uniqueidentifier",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "PreAssignedApproverId",
                table: "HtmlTemplateFields",
                type: "uniqueidentifier",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_PdfTemplates_DefaultApproverId",
                table: "PdfTemplates",
                column: "DefaultApproverId");

            migrationBuilder.CreateIndex(
                name: "IX_PdfTemplateFields_PreAssignedApproverId",
                table: "PdfTemplateFields",
                column: "PreAssignedApproverId");

            migrationBuilder.CreateIndex(
                name: "IX_HtmlTemplates_DefaultApproverId",
                table: "HtmlTemplates",
                column: "DefaultApproverId");

            migrationBuilder.CreateIndex(
                name: "IX_HtmlTemplateFields_PreAssignedApproverId",
                table: "HtmlTemplateFields",
                column: "PreAssignedApproverId");

            migrationBuilder.AddForeignKey(
                name: "FK_HtmlTemplateFields_Users_PreAssignedApproverId",
                table: "HtmlTemplateFields",
                column: "PreAssignedApproverId",
                principalTable: "Users",
                principalColumn: "Id",
                onDelete: ReferentialAction.SetNull);

            migrationBuilder.AddForeignKey(
                name: "FK_HtmlTemplates_Users_DefaultApproverId",
                table: "HtmlTemplates",
                column: "DefaultApproverId",
                principalTable: "Users",
                principalColumn: "Id",
                onDelete: ReferentialAction.SetNull);

            migrationBuilder.AddForeignKey(
                name: "FK_PdfTemplateFields_Users_PreAssignedApproverId",
                table: "PdfTemplateFields",
                column: "PreAssignedApproverId",
                principalTable: "Users",
                principalColumn: "Id",
                onDelete: ReferentialAction.SetNull);

            migrationBuilder.AddForeignKey(
                name: "FK_PdfTemplates_Users_DefaultApproverId",
                table: "PdfTemplates",
                column: "DefaultApproverId",
                principalTable: "Users",
                principalColumn: "Id",
                onDelete: ReferentialAction.SetNull);
        }
    }
}
