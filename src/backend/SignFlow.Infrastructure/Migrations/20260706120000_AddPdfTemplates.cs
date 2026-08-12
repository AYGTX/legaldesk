using System;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using SignFlow.Infrastructure.Persistence;

#nullable disable

namespace SignFlow.Infrastructure.Migrations
{
    /// <inheritdoc />
    [DbContext(typeof(ApplicationDbContext))]
    [Migration("20260706120000_AddPdfTemplates")]
    public partial class AddPdfTemplates : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<Guid>(
                name: "PdfTemplateId",
                table: "Documents",
                type: "uniqueidentifier",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "PdfTemplates",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Name = table.Column<string>(type: "nvarchar(300)", maxLength: 300, nullable: false),
                    Description = table.Column<string>(type: "nvarchar(1000)", maxLength: 1000, nullable: true),
                    IsActive = table.Column<bool>(type: "bit", nullable: false),
                    StoragePath = table.Column<string>(type: "nvarchar(1000)", maxLength: 1000, nullable: false),
                    FileName = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: false),
                    FileSizeBytes = table.Column<long>(type: "bigint", nullable: false),
                    PageCount = table.Column<int>(type: "int", nullable: false),
                    DefaultApproverId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    WorkflowType = table.Column<int>(type: "int", nullable: false),
                    HideRecipientIdentities = table.Column<bool>(type: "bit", nullable: false),
                    CreatedById = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "datetime2", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "datetime2", nullable: true),
                    CreatedBy = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    UpdatedBy = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: true),
                    IsDeleted = table.Column<bool>(type: "bit", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_PdfTemplates", x => x.Id);
                    table.ForeignKey(
                        name: "FK_PdfTemplates_Users_CreatedById",
                        column: x => x.CreatedById,
                        principalTable: "Users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_PdfTemplates_Users_DefaultApproverId",
                        column: x => x.DefaultApproverId,
                        principalTable: "Users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.SetNull);
                });

            migrationBuilder.CreateTable(
                name: "PdfTemplateFields",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    PdfTemplateId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    FieldKey = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false),
                    Label = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                    Type = table.Column<int>(type: "int", nullable: false),
                    FillRole = table.Column<int>(type: "int", nullable: false),
                    Page = table.Column<int>(type: "int", nullable: false),
                    X = table.Column<float>(type: "real", nullable: false),
                    Y = table.Column<float>(type: "real", nullable: false),
                    Width = table.Column<float>(type: "real", nullable: false),
                    Height = table.Column<float>(type: "real", nullable: false),
                    AutoFillFrom = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: true),
                    DefaultValue = table.Column<string>(type: "nvarchar(2000)", maxLength: 2000, nullable: true),
                    IsRequired = table.Column<bool>(type: "bit", nullable: false),
                    Order = table.Column<int>(type: "int", nullable: false),
                    OptionsJson = table.Column<string>(type: "nvarchar(2000)", maxLength: 2000, nullable: true),
                    PreAssignedApproverId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    WorkflowStage = table.Column<int>(type: "int", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "datetime2", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "datetime2", nullable: true),
                    CreatedBy = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: true),
                    UpdatedBy = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: true),
                    IsDeleted = table.Column<bool>(type: "bit", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_PdfTemplateFields", x => x.Id);
                    table.ForeignKey(
                        name: "FK_PdfTemplateFields_PdfTemplates_PdfTemplateId",
                        column: x => x.PdfTemplateId,
                        principalTable: "PdfTemplates",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_PdfTemplateFields_Users_PreAssignedApproverId",
                        column: x => x.PreAssignedApproverId,
                        principalTable: "Users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.SetNull);
                });

            migrationBuilder.CreateIndex(
                name: "IX_Documents_PdfTemplateId",
                table: "Documents",
                column: "PdfTemplateId");

            migrationBuilder.CreateIndex(
                name: "IX_PdfTemplateFields_PdfTemplateId_FieldKey",
                table: "PdfTemplateFields",
                columns: new[] { "PdfTemplateId", "FieldKey" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_PdfTemplateFields_PreAssignedApproverId",
                table: "PdfTemplateFields",
                column: "PreAssignedApproverId");

            migrationBuilder.CreateIndex(
                name: "IX_PdfTemplates_CreatedById",
                table: "PdfTemplates",
                column: "CreatedById");

            migrationBuilder.CreateIndex(
                name: "IX_PdfTemplates_DefaultApproverId",
                table: "PdfTemplates",
                column: "DefaultApproverId");

            migrationBuilder.CreateIndex(
                name: "IX_PdfTemplates_IsActive",
                table: "PdfTemplates",
                column: "IsActive");

            migrationBuilder.AddForeignKey(
                name: "FK_Documents_PdfTemplates_PdfTemplateId",
                table: "Documents",
                column: "PdfTemplateId",
                principalTable: "PdfTemplates",
                principalColumn: "Id",
                onDelete: ReferentialAction.SetNull);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_Documents_PdfTemplates_PdfTemplateId",
                table: "Documents");

            migrationBuilder.DropTable(
                name: "PdfTemplateFields");

            migrationBuilder.DropTable(
                name: "PdfTemplates");

            migrationBuilder.DropIndex(
                name: "IX_Documents_PdfTemplateId",
                table: "Documents");

            migrationBuilder.DropColumn(
                name: "PdfTemplateId",
                table: "Documents");
        }
    }
}
