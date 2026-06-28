using AtoZClinical.Core.Entities;
using AtoZClinical.Infrastructure.Services;

namespace AtoZClinical.Tests;

public class DoctorScopeQueryTests
{
    [Fact]
    public void Matches_ReturnsTrue_WhenUnrestricted()
    {
        var scope = DoctorScopeFilter.Unrestricted;
        Assert.True(DoctorScopeQuery.Matches(scope, Guid.NewGuid(), "Dr A"));
    }

    [Fact]
    public void Matches_ReturnsTrue_WhenDoctorRecordIdMatches()
    {
        var id = Guid.NewGuid();
        var scope = new DoctorScopeFilter { IsRestricted = true, DoctorRecordId = id };
        Assert.True(DoctorScopeQuery.Matches(scope, id, "Dr Mohammed"));
        Assert.False(DoctorScopeQuery.Matches(scope, Guid.NewGuid(), "Dr Mohammed"));
    }

    [Fact]
    public void UserReceivesNotification_LabTechnician_GetsLaboratoryTarget()
    {
        var user = new Infrastructure.Identity.ApplicationUser
        {
            ClinicRole = Core.Enums.ClinicUserRole.LabTechnician,
            ClinicId = Guid.NewGuid()
        };

        Assert.True(ClinicalNotificationRoles.UserReceivesNotification(user, ClinicalNotificationRoles.Laboratory));
        Assert.True(ClinicalNotificationRoles.UserReceivesNotification(user, ClinicalNotificationRoles.Lab));
        Assert.False(ClinicalNotificationRoles.UserReceivesNotification(user, ClinicalNotificationRoles.Radiology));
    }
}
