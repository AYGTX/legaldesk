using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace SignFlow.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddRegulatoryWatch : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
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

            migrationBuilder.CreateTable(
                name: "RegulatoryTexts",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Title = table.Column<string>(type: "nvarchar(300)", maxLength: 300, nullable: false),
                    Reference = table.Column<string>(type: "nvarchar(300)", maxLength: 300, nullable: true),
                    Summary = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    Keywords = table.Column<string>(type: "nvarchar(1000)", maxLength: 1000, nullable: false),
                    Language = table.Column<string>(type: "nvarchar(5)", maxLength: 5, nullable: false),
                    Jurisdiction = table.Column<int>(type: "int", nullable: false),
                    Category = table.Column<int>(type: "int", nullable: false),
                    Status = table.Column<int>(type: "int", nullable: false),
                    EffectiveDate = table.Column<DateTime>(type: "datetime2", nullable: true),
                    RequestTypeId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    CreatedById = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "datetime2", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "datetime2", nullable: true),
                    CreatedBy = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: true),
                    UpdatedBy = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: true),
                    IsDeleted = table.Column<bool>(type: "bit", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_RegulatoryTexts", x => x.Id);
                    table.ForeignKey(
                        name: "FK_RegulatoryTexts_RequestTypes_RequestTypeId",
                        column: x => x.RequestTypeId,
                        principalTable: "RequestTypes",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.SetNull);
                    table.ForeignKey(
                        name: "FK_RegulatoryTexts_Users_CreatedById",
                        column: x => x.CreatedById,
                        principalTable: "Users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_RegulatoryTexts_Category",
                table: "RegulatoryTexts",
                column: "Category");

            migrationBuilder.CreateIndex(
                name: "IX_RegulatoryTexts_CreatedById",
                table: "RegulatoryTexts",
                column: "CreatedById");

            migrationBuilder.CreateIndex(
                name: "IX_RegulatoryTexts_Jurisdiction",
                table: "RegulatoryTexts",
                column: "Jurisdiction");

            migrationBuilder.CreateIndex(
                name: "IX_RegulatoryTexts_Language",
                table: "RegulatoryTexts",
                column: "Language");

            migrationBuilder.CreateIndex(
                name: "IX_RegulatoryTexts_RequestTypeId",
                table: "RegulatoryTexts",
                column: "RequestTypeId");

            migrationBuilder.CreateIndex(
                name: "IX_RegulatoryTexts_Status",
                table: "RegulatoryTexts",
                column: "Status");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "RegulatoryTexts");

            migrationBuilder.DropColumn(
                name: "AiComplianceAnalysisGeneratedAt",
                table: "Documents");

            migrationBuilder.DropColumn(
                name: "AiComplianceAnalysisJson",
                table: "Documents");
        }
    }
}
