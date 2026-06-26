using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace AtoZClinical.Infrastructure.Data.Migrations;

public partial class AddServiceIncomeRequest : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.CreateTable(
            name: "ServiceIncomeRequests",
            columns: table => new
            {
                Id = table.Column<Guid>(type: "uuid", nullable: false),
                ClinicId = table.Column<Guid>(type: "uuid", nullable: false),
                RequestNo = table.Column<int>(type: "integer", nullable: false),
                RequestDate = table.Column<DateTime>(type: "timestamp without time zone", nullable: false),
                PatientName = table.Column<string>(type: "text", nullable: true),
                PatientBarcode = table.Column<string>(type: "text", nullable: true),
                Age = table.Column<int>(type: "integer", nullable: true),
                Gender = table.Column<string>(type: "text", nullable: true),
                Phone = table.Column<string>(type: "text", nullable: true),
                City = table.Column<string>(type: "text", nullable: true),
                DoctorName = table.Column<string>(type: "text", nullable: true),
                Specialty = table.Column<string>(type: "text", nullable: true),
                TotalAmount = table.Column<decimal>(type: "numeric", nullable: false),
                CreatedAt = table.Column<DateTime>(type: "timestamp without time zone", nullable: false),
                UpdatedAt = table.Column<DateTime>(type: "timestamp without time zone", nullable: false)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_ServiceIncomeRequests", x => x.Id);
                table.ForeignKey(
                    name: "FK_ServiceIncomeRequests_Clinics_ClinicId",
                    column: x => x.ClinicId,
                    principalTable: "Clinics",
                    principalColumn: "Id",
                    onDelete: ReferentialAction.Cascade);
            });

        migrationBuilder.CreateTable(
            name: "ServiceIncomeRequestLines",
            columns: table => new
            {
                Id = table.Column<Guid>(type: "uuid", nullable: false),
                ServiceIncomeRequestId = table.Column<Guid>(type: "uuid", nullable: false),
                LineNo = table.Column<int>(type: "integer", nullable: false),
                ServiceNo = table.Column<int>(type: "integer", nullable: true),
                ServiceName = table.Column<string>(type: "text", nullable: true),
                AccountName = table.Column<string>(type: "text", nullable: true),
                Qty = table.Column<int>(type: "integer", nullable: false),
                Fee = table.Column<decimal>(type: "numeric", nullable: false),
                Total = table.Column<decimal>(type: "numeric", nullable: false)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_ServiceIncomeRequestLines", x => x.Id);
                table.ForeignKey(
                    name: "FK_ServiceIncomeRequestLines_ServiceIncomeRequests_ServiceIncomeRequestId",
                    column: x => x.ServiceIncomeRequestId,
                    principalTable: "ServiceIncomeRequests",
                    principalColumn: "Id",
                    onDelete: ReferentialAction.Cascade);
            });

        migrationBuilder.CreateIndex(
            name: "IX_ServiceIncomeRequests_ClinicId_RequestNo",
            table: "ServiceIncomeRequests",
            columns: new[] { "ClinicId", "RequestNo" },
            unique: true);

        migrationBuilder.CreateIndex(
            name: "IX_ServiceIncomeRequestLines_ServiceIncomeRequestId",
            table: "ServiceIncomeRequestLines",
            column: "ServiceIncomeRequestId");
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropTable(name: "ServiceIncomeRequestLines");
        migrationBuilder.DropTable(name: "ServiceIncomeRequests");
    }
}
