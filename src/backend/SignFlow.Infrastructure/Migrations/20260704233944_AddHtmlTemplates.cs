using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace SignFlow.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddHtmlTemplates : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_DataSubjectRequests_Status",
                table: "DataSubjectRequests");

            migrationBuilder.DropIndex(
                name: "IX_DataSubjectRequests_SubjectEmail",
                table: "DataSubjectRequests");

            migrationBuilder.DropIndex(
                name: "IX_ConsentLogs_UserId",
                table: "ConsentLogs");

            migrationBuilder.AddColumn<string>(
                name: "FilledValuesJson",
                table: "Documents",
                type: "nvarchar(max)",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "HtmlTemplateId",
                table: "Documents",
                type: "uniqueidentifier",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "HtmlTemplates",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Name = table.Column<string>(type: "nvarchar(300)", maxLength: 300, nullable: false),
                    Description = table.Column<string>(type: "nvarchar(1000)", maxLength: 1000, nullable: true),
                    HtmlContent = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    IsActive = table.Column<bool>(type: "bit", nullable: false),
                    DefaultApproverId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    CreatedById = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "datetime2", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "datetime2", nullable: true),
                    CreatedBy = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    UpdatedBy = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: true),
                    IsDeleted = table.Column<bool>(type: "bit", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_HtmlTemplates", x => x.Id);
                    table.ForeignKey(
                        name: "FK_HtmlTemplates_Users_CreatedById",
                        column: x => x.CreatedById,
                        principalTable: "Users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_HtmlTemplates_Users_DefaultApproverId",
                        column: x => x.DefaultApproverId,
                        principalTable: "Users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.SetNull);
                });

            migrationBuilder.CreateTable(
                name: "HtmlTemplateFields",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    HtmlTemplateId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    FieldKey = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false),
                    Label = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                    Type = table.Column<int>(type: "int", nullable: false),
                    Role = table.Column<int>(type: "int", nullable: false),
                    AutoFillFrom = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: true),
                    IsRequired = table.Column<bool>(type: "bit", nullable: false),
                    Order = table.Column<int>(type: "int", nullable: false),
                    OptionsJson = table.Column<string>(type: "nvarchar(2000)", maxLength: 2000, nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "datetime2", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "datetime2", nullable: true),
                    CreatedBy = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: true),
                    UpdatedBy = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: true),
                    IsDeleted = table.Column<bool>(type: "bit", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_HtmlTemplateFields", x => x.Id);
                    table.ForeignKey(
                        name: "FK_HtmlTemplateFields_HtmlTemplates_HtmlTemplateId",
                        column: x => x.HtmlTemplateId,
                        principalTable: "HtmlTemplates",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_Documents_HtmlTemplateId",
                table: "Documents",
                column: "HtmlTemplateId");

            migrationBuilder.CreateIndex(
                name: "IX_HtmlTemplateFields_HtmlTemplateId_FieldKey",
                table: "HtmlTemplateFields",
                columns: new[] { "HtmlTemplateId", "FieldKey" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_HtmlTemplates_CreatedById",
                table: "HtmlTemplates",
                column: "CreatedById");

            migrationBuilder.CreateIndex(
                name: "IX_HtmlTemplates_DefaultApproverId",
                table: "HtmlTemplates",
                column: "DefaultApproverId");

            migrationBuilder.CreateIndex(
                name: "IX_HtmlTemplates_IsActive",
                table: "HtmlTemplates",
                column: "IsActive");

            migrationBuilder.AddForeignKey(
                name: "FK_Documents_HtmlTemplates_HtmlTemplateId",
                table: "Documents",
                column: "HtmlTemplateId",
                principalTable: "HtmlTemplates",
                principalColumn: "Id",
                onDelete: ReferentialAction.SetNull);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_Documents_HtmlTemplates_HtmlTemplateId",
                table: "Documents");

            migrationBuilder.DropTable(
                name: "HtmlTemplateFields");

            migrationBuilder.DropTable(
                name: "HtmlTemplates");

            migrationBuilder.DropIndex(
                name: "IX_Documents_HtmlTemplateId",
                table: "Documents");

            migrationBuilder.DropColumn(
                name: "FilledValuesJson",
                table: "Documents");

            migrationBuilder.DropColumn(
                name: "HtmlTemplateId",
                table: "Documents");

            migrationBuilder.CreateIndex(
                name: "IX_DataSubjectRequests_Status",
                table: "DataSubjectRequests",
                column: "Status");

            migrationBuilder.CreateIndex(
                name: "IX_DataSubjectRequests_SubjectEmail",
                table: "DataSubjectRequests",
                column: "SubjectEmail");

            migrationBuilder.CreateIndex(
                name: "IX_ConsentLogs_UserId",
                table: "ConsentLogs",
                column: "UserId");
        }
    }
}
