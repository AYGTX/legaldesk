using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using SignFlow.Infrastructure.Persistence;

#nullable disable

namespace SignFlow.Infrastructure.Migrations
{
    /// <inheritdoc />
    [DbContext(typeof(ApplicationDbContext))]
    [Migration("20260705120000_AddWorkflowConfigToHtmlTemplates")]
    public partial class AddWorkflowConfigToHtmlTemplates : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "WorkflowType",
                table: "HtmlTemplates",
                type: "int",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<bool>(
                name: "HideRecipientIdentities",
                table: "HtmlTemplates",
                type: "bit",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<Guid>(
                name: "PreAssignedApproverId",
                table: "HtmlTemplateFields",
                type: "uniqueidentifier",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "WorkflowStage",
                table: "HtmlTemplateFields",
                type: "int",
                nullable: false,
                defaultValue: 1);

            migrationBuilder.CreateIndex(
                name: "IX_HtmlTemplateFields_PreAssignedApproverId",
                table: "HtmlTemplateFields",
                column: "PreAssignedApproverId");

            migrationBuilder.AddForeignKey(
                name: "FK_HtmlTemplateFields_Users_PreAssignedApproverId",
                table: "HtmlTemplateFields",
                column: "PreAssignedApproverId",
                principalTable: "Users",
                principalColumn: "Id",
                onDelete: ReferentialAction.SetNull);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_HtmlTemplateFields_Users_PreAssignedApproverId",
                table: "HtmlTemplateFields");

            migrationBuilder.DropIndex(
                name: "IX_HtmlTemplateFields_PreAssignedApproverId",
                table: "HtmlTemplateFields");

            migrationBuilder.DropColumn(
                name: "WorkflowType",
                table: "HtmlTemplates");

            migrationBuilder.DropColumn(
                name: "HideRecipientIdentities",
                table: "HtmlTemplates");

            migrationBuilder.DropColumn(
                name: "PreAssignedApproverId",
                table: "HtmlTemplateFields");

            migrationBuilder.DropColumn(
                name: "WorkflowStage",
                table: "HtmlTemplateFields");
        }
    }
}
