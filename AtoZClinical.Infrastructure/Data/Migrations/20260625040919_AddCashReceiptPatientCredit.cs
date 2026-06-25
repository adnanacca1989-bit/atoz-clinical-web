using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace AtoZClinical.Infrastructure.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddCashReceiptPatientCredit : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<decimal>(
                name: "PatientCredit",
                table: "CashReceipts",
                type: "numeric",
                nullable: false,
                defaultValue: 0m);

            migrationBuilder.Sql("""
                UPDATE "CashReceipts"
                SET "PatientCredit" = GREATEST(0::numeric, "Amount" - LEAST("Amount", "BalanceDue"))
                WHERE "BalanceDue" > 0 AND "Amount" > "BalanceDue";
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "PatientCredit",
                table: "CashReceipts");
        }
    }
}
