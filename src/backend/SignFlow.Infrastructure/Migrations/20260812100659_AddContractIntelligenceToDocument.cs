using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace SignFlow.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddContractIntelligenceToDocument : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_Documents_PdfTemplates_PdfTemplateId",
                table: "Documents");

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

            migrationBuilder.AddColumn<DateTime>(
                name: "LastObligationReminderSentAt",
                table: "Documents",
                type: "datetime2",
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

            migrationBuilder.AddForeignKey(
                name: "FK_Documents_PdfTemplates_PdfTemplateId",
                table: "Documents",
                column: "PdfTemplateId",
                principalTable: "PdfTemplates",
                principalColumn: "Id");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_Documents_PdfTemplates_PdfTemplateId",
                table: "Documents");

            migrationBuilder.DropColumn(
                name: "AiRiskAnalysisGeneratedAt",
                table: "Documents");

            migrationBuilder.DropColumn(
                name: "AiRiskAnalysisJson",
                table: "Documents");

            migrationBuilder.DropColumn(
                name: "LastObligationReminderSentAt",
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

            migrationBuilder.AddForeignKey(
                name: "FK_Documents_PdfTemplates_PdfTemplateId",
                table: "Documents",
                column: "PdfTemplateId",
                principalTable: "PdfTemplates",
                principalColumn: "Id",
                onDelete: ReferentialAction.SetNull);
        }
    }
}
