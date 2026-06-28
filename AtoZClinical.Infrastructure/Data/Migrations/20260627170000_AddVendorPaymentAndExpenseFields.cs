using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace AtoZClinical.Infrastructure.Data.Migrations;

public partial class AddVendorPaymentAndExpenseFields : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.AddColumn<Guid>(
            name: "VendorId",
            table: "CashPayments",
            type: "uuid",
            nullable: true);

        migrationBuilder.AddColumn<Guid>(
            name: "JournalEntryId",
            table: "CashPayments",
            type: "uuid",
            nullable: true);

        migrationBuilder.AddColumn<string>(
            name: "PayeeName",
            table: "ExpenseVouchers",
            type: "text",
            nullable: true);

        migrationBuilder.AddColumn<string>(
            name: "CreditAccountName",
            table: "ExpenseVouchers",
            type: "text",
            nullable: true);
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropColumn(name: "VendorId", table: "CashPayments");
        migrationBuilder.DropColumn(name: "JournalEntryId", table: "CashPayments");
        migrationBuilder.DropColumn(name: "PayeeName", table: "ExpenseVouchers");
        migrationBuilder.DropColumn(name: "CreditAccountName", table: "ExpenseVouchers");
    }
}
