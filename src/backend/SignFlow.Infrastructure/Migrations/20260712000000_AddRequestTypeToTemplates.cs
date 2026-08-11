using System;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using SignFlow.Infrastructure.Persistence;

#nullable disable

namespace SignFlow.Infrastructure.Migrations
{
    /// <summary>
    /// Links HTML/PDF templates to a RequestType. The [Migration] attribute is declared inline
    /// (no Designer file) so the startup MigrateAsync call discovers and applies this migration
    /// automatically — without it, EF never sees the class and the columns are never created.
    /// </summary>
    [DbContext(typeof(ApplicationDbContext))]
    [Migration("20260712000000_AddRequestTypeToTemplates")]
    public partial class AddRequestTypeToTemplates : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<Guid>(
                name: "RequestTypeId",
                table: "PdfTemplates",
                type: "uniqueidentifier",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "RequestTypeId",
                table: "HtmlTemplates",
                type: "uniqueidentifier",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_PdfTemplates_RequestTypeId",
                table: "PdfTemplates",
                column: "RequestTypeId");

            migrationBuilder.CreateIndex(
                name: "IX_HtmlTemplates_RequestTypeId",
                table: "HtmlTemplates",
                column: "RequestTypeId");

            migrationBuilder.AddForeignKey(
                name: "FK_HtmlTemplates_RequestTypes_RequestTypeId",
                table: "HtmlTemplates",
                column: "RequestTypeId",
                principalTable: "RequestTypes",
                principalColumn: "Id",
                onDelete: ReferentialAction.SetNull);

            migrationBuilder.AddForeignKey(
                name: "FK_PdfTemplates_RequestTypes_RequestTypeId",
                table: "PdfTemplates",
                column: "RequestTypeId",
                principalTable: "RequestTypes",
                principalColumn: "Id",
                onDelete: ReferentialAction.SetNull);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_HtmlTemplates_RequestTypes_RequestTypeId",
                table: "HtmlTemplates");

            migrationBuilder.DropForeignKey(
                name: "FK_PdfTemplates_RequestTypes_RequestTypeId",
                table: "PdfTemplates");

            migrationBuilder.DropIndex(
                name: "IX_HtmlTemplates_RequestTypeId",
                table: "HtmlTemplates");

            migrationBuilder.DropIndex(
                name: "IX_PdfTemplates_RequestTypeId",
                table: "PdfTemplates");

            migrationBuilder.DropColumn(
                name: "RequestTypeId",
                table: "PdfTemplates");

            migrationBuilder.DropColumn(
                name: "RequestTypeId",
                table: "HtmlTemplates");
        }
    }
}
