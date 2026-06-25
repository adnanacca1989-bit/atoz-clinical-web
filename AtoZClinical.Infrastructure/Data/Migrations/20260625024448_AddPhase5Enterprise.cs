using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace AtoZClinical.Infrastructure.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddPhase5Enterprise : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "DedicatedConnectionName",
                table: "Clinics",
                type: "character varying(128)",
                maxLength: 128,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "Subdomain",
                table: "Clinics",
                type: "character varying(63)",
                maxLength: 63,
                nullable: true);

            migrationBuilder.AddColumn<bool>(
                name: "PatientPortalEnabled",
                table: "ClinicConfigurations",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.CreateTable(
                name: "ClinicApiKeys",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    ClinicId = table.Column<Guid>(type: "uuid", nullable: false),
                    Name = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    KeyPrefix = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                    KeyHash = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                    IsActive = table.Column<bool>(type: "boolean", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "timestamp without time zone", nullable: false),
                    LastUsedAt = table.Column<DateTime>(type: "timestamp without time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ClinicApiKeys", x => x.Id);
                    table.ForeignKey(
                        name: "FK_ClinicApiKeys_Clinics_ClinicId",
                        column: x => x.ClinicId,
                        principalTable: "Clinics",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "WebhookSubscriptions",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    ClinicId = table.Column<Guid>(type: "uuid", nullable: false),
                    TargetUrl = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: false),
                    Secret = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                    Events = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: false),
                    IsActive = table.Column<bool>(type: "boolean", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "timestamp without time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_WebhookSubscriptions", x => x.Id);
                    table.ForeignKey(
                        name: "FK_WebhookSubscriptions_Clinics_ClinicId",
                        column: x => x.ClinicId,
                        principalTable: "Clinics",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_Clinics_Subdomain",
                table: "Clinics",
                column: "Subdomain",
                unique: true,
                filter: "\"Subdomain\" IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "IX_ClinicApiKeys_ClinicId_Name",
                table: "ClinicApiKeys",
                columns: new[] { "ClinicId", "Name" });

            migrationBuilder.CreateIndex(
                name: "IX_ClinicApiKeys_KeyPrefix",
                table: "ClinicApiKeys",
                column: "KeyPrefix");

            migrationBuilder.CreateIndex(
                name: "IX_WebhookSubscriptions_ClinicId_TargetUrl",
                table: "WebhookSubscriptions",
                columns: new[] { "ClinicId", "TargetUrl" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "ClinicApiKeys");

            migrationBuilder.DropTable(
                name: "WebhookSubscriptions");

            migrationBuilder.DropIndex(
                name: "IX_Clinics_Subdomain",
                table: "Clinics");

            migrationBuilder.DropColumn(
                name: "DedicatedConnectionName",
                table: "Clinics");

            migrationBuilder.DropColumn(
                name: "Subdomain",
                table: "Clinics");

            migrationBuilder.DropColumn(
                name: "PatientPortalEnabled",
                table: "ClinicConfigurations");
        }
    }
}
