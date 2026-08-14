using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace SignFlow.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddContractTermToDocument : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
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
                name: "TermConfirmed",
                table: "Documents",
                type: "bit",
                nullable: false,
                defaultValue: false);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
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
                name: "LastTermAlertSentAt",
                table: "Documents");

            migrationBuilder.DropColumn(
                name: "LastTermAlertStage",
                table: "Documents");

            migrationBuilder.DropColumn(
                name: "NoticePeriodDays",
                table: "Documents");

            migrationBuilder.DropColumn(
                name: "TermConfirmed",
                table: "Documents");
        }
    }
}
