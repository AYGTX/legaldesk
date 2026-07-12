using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using SignFlow.Infrastructure.Persistence;

#nullable disable

namespace SignFlow.Infrastructure.Migrations
{
    /// <summary>
    /// Drops the unique constraint on Signatures.SignatureFieldId, turning the field↔signature
    /// relationship one-to-many. A field re-signed after a revision (request-changes loop)
    /// accumulates one Signature row per round — earlier rows are kept as evidence.
    /// The [Migration] attribute is declared inline (no Designer file) so the startup
    /// MigrateAsync call discovers and applies this migration automatically.
    /// </summary>
    [DbContext(typeof(ApplicationDbContext))]
    [Migration("20260711170000_SignatureFieldManySignatures")]
    public partial class SignatureFieldManySignatures : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_Signatures_SignatureFieldId",
                table: "Signatures");

            migrationBuilder.CreateIndex(
                name: "IX_Signatures_SignatureFieldId",
                table: "Signatures",
                column: "SignatureFieldId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_Signatures_SignatureFieldId",
                table: "Signatures");

            migrationBuilder.CreateIndex(
                name: "IX_Signatures_SignatureFieldId",
                table: "Signatures",
                column: "SignatureFieldId",
                unique: true);
        }
    }
}
