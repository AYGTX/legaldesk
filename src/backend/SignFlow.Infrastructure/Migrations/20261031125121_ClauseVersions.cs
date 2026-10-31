using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace SignFlow.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class ClauseVersions : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "ClauseRuleLinks",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    ClauseId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    RuleKey = table.Column<string>(type: "nvarchar(120)", maxLength: 120, nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "datetime2", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "datetime2", nullable: true),
                    CreatedBy = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: true),
                    UpdatedBy = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: true),
                    IsDeleted = table.Column<bool>(type: "bit", nullable: false),
                    OrganisationId = table.Column<Guid>(type: "uniqueidentifier", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ClauseRuleLinks", x => x.Id);
                    table.ForeignKey(
                        name: "FK_ClauseRuleLinks_Clauses_ClauseId",
                        column: x => x.ClauseId,
                        principalTable: "Clauses",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_ClauseRuleLinks_Organisations_OrganisationId",
                        column: x => x.OrganisationId,
                        principalTable: "Organisations",
                        principalColumn: "Id");
                });

            migrationBuilder.CreateTable(
                name: "ClauseVersions",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    ClauseId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Number = table.Column<int>(type: "int", nullable: false),
                    Body = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    Language = table.Column<string>(type: "nvarchar(5)", maxLength: 5, nullable: false),
                    Status = table.Column<int>(type: "int", nullable: false),
                    AuthorId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    ApprovedById = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    ApprovedAt = table.Column<DateTime>(type: "datetime2", nullable: true),
                    DeprecatedAt = table.Column<DateTime>(type: "datetime2", nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "datetime2", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "datetime2", nullable: true),
                    CreatedBy = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: true),
                    UpdatedBy = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: true),
                    IsDeleted = table.Column<bool>(type: "bit", nullable: false),
                    OrganisationId = table.Column<Guid>(type: "uniqueidentifier", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ClauseVersions", x => x.Id);
                    table.ForeignKey(
                        name: "FK_ClauseVersions_Clauses_ClauseId",
                        column: x => x.ClauseId,
                        principalTable: "Clauses",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_ClauseVersions_Organisations_OrganisationId",
                        column: x => x.OrganisationId,
                        principalTable: "Organisations",
                        principalColumn: "Id");
                });

            migrationBuilder.CreateIndex(
                name: "IX_ClauseRuleLinks_ClauseId_RuleKey",
                table: "ClauseRuleLinks",
                columns: new[] { "ClauseId", "RuleKey" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_ClauseRuleLinks_OrganisationId",
                table: "ClauseRuleLinks",
                column: "OrganisationId");

            migrationBuilder.CreateIndex(
                name: "IX_ClauseRuleLinks_RuleKey",
                table: "ClauseRuleLinks",
                column: "RuleKey");

            migrationBuilder.CreateIndex(
                name: "IX_ClauseVersions_ClauseId_Number",
                table: "ClauseVersions",
                columns: new[] { "ClauseId", "Number" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_ClauseVersions_OrganisationId",
                table: "ClauseVersions",
                column: "OrganisationId");

            migrationBuilder.CreateIndex(
                name: "IX_ClauseVersions_Status",
                table: "ClauseVersions",
                column: "Status");

            // Data (R-M9-1): each clause's wording becomes a version with the number it had. An approved clause was signed
            // off before approvals were recorded, so its version has no approver; the author is whoever created the clause.
            migrationBuilder.Sql(@"
INSERT INTO ClauseVersions (Id, ClauseId, Number, Body, Language, Status, AuthorId, ApprovedById, ApprovedAt, DeprecatedAt,
                            CreatedAt, UpdatedAt, CreatedBy, UpdatedBy, IsDeleted, OrganisationId)
SELECT NEWID(), c.Id, c.Version, c.Body, c.Language, c.Status, c.CreatedById, NULL,
       CASE WHEN c.Status = 1 THEN COALESCE(c.UpdatedAt, c.CreatedAt) END,
       CASE WHEN c.Status = 2 THEN COALESCE(c.UpdatedAt, c.CreatedAt) END,
       COALESCE(c.UpdatedAt, c.CreatedAt), NULL, c.CreatedBy, NULL, 0, c.OrganisationId
FROM Clauses c;

-- Risk level is no longer free text (R-M9-5): Low, Medium, High become 0, 1, 2; anything else is dropped.
UPDATE Clauses SET RiskLevel = CASE LOWER(LTRIM(RTRIM(RiskLevel)))
    WHEN 'low' THEN '0' WHEN 'medium' THEN '1' WHEN 'high' THEN '2' ELSE NULL END;
");

            migrationBuilder.DropIndex(
                name: "IX_Clauses_Language",
                table: "Clauses");

            migrationBuilder.DropIndex(
                name: "IX_Clauses_Status",
                table: "Clauses");

            migrationBuilder.DropColumn(
                name: "Body",
                table: "Clauses");

            migrationBuilder.DropColumn(
                name: "Language",
                table: "Clauses");

            migrationBuilder.DropColumn(
                name: "Status",
                table: "Clauses");

            migrationBuilder.DropColumn(
                name: "Version",
                table: "Clauses");

            migrationBuilder.AlterColumn<int>(
                name: "RiskLevel",
                table: "Clauses",
                type: "int",
                nullable: true,
                oldClrType: typeof(string),
                oldType: "nvarchar(20)",
                oldMaxLength: 20,
                oldNullable: true);

        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AlterColumn<string>(
                name: "RiskLevel",
                table: "Clauses",
                type: "nvarchar(20)",
                maxLength: 20,
                nullable: true,
                oldClrType: typeof(int),
                oldType: "int",
                oldNullable: true);

            migrationBuilder.AddColumn<string>(
                name: "Body",
                table: "Clauses",
                type: "nvarchar(max)",
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<string>(
                name: "Language",
                table: "Clauses",
                type: "nvarchar(5)",
                maxLength: 5,
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<int>(
                name: "Status",
                table: "Clauses",
                type: "int",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<int>(
                name: "Version",
                table: "Clauses",
                type: "int",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.CreateIndex(
                name: "IX_Clauses_Language",
                table: "Clauses",
                column: "Language");

            migrationBuilder.CreateIndex(
                name: "IX_Clauses_Status",
                table: "Clauses",
                column: "Status");

            // Back to one wording per clause: the approved one, else the newest.
            migrationBuilder.Sql(@"
UPDATE c SET Body = v.Body, Language = v.Language, Status = v.Status, Version = v.Number,
    RiskLevel = CASE c.RiskLevel WHEN '0' THEN 'Low' WHEN '1' THEN 'Medium' WHEN '2' THEN 'High' END
FROM Clauses c
CROSS APPLY (SELECT TOP 1 * FROM ClauseVersions x WHERE x.ClauseId = c.Id ORDER BY CASE WHEN x.Status = 1 THEN 0 ELSE 1 END, x.Number DESC) v;
");

            migrationBuilder.DropTable(
                name: "ClauseRuleLinks");

            migrationBuilder.DropTable(
                name: "ClauseVersions");
        }
    }
}
