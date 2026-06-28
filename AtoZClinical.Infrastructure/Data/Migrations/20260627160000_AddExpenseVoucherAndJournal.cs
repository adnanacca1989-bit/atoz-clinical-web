using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace AtoZClinical.Infrastructure.Data.Migrations;

public partial class AddExpenseVoucherAndJournal : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.CreateTable(
            name: "JournalEntries",
            columns: table => new
            {
                Id = table.Column<Guid>(type: "uuid", nullable: false),
                ClinicId = table.Column<Guid>(type: "uuid", nullable: false),
                EntryNo = table.Column<int>(type: "integer", nullable: false),
                EntryDate = table.Column<DateTime>(type: "timestamp without time zone", nullable: false),
                SourceType = table.Column<string>(type: "text", nullable: false),
                SourceId = table.Column<Guid>(type: "uuid", nullable: true),
                Description = table.Column<string>(type: "text", nullable: true),
                CreatedAt = table.Column<DateTime>(type: "timestamp without time zone", nullable: false),
                UpdatedAt = table.Column<DateTime>(type: "timestamp without time zone", nullable: false)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_JournalEntries", x => x.Id);
                table.ForeignKey(
                    name: "FK_JournalEntries_Clinics_ClinicId",
                    column: x => x.ClinicId,
                    principalTable: "Clinics",
                    principalColumn: "Id",
                    onDelete: ReferentialAction.Cascade);
            });

        migrationBuilder.CreateTable(
            name: "ExpenseVouchers",
            columns: table => new
            {
                Id = table.Column<Guid>(type: "uuid", nullable: false),
                ClinicId = table.Column<Guid>(type: "uuid", nullable: false),
                ExpenseNo = table.Column<int>(type: "integer", nullable: false),
                ExpenseDate = table.Column<DateTime>(type: "timestamp without time zone", nullable: false),
                PaymentMethod = table.Column<string>(type: "text", nullable: false),
                Description = table.Column<string>(type: "text", nullable: true),
                TotalAmount = table.Column<decimal>(type: "numeric", nullable: false),
                JournalEntryId = table.Column<Guid>(type: "uuid", nullable: true),
                CreatedAt = table.Column<DateTime>(type: "timestamp without time zone", nullable: false),
                UpdatedAt = table.Column<DateTime>(type: "timestamp without time zone", nullable: false)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_ExpenseVouchers", x => x.Id);
                table.ForeignKey(
                    name: "FK_ExpenseVouchers_Clinics_ClinicId",
                    column: x => x.ClinicId,
                    principalTable: "Clinics",
                    principalColumn: "Id",
                    onDelete: ReferentialAction.Cascade);
                table.ForeignKey(
                    name: "FK_ExpenseVouchers_JournalEntries_JournalEntryId",
                    column: x => x.JournalEntryId,
                    principalTable: "JournalEntries",
                    principalColumn: "Id",
                    onDelete: ReferentialAction.SetNull);
            });

        migrationBuilder.CreateTable(
            name: "ExpenseVoucherLines",
            columns: table => new
            {
                Id = table.Column<Guid>(type: "uuid", nullable: false),
                ExpenseVoucherId = table.Column<Guid>(type: "uuid", nullable: false),
                LineNo = table.Column<int>(type: "integer", nullable: false),
                ChartAccountName = table.Column<string>(type: "text", nullable: false),
                Amount = table.Column<decimal>(type: "numeric", nullable: false),
                Description = table.Column<string>(type: "text", nullable: true)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_ExpenseVoucherLines", x => x.Id);
                table.ForeignKey(
                    name: "FK_ExpenseVoucherLines_ExpenseVouchers_ExpenseVoucherId",
                    column: x => x.ExpenseVoucherId,
                    principalTable: "ExpenseVouchers",
                    principalColumn: "Id",
                    onDelete: ReferentialAction.Cascade);
            });

        migrationBuilder.CreateTable(
            name: "JournalEntryLines",
            columns: table => new
            {
                Id = table.Column<Guid>(type: "uuid", nullable: false),
                JournalEntryId = table.Column<Guid>(type: "uuid", nullable: false),
                LineNo = table.Column<int>(type: "integer", nullable: false),
                AccountName = table.Column<string>(type: "text", nullable: false),
                AccountCategory = table.Column<string>(type: "text", nullable: true),
                Debit = table.Column<decimal>(type: "numeric", nullable: false),
                Credit = table.Column<decimal>(type: "numeric", nullable: false),
                Description = table.Column<string>(type: "text", nullable: true)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_JournalEntryLines", x => x.Id);
                table.ForeignKey(
                    name: "FK_JournalEntryLines_JournalEntries_JournalEntryId",
                    column: x => x.JournalEntryId,
                    principalTable: "JournalEntries",
                    principalColumn: "Id",
                    onDelete: ReferentialAction.Cascade);
            });

        migrationBuilder.CreateIndex(name: "IX_JournalEntries_ClinicId_EntryNo", table: "JournalEntries", columns: new[] { "ClinicId", "EntryNo" }, unique: true);
        migrationBuilder.CreateIndex(name: "IX_JournalEntryLines_JournalEntryId", table: "JournalEntryLines", column: "JournalEntryId");
        migrationBuilder.CreateIndex(name: "IX_ExpenseVouchers_ClinicId_ExpenseNo", table: "ExpenseVouchers", columns: new[] { "ClinicId", "ExpenseNo" }, unique: true);
        migrationBuilder.CreateIndex(name: "IX_ExpenseVouchers_JournalEntryId", table: "ExpenseVouchers", column: "JournalEntryId");
        migrationBuilder.CreateIndex(name: "IX_ExpenseVoucherLines_ExpenseVoucherId", table: "ExpenseVoucherLines", column: "ExpenseVoucherId");
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropTable(name: "ExpenseVoucherLines");
        migrationBuilder.DropTable(name: "JournalEntryLines");
        migrationBuilder.DropTable(name: "ExpenseVouchers");
        migrationBuilder.DropTable(name: "JournalEntries");
    }
}
