using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using SignFlow.Infrastructure.Persistence;

#nullable disable

namespace SignFlow.Infrastructure.Migrations
{
    /// <summary>
    /// Adds SQL Server rowversion concurrency tokens to Documents and Workflows so that
    /// concurrent updates (e.g. two parallel-group approvals advancing the same workflow)
    /// surface as a 409 conflict instead of silently overwriting each other.
    /// The [Migration] attribute is declared inline (no Designer file) so the startup
    /// MigrateAsync call discovers and applies this migration automatically.
    /// </summary>
    [DbContext(typeof(ApplicationDbContext))]
    [Migration("20260711100000_AddConcurrencyTokens")]
    public partial class AddConcurrencyTokens : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<byte[]>(
                name: "RowVersion",
                table: "Documents",
                type: "rowversion",
                rowVersion: true,
                nullable: false,
                defaultValue: new byte[0]);

            migrationBuilder.AddColumn<byte[]>(
                name: "RowVersion",
                table: "Workflows",
                type: "rowversion",
                rowVersion: true,
                nullable: false,
                defaultValue: new byte[0]);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "RowVersion",
                table: "Documents");

            migrationBuilder.DropColumn(
                name: "RowVersion",
                table: "Workflows");
        }
    }
}
