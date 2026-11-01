using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace SignFlow.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class LibraryClausesAndFamilies : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<Guid>(
                name: "DefaultPresetId",
                table: "RequestTypes",
                type: "uniqueidentifier",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "RuleSetKeys",
                table: "RequestTypes",
                type: "nvarchar(max)",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "LibraryClausesJson",
                table: "Documents",
                type: "nvarchar(max)",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "DefaultPresetId",
                table: "RequestTypes");

            migrationBuilder.DropColumn(
                name: "RuleSetKeys",
                table: "RequestTypes");

            migrationBuilder.DropColumn(
                name: "LibraryClausesJson",
                table: "Documents");
        }
    }
}
