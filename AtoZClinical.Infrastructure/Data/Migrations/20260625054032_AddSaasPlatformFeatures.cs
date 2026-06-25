using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace AtoZClinical.Infrastructure.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddSaasPlatformFeatures : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_Appointments_ClinicId",
                table: "Appointments");

            migrationBuilder.AddColumn<DateTime>(
                name: "SubscriptionExpiryDate",
                table: "Clinics",
                type: "timestamp without time zone",
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "SubscriptionStartDate",
                table: "Clinics",
                type: "timestamp without time zone",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "SubscriptionType",
                table: "Clinics",
                type: "character varying(64)",
                maxLength: 64,
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<string>(
                name: "TimeZoneId",
                table: "Clinics",
                type: "character varying(64)",
                maxLength: 64,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "LogoBase64",
                table: "ClinicConfigurations",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "Tagline",
                table: "ClinicConfigurations",
                type: "character varying(200)",
                maxLength: 200,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "TimeZoneId",
                table: "ClinicConfigurations",
                type: "character varying(64)",
                maxLength: 64,
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<string>(
                name: "Website",
                table: "ClinicConfigurations",
                type: "character varying(256)",
                maxLength: 256,
                nullable: true);

            migrationBuilder.CreateTable(
                name: "ClinicBackupHistories",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    ClinicId = table.Column<Guid>(type: "uuid", nullable: false),
                    Action = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    FileName = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: false),
                    FileSizeBytes = table.Column<long>(type: "bigint", nullable: false),
                    PerformedBy = table.Column<string>(type: "text", nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "timestamp without time zone", nullable: false),
                    Notes = table.Column<string>(type: "text", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ClinicBackupHistories", x => x.Id);
                    table.ForeignKey(
                        name: "FK_ClinicBackupHistories_Clinics_ClinicId",
                        column: x => x.ClinicId,
                        principalTable: "Clinics",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "SecurityAuditEntries",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    ClinicId = table.Column<Guid>(type: "uuid", nullable: true),
                    EventType = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    UserName = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: true),
                    Details = table.Column<string>(type: "text", nullable: true),
                    IpAddress = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "timestamp without time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_SecurityAuditEntries", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_Invoices_ClinicId_InvoiceDate",
                table: "Invoices",
                columns: new[] { "ClinicId", "InvoiceDate" });

            migrationBuilder.CreateIndex(
                name: "IX_Clinics_PlanName",
                table: "Clinics",
                column: "PlanName");

            migrationBuilder.CreateIndex(
                name: "IX_Clinics_Status",
                table: "Clinics",
                column: "Status");

            migrationBuilder.CreateIndex(
                name: "IX_Clinics_SubscriptionExpiryDate",
                table: "Clinics",
                column: "SubscriptionExpiryDate");

            migrationBuilder.CreateIndex(
                name: "IX_Appointments_ClinicId_AppointmentDate",
                table: "Appointments",
                columns: new[] { "ClinicId", "AppointmentDate" });

            migrationBuilder.CreateIndex(
                name: "IX_ClinicBackupHistories_ClinicId_CreatedAt",
                table: "ClinicBackupHistories",
                columns: new[] { "ClinicId", "CreatedAt" });

            migrationBuilder.CreateIndex(
                name: "IX_SecurityAuditEntries_ClinicId",
                table: "SecurityAuditEntries",
                column: "ClinicId");

            migrationBuilder.CreateIndex(
                name: "IX_SecurityAuditEntries_CreatedAt",
                table: "SecurityAuditEntries",
                column: "CreatedAt");

            migrationBuilder.CreateIndex(
                name: "IX_SecurityAuditEntries_EventType",
                table: "SecurityAuditEntries",
                column: "EventType");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "ClinicBackupHistories");

            migrationBuilder.DropTable(
                name: "SecurityAuditEntries");

            migrationBuilder.DropIndex(
                name: "IX_Invoices_ClinicId_InvoiceDate",
                table: "Invoices");

            migrationBuilder.DropIndex(
                name: "IX_Clinics_PlanName",
                table: "Clinics");

            migrationBuilder.DropIndex(
                name: "IX_Clinics_Status",
                table: "Clinics");

            migrationBuilder.DropIndex(
                name: "IX_Clinics_SubscriptionExpiryDate",
                table: "Clinics");

            migrationBuilder.DropIndex(
                name: "IX_Appointments_ClinicId_AppointmentDate",
                table: "Appointments");

            migrationBuilder.DropColumn(
                name: "SubscriptionExpiryDate",
                table: "Clinics");

            migrationBuilder.DropColumn(
                name: "SubscriptionStartDate",
                table: "Clinics");

            migrationBuilder.DropColumn(
                name: "SubscriptionType",
                table: "Clinics");

            migrationBuilder.DropColumn(
                name: "TimeZoneId",
                table: "Clinics");

            migrationBuilder.DropColumn(
                name: "LogoBase64",
                table: "ClinicConfigurations");

            migrationBuilder.DropColumn(
                name: "Tagline",
                table: "ClinicConfigurations");

            migrationBuilder.DropColumn(
                name: "TimeZoneId",
                table: "ClinicConfigurations");

            migrationBuilder.DropColumn(
                name: "Website",
                table: "ClinicConfigurations");

            migrationBuilder.CreateIndex(
                name: "IX_Appointments_ClinicId",
                table: "Appointments",
                column: "ClinicId");
        }
    }
}
