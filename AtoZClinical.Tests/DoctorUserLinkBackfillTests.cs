using AtoZClinical.Core.Entities;
using AtoZClinical.Core.Enums;
using AtoZClinical.Infrastructure.Identity;
using AtoZClinical.Infrastructure.Services;
using AtoZClinical.Tests.Helpers;
using Microsoft.EntityFrameworkCore;

namespace AtoZClinical.Tests;

public class DoctorUserLinkBackfillTests
{
    [Fact]
    public async Task BackfillAsync_LinksDoctorUser_WhenFullNameMatchesDoctorName()
    {
        var clinicId = Guid.NewGuid();
        await using var db = await SqliteTestDatabase.CreateAsync(TestClinicProvider.ForClinic(clinicId));
        var doctorId = Guid.NewGuid();

        db.Db.Clinics.Add(new Clinic { Id = clinicId, ClinicCode = "TST", Name = "Test Clinic" });
        db.Db.Doctors.Add(new Doctor { Id = doctorId, ClinicId = clinicId, DoctorNo = 1, Name = "Dr Ali" });
        db.Db.Users.Add(new ApplicationUser
        {
            Id = Guid.NewGuid().ToString(),
            UserName = "ali",
            ClinicId = clinicId,
            ClinicRole = ClinicUserRole.Doctor,
            FullName = "Dr Ali",
            DoctorRecordId = null
        });
        await db.Db.SaveChangesAsync();

        var linked = await DoctorUserLinkBackfill.BackfillAsync(db.Db);

        Assert.Equal(1, linked);
        var user = await db.Db.Users.SingleAsync();
        Assert.Equal(doctorId, user.DoctorRecordId);
    }

    [Fact]
    public async Task BackfillAsync_Skips_WhenDoctorAlreadyLinkedToAnotherUser()
    {
        var clinicId = Guid.NewGuid();
        await using var db = await SqliteTestDatabase.CreateAsync(TestClinicProvider.ForClinic(clinicId));
        var doctorId = Guid.NewGuid();

        db.Db.Clinics.Add(new Clinic { Id = clinicId, ClinicCode = "TST", Name = "Test Clinic" });
        db.Db.Doctors.Add(new Doctor { Id = doctorId, ClinicId = clinicId, DoctorNo = 1, Name = "Dr Ali" });
        db.Db.Users.Add(new ApplicationUser
        {
            Id = Guid.NewGuid().ToString(),
            UserName = "linked",
            ClinicId = clinicId,
            ClinicRole = ClinicUserRole.Doctor,
            FullName = "Dr Ali",
            DoctorRecordId = doctorId
        });
        db.Db.Users.Add(new ApplicationUser
        {
            Id = Guid.NewGuid().ToString(),
            UserName = "ali2",
            ClinicId = clinicId,
            ClinicRole = ClinicUserRole.Doctor,
            FullName = "Dr Ali",
            DoctorRecordId = null
        });
        await db.Db.SaveChangesAsync();

        var linked = await DoctorUserLinkBackfill.BackfillAsync(db.Db);

        Assert.Equal(0, linked);
        var unlinked = await db.Db.Users.SingleAsync(u => u.UserName == "ali2");
        Assert.Null(unlinked.DoctorRecordId);
    }
}
