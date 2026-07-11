using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace SignFlow.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddHideRecipientIdentitiesToDocument : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<bool>(
                name: "HideRecipientIdentities",
                table: "Documents",
                type: "bit",
                nullable: false,
                defaultValue: false);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "HideRecipientIdentities",
                table: "Documents");
        }
    }
}
