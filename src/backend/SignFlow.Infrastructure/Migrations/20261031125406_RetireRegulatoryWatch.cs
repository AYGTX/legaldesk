using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace SignFlow.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class RetireRegulatoryWatch : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // R-M9-6: the regulatory watch is retired (D6), its texts exported first. The export is kept in the database,
            // one JSON document per organisation, for a lawyer to verify and import into Harvey's corpus:
            //   SELECT Json FROM LegacyExports WHERE Name = 'regulatory-watch'
            // The table is the migration's own: no entity maps it, and it is never dropped by a later model change.
            migrationBuilder.Sql(@"
IF OBJECT_ID('LegacyExports') IS NULL
    CREATE TABLE LegacyExports (
        Id uniqueidentifier NOT NULL PRIMARY KEY,
        Name nvarchar(100) NOT NULL,
        OrganisationId uniqueidentifier NULL,
        ExportedAt datetime2 NOT NULL,
        Json nvarchar(max) NOT NULL);

INSERT INTO LegacyExports (Id, Name, OrganisationId, ExportedAt, Json)
SELECT NEWID(), 'regulatory-watch', o.OrganisationId, SYSUTCDATETIME(),
       (SELECT t.*,
               (SELECT a.* FROM RegulatoryArticles a WHERE a.RegulatoryTextId = t.Id ORDER BY a.Id FOR JSON PATH) AS Articles
        FROM RegulatoryTexts t WHERE t.OrganisationId = o.OrganisationId
        FOR JSON PATH)
FROM (SELECT DISTINCT OrganisationId FROM RegulatoryTexts) o;

-- The alerts the watch sent (NotificationType 10) point at nothing any more.
DELETE FROM Notifications WHERE Type = 10;
");

            migrationBuilder.DropTable(
                name: "RegulatoryArticles");

            migrationBuilder.DropTable(
                name: "RegulatoryTexts");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "RegulatoryTexts",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    CreatedById = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    RequestTypeId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    Category = table.Column<int>(type: "int", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "datetime2", nullable: false),
                    CreatedBy = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: true),
                    EffectiveDate = table.Column<DateTime>(type: "datetime2", nullable: true),
                    IsDeleted = table.Column<bool>(type: "bit", nullable: false),
                    Jurisdiction = table.Column<int>(type: "int", nullable: false),
                    Keywords = table.Column<string>(type: "nvarchar(1000)", maxLength: 1000, nullable: false),
                    Language = table.Column<string>(type: "nvarchar(5)", maxLength: 5, nullable: false),
                    OrganisationId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Reference = table.Column<string>(type: "nvarchar(300)", maxLength: 300, nullable: true),
                    SourceUrl = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: true),
                    Status = table.Column<int>(type: "int", nullable: false),
                    Summary = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    Title = table.Column<string>(type: "nvarchar(300)", maxLength: 300, nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "datetime2", nullable: true),
                    UpdatedBy = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: true),
                    VerifiedAt = table.Column<DateTime>(type: "datetime2", nullable: true),
                    VerifiedById = table.Column<Guid>(type: "uniqueidentifier", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_RegulatoryTexts", x => x.Id);
                    table.ForeignKey(
                        name: "FK_RegulatoryTexts_Organisations_OrganisationId",
                        column: x => x.OrganisationId,
                        principalTable: "Organisations",
                        principalColumn: "Id");
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

            migrationBuilder.CreateTable(
                name: "RegulatoryArticles",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    RegulatoryTextId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "datetime2", nullable: false),
                    CreatedBy = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: true),
                    IsDeleted = table.Column<bool>(type: "bit", nullable: false),
                    Number = table.Column<string>(type: "nvarchar(30)", maxLength: 30, nullable: false),
                    Order = table.Column<int>(type: "int", nullable: false),
                    OrganisationId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Text = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "datetime2", nullable: true),
                    UpdatedBy = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_RegulatoryArticles", x => x.Id);
                    table.ForeignKey(
                        name: "FK_RegulatoryArticles_Organisations_OrganisationId",
                        column: x => x.OrganisationId,
                        principalTable: "Organisations",
                        principalColumn: "Id");
                    table.ForeignKey(
                        name: "FK_RegulatoryArticles_RegulatoryTexts_RegulatoryTextId",
                        column: x => x.RegulatoryTextId,
                        principalTable: "RegulatoryTexts",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_RegulatoryArticles_OrganisationId",
                table: "RegulatoryArticles",
                column: "OrganisationId");

            migrationBuilder.CreateIndex(
                name: "IX_RegulatoryArticles_RegulatoryTextId_Number",
                table: "RegulatoryArticles",
                columns: new[] { "RegulatoryTextId", "Number" });

            migrationBuilder.CreateIndex(
                name: "IX_RegulatoryArticles_RegulatoryTextId_Order",
                table: "RegulatoryArticles",
                columns: new[] { "RegulatoryTextId", "Order" });

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
                name: "IX_RegulatoryTexts_OrganisationId",
                table: "RegulatoryTexts",
                column: "OrganisationId");

            migrationBuilder.CreateIndex(
                name: "IX_RegulatoryTexts_RequestTypeId",
                table: "RegulatoryTexts",
                column: "RequestTypeId");

            migrationBuilder.CreateIndex(
                name: "IX_RegulatoryTexts_Status",
                table: "RegulatoryTexts",
                column: "Status");
        }
    }
}
