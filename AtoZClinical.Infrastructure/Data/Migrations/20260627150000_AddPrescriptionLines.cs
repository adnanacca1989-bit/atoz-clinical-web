using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace AtoZClinical.Infrastructure.Data.Migrations;

public partial class AddPrescriptionLines : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.CreateTable(
            name: "PrescriptionLines",
            columns: table => new
            {
                Id = table.Column<Guid>(type: "uuid", nullable: false),
                PrescriptionId = table.Column<Guid>(type: "uuid", nullable: false),
                LineNo = table.Column<int>(type: "integer", nullable: false),
                PharmacyItemId = table.Column<Guid>(type: "uuid", nullable: true),
                MedicineName = table.Column<string>(type: "text", nullable: true),
                MedicationForm = table.Column<string>(type: "text", nullable: true),
                Dose = table.Column<string>(type: "text", nullable: true),
                Unit = table.Column<string>(type: "text", nullable: true),
                Frequency = table.Column<string>(type: "text", nullable: true),
                Duration = table.Column<string>(type: "text", nullable: true),
                Instruction = table.Column<string>(type: "text", nullable: true)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_PrescriptionLines", x => x.Id);
                table.ForeignKey(
                    name: "FK_PrescriptionLines_Prescriptions_PrescriptionId",
                    column: x => x.PrescriptionId,
                    principalTable: "Prescriptions",
                    principalColumn: "Id",
                    onDelete: ReferentialAction.Cascade);
            });

        migrationBuilder.CreateIndex(
            name: "IX_PrescriptionLines_PrescriptionId",
            table: "PrescriptionLines",
            column: "PrescriptionId");
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropTable(name: "PrescriptionLines");
    }
}
