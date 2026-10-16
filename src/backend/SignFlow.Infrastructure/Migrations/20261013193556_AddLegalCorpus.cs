using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace SignFlow.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddLegalCorpus : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "SourceUrl",
                table: "RegulatoryTexts",
                type: "nvarchar(500)",
                maxLength: 500,
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "VerifiedAt",
                table: "RegulatoryTexts",
                type: "datetime2",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "VerifiedById",
                table: "RegulatoryTexts",
                type: "uniqueidentifier",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "RegulatoryArticles",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    RegulatoryTextId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Number = table.Column<string>(type: "nvarchar(30)", maxLength: 30, nullable: false),
                    Text = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    Order = table.Column<int>(type: "int", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "datetime2", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "datetime2", nullable: true),
                    CreatedBy = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: true),
                    UpdatedBy = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: true),
                    IsDeleted = table.Column<bool>(type: "bit", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_RegulatoryArticles", x => x.Id);
                    table.ForeignKey(
                        name: "FK_RegulatoryArticles_RegulatoryTexts_RegulatoryTextId",
                        column: x => x.RegulatoryTextId,
                        principalTable: "RegulatoryTexts",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_RegulatoryArticles_RegulatoryTextId_Number",
                table: "RegulatoryArticles",
                columns: new[] { "RegulatoryTextId", "Number" });

            migrationBuilder.CreateIndex(
                name: "IX_RegulatoryArticles_RegulatoryTextId_Order",
                table: "RegulatoryArticles",
                columns: new[] { "RegulatoryTextId", "Order" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "RegulatoryArticles");

            migrationBuilder.DropColumn(
                name: "SourceUrl",
                table: "RegulatoryTexts");

            migrationBuilder.DropColumn(
                name: "VerifiedAt",
                table: "RegulatoryTexts");

            migrationBuilder.DropColumn(
                name: "VerifiedById",
                table: "RegulatoryTexts");
        }
    }
}
