using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace AtoZClinical.Infrastructure.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddSubscriptionBilling : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<DateTime>(
                name: "LastTrialReminderSentAt",
                table: "Clinics",
                type: "timestamp without time zone",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "OnboardingEmailsSent",
                table: "Clinics",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<string>(
                name: "StripeCustomerId",
                table: "Clinics",
                type: "character varying(128)",
                maxLength: 128,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "StripeSubscriptionId",
                table: "Clinics",
                type: "character varying(128)",
                maxLength: 128,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "SubscriptionStatus",
                table: "Clinics",
                type: "character varying(32)",
                maxLength: 32,
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<DateTime>(
                name: "TrialEndsAt",
                table: "Clinics",
                type: "timestamp without time zone",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_Clinics_StripeCustomerId",
                table: "Clinics",
                column: "StripeCustomerId");

            migrationBuilder.CreateIndex(
                name: "IX_Clinics_StripeSubscriptionId",
                table: "Clinics",
                column: "StripeSubscriptionId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_Clinics_StripeCustomerId",
                table: "Clinics");

            migrationBuilder.DropIndex(
                name: "IX_Clinics_StripeSubscriptionId",
                table: "Clinics");

            migrationBuilder.DropColumn(
                name: "LastTrialReminderSentAt",
                table: "Clinics");

            migrationBuilder.DropColumn(
                name: "OnboardingEmailsSent",
                table: "Clinics");

            migrationBuilder.DropColumn(
                name: "StripeCustomerId",
                table: "Clinics");

            migrationBuilder.DropColumn(
                name: "StripeSubscriptionId",
                table: "Clinics");

            migrationBuilder.DropColumn(
                name: "SubscriptionStatus",
                table: "Clinics");

            migrationBuilder.DropColumn(
                name: "TrialEndsAt",
                table: "Clinics");
        }
    }
}
