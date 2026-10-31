using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace SignFlow.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class VersionFingerprintsCommentVisibility : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "FileSha256",
                table: "DocumentVersions",
                type: "nvarchar(max)",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "RoundId",
                table: "DocumentVersions",
                type: "uniqueidentifier",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "TextSha256",
                table: "DocumentVersions",
                type: "nvarchar(max)",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "TextSource",
                table: "DocumentVersions",
                type: "int",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "MentionIds",
                table: "Comments",
                type: "nvarchar(max)",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "VersionNumber",
                table: "Comments",
                type: "int",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "Visibility",
                table: "Comments",
                type: "int",
                nullable: false,
                // Every comment so far was seen by the contract's participants; it stays that way (R-M7-1).
                defaultValue: 1);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "FileSha256",
                table: "DocumentVersions");

            migrationBuilder.DropColumn(
                name: "RoundId",
                table: "DocumentVersions");

            migrationBuilder.DropColumn(
                name: "TextSha256",
                table: "DocumentVersions");

            migrationBuilder.DropColumn(
                name: "TextSource",
                table: "DocumentVersions");

            migrationBuilder.DropColumn(
                name: "MentionIds",
                table: "Comments");

            migrationBuilder.DropColumn(
                name: "VersionNumber",
                table: "Comments");

            migrationBuilder.DropColumn(
                name: "Visibility",
                table: "Comments");
        }
    }
}
