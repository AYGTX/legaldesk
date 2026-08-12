using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using SignFlow.Infrastructure.Persistence;

#nullable disable

namespace SignFlow.Infrastructure.Migrations
{
    /// <inheritdoc />
    [DbContext(typeof(ApplicationDbContext))]
    [Migration("20260704210000_AddGdprTables")]
    public partial class AddGdprTables : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "ConsentLogs",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    UserId = table.Column<string>(type: "nvarchar(450)", maxLength: 450, nullable: false),
                    Tier = table.Column<int>(type: "int", nullable: false),
                    Granted = table.Column<bool>(type: "bit", nullable: false),
                    IpAddress = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: true),
                    UserAgent = table.Column<string>(type: "nvarchar(512)", maxLength: 512, nullable: true),
                    Source = table.Column<string>(type: "nvarchar(128)", maxLength: 128, nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "datetime2", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "datetime2", nullable: true),
                    CreatedBy = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    UpdatedBy = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    IsDeleted = table.Column<bool>(type: "bit", nullable: false, defaultValue: false)
                },
                constraints: table => table.PrimaryKey("PK_ConsentLogs", x => x.Id));

            migrationBuilder.CreateTable(
                name: "DataSubjectRequests",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    SubjectEmail = table.Column<string>(type: "nvarchar(256)", maxLength: 256, nullable: false),
                    SubjectName = table.Column<string>(type: "nvarchar(256)", maxLength: 256, nullable: true),
                    RequestType = table.Column<int>(type: "int", nullable: false),
                    Status = table.Column<int>(type: "int", nullable: false, defaultValue: 0),
                    Details = table.Column<string>(type: "nvarchar(2000)", maxLength: 2000, nullable: true),
                    DueDate = table.Column<DateTime>(type: "datetime2", nullable: false),
                    CompletedAt = table.Column<DateTime>(type: "datetime2", nullable: true),
                    HandledBy = table.Column<string>(type: "nvarchar(450)", maxLength: 450, nullable: true),
                    HandlerNotes = table.Column<string>(type: "nvarchar(2000)", maxLength: 2000, nullable: true),
                    ExportPath = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "datetime2", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "datetime2", nullable: true),
                    CreatedBy = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    UpdatedBy = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    IsDeleted = table.Column<bool>(type: "bit", nullable: false, defaultValue: false)
                },
                constraints: table => table.PrimaryKey("PK_DataSubjectRequests", x => x.Id));

            migrationBuilder.CreateIndex(
                name: "IX_ConsentLogs_UserId",
                table: "ConsentLogs",
                column: "UserId");

            migrationBuilder.CreateIndex(
                name: "IX_DataSubjectRequests_SubjectEmail",
                table: "DataSubjectRequests",
                column: "SubjectEmail");

            migrationBuilder.CreateIndex(
                name: "IX_DataSubjectRequests_Status",
                table: "DataSubjectRequests",
                column: "Status");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(name: "ConsentLogs");
            migrationBuilder.DropTable(name: "DataSubjectRequests");
        }
    }
}
