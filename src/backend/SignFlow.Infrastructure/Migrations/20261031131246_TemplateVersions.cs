using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace SignFlow.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class TemplateVersions : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<DateTime>(
                name: "ApprovedAt",
                table: "PdfTemplates",
                type: "datetime2",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "ApprovedById",
                table: "PdfTemplates",
                type: "uniqueidentifier",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "ParentTemplateId",
                table: "PdfTemplates",
                type: "uniqueidentifier",
                nullable: true);

            // Templates in use before versions existed are approved version 1 (R-M6-5): requesters keep them.
            migrationBuilder.AddColumn<int>(
                name: "Status",
                table: "PdfTemplates",
                type: "int",
                nullable: false,
                defaultValue: 2);

            migrationBuilder.AddColumn<DateTime>(
                name: "SubmittedAt",
                table: "PdfTemplates",
                type: "datetime2",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "SubmittedById",
                table: "PdfTemplates",
                type: "uniqueidentifier",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "Version",
                table: "PdfTemplates",
                type: "int",
                nullable: false,
                defaultValue: 1);

            migrationBuilder.AddColumn<DateTime>(
                name: "ApprovedAt",
                table: "HtmlTemplates",
                type: "datetime2",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "ApprovedById",
                table: "HtmlTemplates",
                type: "uniqueidentifier",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "ParentTemplateId",
                table: "HtmlTemplates",
                type: "uniqueidentifier",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "Status",
                table: "HtmlTemplates",
                type: "int",
                nullable: false,
                defaultValue: 2);

            migrationBuilder.AddColumn<DateTime>(
                name: "SubmittedAt",
                table: "HtmlTemplates",
                type: "datetime2",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "SubmittedById",
                table: "HtmlTemplates",
                type: "uniqueidentifier",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "Version",
                table: "HtmlTemplates",
                type: "int",
                nullable: false,
                defaultValue: 1);

            migrationBuilder.AddColumn<int>(
                name: "TemplateVersion",
                table: "Documents",
                type: "int",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "ApprovedAt",
                table: "PdfTemplates");

            migrationBuilder.DropColumn(
                name: "ApprovedById",
                table: "PdfTemplates");

            migrationBuilder.DropColumn(
                name: "ParentTemplateId",
                table: "PdfTemplates");

            migrationBuilder.DropColumn(
                name: "Status",
                table: "PdfTemplates");

            migrationBuilder.DropColumn(
                name: "SubmittedAt",
                table: "PdfTemplates");

            migrationBuilder.DropColumn(
                name: "SubmittedById",
                table: "PdfTemplates");

            migrationBuilder.DropColumn(
                name: "Version",
                table: "PdfTemplates");

            migrationBuilder.DropColumn(
                name: "ApprovedAt",
                table: "HtmlTemplates");

            migrationBuilder.DropColumn(
                name: "ApprovedById",
                table: "HtmlTemplates");

            migrationBuilder.DropColumn(
                name: "ParentTemplateId",
                table: "HtmlTemplates");

            migrationBuilder.DropColumn(
                name: "Status",
                table: "HtmlTemplates");

            migrationBuilder.DropColumn(
                name: "SubmittedAt",
                table: "HtmlTemplates");

            migrationBuilder.DropColumn(
                name: "SubmittedById",
                table: "HtmlTemplates");

            migrationBuilder.DropColumn(
                name: "Version",
                table: "HtmlTemplates");

            migrationBuilder.DropColumn(
                name: "TemplateVersion",
                table: "Documents");
        }
    }
}
