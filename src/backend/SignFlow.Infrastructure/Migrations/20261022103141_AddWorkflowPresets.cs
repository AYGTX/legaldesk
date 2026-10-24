using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace SignFlow.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddWorkflowPresets : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<Guid>(
                name: "WorkflowPresetId",
                table: "PdfTemplates",
                type: "uniqueidentifier",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "WorkflowPresetId",
                table: "HtmlTemplates",
                type: "uniqueidentifier",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "WorkflowPresets",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Name = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                    Description = table.Column<string>(type: "nvarchar(1000)", maxLength: 1000, nullable: true),
                    WorkflowType = table.Column<int>(type: "int", nullable: false),
                    IsActive = table.Column<bool>(type: "bit", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "datetime2", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "datetime2", nullable: true),
                    CreatedBy = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    UpdatedBy = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    IsDeleted = table.Column<bool>(type: "bit", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_WorkflowPresets", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "WorkflowPresetSteps",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    WorkflowPresetId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Order = table.Column<int>(type: "int", nullable: false),
                    Stage = table.Column<int>(type: "int", nullable: false),
                    Label = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: true),
                    ApproverKind = table.Column<int>(type: "int", nullable: false),
                    ApproverUserId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    RoleName = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: true),
                    Department = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: true),
                    ConditionFieldKey = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: true),
                    ConditionOperator = table.Column<string>(type: "nvarchar(2)", maxLength: 2, nullable: true),
                    ConditionValue = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "datetime2", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "datetime2", nullable: true),
                    CreatedBy = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    UpdatedBy = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    IsDeleted = table.Column<bool>(type: "bit", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_WorkflowPresetSteps", x => x.Id);
                    table.ForeignKey(
                        name: "FK_WorkflowPresetSteps_Users_ApproverUserId",
                        column: x => x.ApproverUserId,
                        principalTable: "Users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_WorkflowPresetSteps_WorkflowPresets_WorkflowPresetId",
                        column: x => x.WorkflowPresetId,
                        principalTable: "WorkflowPresets",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_PdfTemplates_WorkflowPresetId",
                table: "PdfTemplates",
                column: "WorkflowPresetId");

            migrationBuilder.CreateIndex(
                name: "IX_HtmlTemplates_WorkflowPresetId",
                table: "HtmlTemplates",
                column: "WorkflowPresetId");

            migrationBuilder.CreateIndex(
                name: "IX_WorkflowPresets_IsActive",
                table: "WorkflowPresets",
                column: "IsActive");

            migrationBuilder.CreateIndex(
                name: "IX_WorkflowPresetSteps_ApproverUserId",
                table: "WorkflowPresetSteps",
                column: "ApproverUserId");

            migrationBuilder.CreateIndex(
                name: "IX_WorkflowPresetSteps_WorkflowPresetId_Order",
                table: "WorkflowPresetSteps",
                columns: new[] { "WorkflowPresetId", "Order" });

            migrationBuilder.AddForeignKey(
                name: "FK_HtmlTemplates_WorkflowPresets_WorkflowPresetId",
                table: "HtmlTemplates",
                column: "WorkflowPresetId",
                principalTable: "WorkflowPresets",
                principalColumn: "Id",
                onDelete: ReferentialAction.SetNull);

            migrationBuilder.AddForeignKey(
                name: "FK_PdfTemplates_WorkflowPresets_WorkflowPresetId",
                table: "PdfTemplates",
                column: "WorkflowPresetId",
                principalTable: "WorkflowPresets",
                principalColumn: "Id",
                onDelete: ReferentialAction.SetNull);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_HtmlTemplates_WorkflowPresets_WorkflowPresetId",
                table: "HtmlTemplates");

            migrationBuilder.DropForeignKey(
                name: "FK_PdfTemplates_WorkflowPresets_WorkflowPresetId",
                table: "PdfTemplates");

            migrationBuilder.DropTable(
                name: "WorkflowPresetSteps");

            migrationBuilder.DropTable(
                name: "WorkflowPresets");

            migrationBuilder.DropIndex(
                name: "IX_PdfTemplates_WorkflowPresetId",
                table: "PdfTemplates");

            migrationBuilder.DropIndex(
                name: "IX_HtmlTemplates_WorkflowPresetId",
                table: "HtmlTemplates");

            migrationBuilder.DropColumn(
                name: "WorkflowPresetId",
                table: "PdfTemplates");

            migrationBuilder.DropColumn(
                name: "WorkflowPresetId",
                table: "HtmlTemplates");
        }
    }
}
