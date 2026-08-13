using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace SignFlow.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddContractReadiness : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<DateTime>(
                name: "ReadinessEvaluatedAt",
                table: "Documents",
                type: "datetime2",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "ReadinessScore",
                table: "Documents",
                type: "int",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "ReadinessVerdict",
                table: "Documents",
                type: "int",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.CreateTable(
                name: "DocumentReadinessItems",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    DocumentId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Category = table.Column<int>(type: "int", nullable: false),
                    Severity = table.Column<string>(type: "nvarchar(32)", maxLength: 32, nullable: false),
                    SourceKey = table.Column<string>(type: "nvarchar(256)", maxLength: 256, nullable: false),
                    Title = table.Column<string>(type: "nvarchar(300)", maxLength: 300, nullable: false),
                    Detail = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    WhyItMatters = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    RecommendedAction = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    ProposedLanguage = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    Status = table.Column<int>(type: "int", nullable: false),
                    ResolutionNote = table.Column<string>(type: "nvarchar(1000)", maxLength: 1000, nullable: true),
                    ResolvedByUserId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    ResolvedAt = table.Column<DateTime>(type: "datetime2", nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "datetime2", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "datetime2", nullable: true),
                    CreatedBy = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: true),
                    UpdatedBy = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: true),
                    IsDeleted = table.Column<bool>(type: "bit", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_DocumentReadinessItems", x => x.Id);
                    table.ForeignKey(
                        name: "FK_DocumentReadinessItems_Documents_DocumentId",
                        column: x => x.DocumentId,
                        principalTable: "Documents",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_DocumentReadinessItems_DocumentId_SourceKey",
                table: "DocumentReadinessItems",
                columns: new[] { "DocumentId", "SourceKey" });

            migrationBuilder.CreateIndex(
                name: "IX_DocumentReadinessItems_Status",
                table: "DocumentReadinessItems",
                column: "Status");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "DocumentReadinessItems");

            migrationBuilder.DropColumn(
                name: "ReadinessEvaluatedAt",
                table: "Documents");

            migrationBuilder.DropColumn(
                name: "ReadinessScore",
                table: "Documents");

            migrationBuilder.DropColumn(
                name: "ReadinessVerdict",
                table: "Documents");
        }
    }
}
