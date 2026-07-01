using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace AtoZClinical.Infrastructure.Data.Migrations;

public partial class AddInpatientForms : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.CreateTable(
            name: "DoctorSurgeries",
            columns: table => new
            {
                Id = table.Column<Guid>(type: "uuid", nullable: false),
                ClinicId = table.Column<Guid>(type: "uuid", nullable: false),
                SurgeryNo = table.Column<int>(type: "integer", nullable: false),
                RecordDate = table.Column<DateTime>(type: "timestamp without time zone", nullable: false),
                SurgeryDate = table.Column<DateTime>(type: "timestamp without time zone", nullable: true),
                SurgeryTime = table.Column<TimeSpan>(type: "interval", nullable: true),
                PatientRecordId = table.Column<Guid>(type: "uuid", nullable: true),
                PatientName = table.Column<string>(type: "text", nullable: true),
                PatientBarcode = table.Column<string>(type: "text", nullable: true),
                Age = table.Column<int>(type: "integer", nullable: true),
                City = table.Column<string>(type: "text", nullable: true),
                NationalId = table.Column<string>(type: "text", nullable: true),
                Phone = table.Column<string>(type: "text", nullable: true),
                MotherName = table.Column<string>(type: "text", nullable: true),
                DoctorRecordId = table.Column<Guid>(type: "uuid", nullable: true),
                DoctorName = table.Column<string>(type: "text", nullable: true),
                Specialty = table.Column<string>(type: "text", nullable: true),
                TypeOfSurgery = table.Column<string>(type: "text", nullable: true),
                Classify = table.Column<string>(type: "text", nullable: true),
                SurgeryName = table.Column<string>(type: "text", nullable: true),
                InitialAmount = table.Column<decimal>(type: "numeric", nullable: false),
                CreatedAt = table.Column<DateTime>(type: "timestamp without time zone", nullable: false),
                UpdatedAt = table.Column<DateTime>(type: "timestamp without time zone", nullable: false)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_DoctorSurgeries", x => x.Id);
                table.ForeignKey(
                    name: "FK_DoctorSurgeries_Clinics_ClinicId",
                    column: x => x.ClinicId,
                    principalTable: "Clinics",
                    principalColumn: "Id",
                    onDelete: ReferentialAction.Cascade);
            });

        migrationBuilder.CreateTable(
            name: "RoomBookings",
            columns: table => new
            {
                Id = table.Column<Guid>(type: "uuid", nullable: false),
                ClinicId = table.Column<Guid>(type: "uuid", nullable: false),
                BookingNo = table.Column<int>(type: "integer", nullable: false),
                DateBook = table.Column<DateTime>(type: "timestamp without time zone", nullable: false),
                PatientRecordId = table.Column<Guid>(type: "uuid", nullable: true),
                DoctorSurgeryId = table.Column<Guid>(type: "uuid", nullable: true),
                PatientName = table.Column<string>(type: "text", nullable: true),
                PatientBarcode = table.Column<string>(type: "text", nullable: true),
                Age = table.Column<int>(type: "integer", nullable: true),
                City = table.Column<string>(type: "text", nullable: true),
                NationalId = table.Column<string>(type: "text", nullable: true),
                Phone = table.Column<string>(type: "text", nullable: true),
                MotherName = table.Column<string>(type: "text", nullable: true),
                DoctorName = table.Column<string>(type: "text", nullable: true),
                Specialty = table.Column<string>(type: "text", nullable: true),
                TypeOfSurgery = table.Column<string>(type: "text", nullable: true),
                Classify = table.Column<string>(type: "text", nullable: true),
                SurgeryName = table.Column<string>(type: "text", nullable: true),
                RoomNumber = table.Column<int>(type: "integer", nullable: true),
                EnterDate = table.Column<DateTime>(type: "timestamp without time zone", nullable: true),
                ExitDate = table.Column<DateTime>(type: "timestamp without time zone", nullable: true),
                EnterTime = table.Column<TimeSpan>(type: "interval", nullable: true),
                ExitTime = table.Column<TimeSpan>(type: "interval", nullable: true),
                Days = table.Column<int>(type: "integer", nullable: true),
                Note = table.Column<string>(type: "text", nullable: true),
                CreatedAt = table.Column<DateTime>(type: "timestamp without time zone", nullable: false),
                UpdatedAt = table.Column<DateTime>(type: "timestamp without time zone", nullable: false)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_RoomBookings", x => x.Id);
                table.ForeignKey(
                    name: "FK_RoomBookings_Clinics_ClinicId",
                    column: x => x.ClinicId,
                    principalTable: "Clinics",
                    principalColumn: "Id",
                    onDelete: ReferentialAction.Cascade);
            });

        migrationBuilder.CreateTable(
            name: "WardRooms",
            columns: table => new
            {
                Id = table.Column<Guid>(type: "uuid", nullable: false),
                ClinicId = table.Column<Guid>(type: "uuid", nullable: false),
                RoomNo = table.Column<int>(type: "integer", nullable: false),
                Status = table.Column<string>(type: "text", nullable: false),
                UpdatedAt = table.Column<DateTime>(type: "timestamp without time zone", nullable: false)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_WardRooms", x => x.Id);
                table.ForeignKey(
                    name: "FK_WardRooms_Clinics_ClinicId",
                    column: x => x.ClinicId,
                    principalTable: "Clinics",
                    principalColumn: "Id",
                    onDelete: ReferentialAction.Cascade);
            });

        migrationBuilder.CreateIndex(
            name: "IX_DoctorSurgeries_ClinicId_SurgeryNo",
            table: "DoctorSurgeries",
            columns: new[] { "ClinicId", "SurgeryNo" },
            unique: true);

        migrationBuilder.CreateIndex(
            name: "IX_RoomBookings_ClinicId_BookingNo",
            table: "RoomBookings",
            columns: new[] { "ClinicId", "BookingNo" },
            unique: true);

        migrationBuilder.CreateIndex(
            name: "IX_WardRooms_ClinicId_RoomNo",
            table: "WardRooms",
            columns: new[] { "ClinicId", "RoomNo" },
            unique: true);
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropTable(name: "WardRooms");
        migrationBuilder.DropTable(name: "RoomBookings");
        migrationBuilder.DropTable(name: "DoctorSurgeries");
    }
}
