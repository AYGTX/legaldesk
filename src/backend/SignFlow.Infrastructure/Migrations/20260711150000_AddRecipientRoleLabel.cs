using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using SignFlow.Infrastructure.Persistence;

#nullable disable

namespace SignFlow.Infrastructure.Migrations
{
    /// <summary>
    /// Adds the free-text participant role label (e.g. "Legal team", "Compliance officer")
    /// to Recipients. The [Migration] attribute is declared inline (no Designer file) so the
    /// startup MigrateAsync call discovers and applies this migration automatically.
    /// </summary>
    [DbContext(typeof(ApplicationDbContext))]
    [Migration("20260711150000_AddRecipientRoleLabel")]
    public partial class AddRecipientRoleLabel : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "RoleLabel",
                table: "Recipients",
                type: "nvarchar(100)",
                maxLength: 100,
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "RoleLabel",
                table: "Recipients");
        }
    }
}
