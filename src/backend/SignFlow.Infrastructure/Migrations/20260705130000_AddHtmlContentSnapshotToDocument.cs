using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using SignFlow.Infrastructure.Persistence;

#nullable disable

namespace SignFlow.Infrastructure.Migrations
{
    /// <inheritdoc />
    [DbContext(typeof(ApplicationDbContext))]
    [Migration("20260705130000_AddHtmlContentSnapshotToDocument")]
    public partial class AddHtmlContentSnapshotToDocument : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "HtmlContentSnapshot",
                table: "Documents",
                type: "nvarchar(max)",
                nullable: true);

            // Backfill: for existing HTML-template documents, snapshot the current (live) template
            // content so they keep rendering correctly even if the template is edited or deleted later.
            migrationBuilder.Sql(@"
                UPDATE d
                SET d.HtmlContentSnapshot = t.HtmlContent
                FROM Documents d
                INNER JOIN HtmlTemplates t ON t.Id = d.HtmlTemplateId
                WHERE d.HtmlTemplateId IS NOT NULL AND d.HtmlContentSnapshot IS NULL;
            ");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "HtmlContentSnapshot",
                table: "Documents");
        }
    }
}
