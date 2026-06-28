using AtoZClinical.Infrastructure;

namespace AtoZClinical.Tests;

public class RolePermissionDefaultsTests
{
    [Theory]
    [InlineData("Doctor")]
    [InlineData("Reception")]
    [InlineData("Lab")]
    [InlineData("Radiology")]
    [InlineData("Cashier")]
    public void DefaultRoles_IncludeDashboard(string roleName)
    {
        Assert.Contains(ClinicalFormKeys.Dashboard, RolePermissionDefaults.ByRole[roleName]);
    }

    [Theory]
    [InlineData("Doctor")]
    [InlineData("Lab")]
    [InlineData("Radiology")]
    [InlineData("Cashier")]
    public void SeedsForRole_IncludesEveryFormKey(string roleName)
    {
        var seeds = RolePermissionDefaults.SeedsForRole(roleName).ToList();
        Assert.Equal(ClinicalFormKeys.All.Length, seeds.Count);
        Assert.Contains(seeds, s => s.FormKey == ClinicalFormKeys.Dashboard && s.IsVisible);
    }

    [Fact]
    public void SeedsForRole_UnknownRole_HasNoVisibleForms()
    {
        var seeds = RolePermissionDefaults.SeedsForRole("UnknownRole").ToList();
        Assert.Equal(ClinicalFormKeys.All.Length, seeds.Count);
        Assert.DoesNotContain(seeds, s => s.IsVisible);
    }
}
