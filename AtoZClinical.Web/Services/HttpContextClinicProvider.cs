using AtoZClinical.Infrastructure.Data;

namespace AtoZClinical.Web.Services;

public sealed class HttpContextClinicProvider : ICurrentClinicProvider
{
    public const string TenantClinicIdKey = "TenantClinicId";
    public const string BypassTenantFilterKey = "BypassTenantFilter";
    public const string SubdomainClinicIdKey = "SubdomainClinicId";

    private readonly IHttpContextAccessor _http;

    public HttpContextClinicProvider(IHttpContextAccessor http) => _http = http;

    public Guid? ClinicId =>
        _http.HttpContext?.Items[TenantClinicIdKey] as Guid?;

    public bool BypassTenantFilter =>
        _http.HttpContext is null
        || (_http.HttpContext.Items[BypassTenantFilterKey] as bool?) == true;
}
