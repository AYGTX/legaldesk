using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace SignFlow.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class SigningAccess : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_Recipients_SecureToken",
                table: "Recipients");

            migrationBuilder.DropColumn(
                name: "Latitude",
                table: "Signatures");

            migrationBuilder.DropColumn(
                name: "Longitude",
                table: "Signatures");

            migrationBuilder.DropColumn(
                name: "SignedLocation",
                table: "Recipients");

            migrationBuilder.DropColumn(
                name: "Location",
                table: "Signatures");

            migrationBuilder.AddColumn<string>(
                name: "VerifiedEmail",
                table: "Signatures",
                type: "nvarchar(max)",
                nullable: true);

            migrationBuilder.DropColumn(
                name: "Hash",
                table: "Signatures");

            migrationBuilder.AddColumn<string>(
                name: "VerificationMethod",
                table: "Signatures",
                type: "nvarchar(max)",
                nullable: true);

            migrationBuilder.DropColumn(
                name: "Certificate",
                table: "Signatures");

            migrationBuilder.AddColumn<string>(
                name: "ImageSha256",
                table: "Signatures",
                type: "nvarchar(max)",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "DocumentSha256",
                table: "Signatures",
                type: "nvarchar(max)",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "DocumentVersion",
                table: "Signatures",
                type: "int",
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "VerifiedAt",
                table: "Signatures",
                type: "datetime2",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "TokenHash",
                table: "Recipients",
                type: "nvarchar(64)",
                maxLength: 64,
                nullable: true);

            // Same hash as SecureTokens.Hash: lowercase hex SHA-256 of the token's UTF-8 (ASCII) bytes.
            migrationBuilder.Sql("UPDATE Recipients SET TokenHash = LOWER(CONVERT(varchar(64), HASHBYTES('SHA2_256', CONVERT(varchar(100), SecureToken)), 2)) WHERE SecureToken IS NOT NULL AND SecureToken <> '';");

            migrationBuilder.DropColumn(
                name: "SecureToken",
                table: "Recipients");

            migrationBuilder.AddColumn<string>(
                name: "VerificationMethod",
                table: "Recipients",
                type: "nvarchar(32)",
                maxLength: 32,
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "VerifiedAt",
                table: "Recipients",
                type: "datetime2",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "DateFormat",
                table: "Organisations",
                type: "nvarchar(32)",
                maxLength: 32,
                nullable: false,
                defaultValue: "dd/MM/yyyy");

            migrationBuilder.AddColumn<string>(
                name: "TimeZoneId",
                table: "Organisations",
                type: "nvarchar(64)",
                maxLength: 64,
                nullable: false,
                defaultValue: "Africa/Tunis");

            migrationBuilder.CreateTable(
                name: "SigningOtps",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    RecipientId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    CodeHash = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: false),
                    ExpiresAt = table.Column<DateTime>(type: "datetime2", nullable: false),
                    Attempts = table.Column<int>(type: "int", nullable: false),
                    UsedAt = table.Column<DateTime>(type: "datetime2", nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "datetime2", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "datetime2", nullable: true),
                    CreatedBy = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: true),
                    UpdatedBy = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: true),
                    IsDeleted = table.Column<bool>(type: "bit", nullable: false),
                    OrganisationId = table.Column<Guid>(type: "uniqueidentifier", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_SigningOtps", x => x.Id);
                    table.ForeignKey(
                        name: "FK_SigningOtps_Organisations_OrganisationId",
                        column: x => x.OrganisationId,
                        principalTable: "Organisations",
                        principalColumn: "Id");
                    table.ForeignKey(
                        name: "FK_SigningOtps_Recipients_RecipientId",
                        column: x => x.RecipientId,
                        principalTable: "Recipients",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "SigningSessions",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    RecipientId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    SecretHash = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: false),
                    Method = table.Column<string>(type: "nvarchar(32)", maxLength: 32, nullable: false),
                    VerifiedEmail = table.Column<string>(type: "nvarchar(256)", maxLength: 256, nullable: false),
                    VerifiedAt = table.Column<DateTime>(type: "datetime2", nullable: false),
                    ExpiresAt = table.Column<DateTime>(type: "datetime2", nullable: false),
                    Ip = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "datetime2", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "datetime2", nullable: true),
                    CreatedBy = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: true),
                    UpdatedBy = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: true),
                    IsDeleted = table.Column<bool>(type: "bit", nullable: false),
                    OrganisationId = table.Column<Guid>(type: "uniqueidentifier", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_SigningSessions", x => x.Id);
                    table.ForeignKey(
                        name: "FK_SigningSessions_Organisations_OrganisationId",
                        column: x => x.OrganisationId,
                        principalTable: "Organisations",
                        principalColumn: "Id");
                    table.ForeignKey(
                        name: "FK_SigningSessions_Recipients_RecipientId",
                        column: x => x.RecipientId,
                        principalTable: "Recipients",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_Recipients_TokenHash",
                table: "Recipients",
                column: "TokenHash",
                unique: true,
                filter: "[TokenHash] IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "IX_SigningOtps_OrganisationId",
                table: "SigningOtps",
                column: "OrganisationId");

            migrationBuilder.CreateIndex(
                name: "IX_SigningOtps_RecipientId_CreatedAt",
                table: "SigningOtps",
                columns: new[] { "RecipientId", "CreatedAt" });

            migrationBuilder.CreateIndex(
                name: "IX_SigningSessions_OrganisationId",
                table: "SigningSessions",
                column: "OrganisationId");

            migrationBuilder.CreateIndex(
                name: "IX_SigningSessions_RecipientId",
                table: "SigningSessions",
                column: "RecipientId");

            migrationBuilder.CreateIndex(
                name: "IX_SigningSessions_SecretHash",
                table: "SigningSessions",
                column: "SecretHash",
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "SigningOtps");

            migrationBuilder.DropTable(
                name: "SigningSessions");

            migrationBuilder.DropIndex(
                name: "IX_Recipients_TokenHash",
                table: "Recipients");

            migrationBuilder.DropColumn(
                name: "DocumentSha256",
                table: "Signatures");

            migrationBuilder.DropColumn(
                name: "DocumentVersion",
                table: "Signatures");

            migrationBuilder.DropColumn(
                name: "VerifiedAt",
                table: "Signatures");

            migrationBuilder.DropColumn(
                name: "TokenHash",
                table: "Recipients");

            migrationBuilder.DropColumn(
                name: "VerificationMethod",
                table: "Recipients");

            migrationBuilder.DropColumn(
                name: "VerifiedAt",
                table: "Recipients");

            migrationBuilder.DropColumn(
                name: "DateFormat",
                table: "Organisations");

            migrationBuilder.DropColumn(
                name: "TimeZoneId",
                table: "Organisations");

            migrationBuilder.DropColumn(
                name: "VerifiedEmail",
                table: "Signatures");

            migrationBuilder.AddColumn<string>(
                name: "Location",
                table: "Signatures",
                type: "nvarchar(max)",
                nullable: true);

            migrationBuilder.DropColumn(
                name: "VerificationMethod",
                table: "Signatures");

            migrationBuilder.AddColumn<string>(
                name: "Hash",
                table: "Signatures",
                type: "nvarchar(max)",
                nullable: true);

            migrationBuilder.DropColumn(
                name: "ImageSha256",
                table: "Signatures");

            migrationBuilder.AddColumn<string>(
                name: "Certificate",
                table: "Signatures",
                type: "nvarchar(max)",
                nullable: true);

            migrationBuilder.AddColumn<double>(
                name: "Latitude",
                table: "Signatures",
                type: "float",
                nullable: true);

            migrationBuilder.AddColumn<double>(
                name: "Longitude",
                table: "Signatures",
                type: "float",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "SecureToken",
                table: "Recipients",
                type: "nvarchar(100)",
                maxLength: 100,
                nullable: false,
                defaultValue: "");

            migrationBuilder.Sql("UPDATE Recipients SET SecureToken = LOWER(REPLACE(CONVERT(varchar(36), NEWID()), '-', ''));");

            migrationBuilder.AddColumn<string>(
                name: "SignedLocation",
                table: "Recipients",
                type: "nvarchar(200)",
                maxLength: 200,
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_Recipients_SecureToken",
                table: "Recipients",
                column: "SecureToken",
                unique: true);
        }
    }
}
