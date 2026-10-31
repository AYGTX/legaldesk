using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace SignFlow.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class FieldsInPoints : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "FieldName",
                table: "SignatureFields");

            migrationBuilder.DropColumn(
                name: "MinLength",
                table: "SignatureFields");

            migrationBuilder.DropColumn(
                name: "VisibilityRules",
                table: "SignatureFields");

            migrationBuilder.DropColumn(
                name: "ZIndex",
                table: "SignatureFields");

            migrationBuilder.AddColumn<int>(
                name: "LayoutVersion",
                table: "Documents",
                type: "int",
                nullable: false,
                defaultValue: 0);

            // Coordinates were PDF.js canvas pixels at scale 1.5; they are PDF points from now on (R-M3-1, R-M3-8).
            // PDF.js draws a page as displayed (after its /Rotate), so dividing by 1.5 gives points as displayed: the
            // stamper applies the page's rotation from there. Fields that end up outside their page are listed by
            // GET documents/field-layout-report.
            migrationBuilder.Sql("UPDATE SignatureFields SET X = X / 1.5, Y = Y / 1.5, Width = Width / 1.5, Height = Height / 1.5;");
            migrationBuilder.Sql("UPDATE PdfTemplateFields SET X = X / 1.5, Y = Y / 1.5, Width = Width / 1.5, Height = Height / 1.5;");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "LayoutVersion",
                table: "Documents");

            migrationBuilder.AddColumn<string>(
                name: "FieldName",
                table: "SignatureFields",
                type: "nvarchar(100)",
                maxLength: 100,
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "MinLength",
                table: "SignatureFields",
                type: "int",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "VisibilityRules",
                table: "SignatureFields",
                type: "nvarchar(2000)",
                maxLength: 2000,
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "ZIndex",
                table: "SignatureFields",
                type: "int",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.Sql("UPDATE SignatureFields SET X = X * 1.5, Y = Y * 1.5, Width = Width * 1.5, Height = Height * 1.5;");
            migrationBuilder.Sql("UPDATE PdfTemplateFields SET X = X * 1.5, Y = Y * 1.5, Width = Width * 1.5, Height = Height * 1.5;");
        }
    }
}
