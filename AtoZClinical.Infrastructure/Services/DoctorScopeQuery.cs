using AtoZClinical.Core.Entities;
using Microsoft.EntityFrameworkCore;

namespace AtoZClinical.Infrastructure.Services;

public static class DoctorScopeQuery
{
    public static IQueryable<Patient> Apply(this IQueryable<Patient> query, DoctorScopeFilter scope) =>
        ApplyDoctor(query, scope, p => p.DoctorRecordId, p => p.DoctorName);

    public static IQueryable<LabRequest> Apply(this IQueryable<LabRequest> query, DoctorScopeFilter scope) =>
        ApplyDoctor(query, scope, r => r.DoctorRecordId, r => r.DoctorName);

    public static IQueryable<LabResult> Apply(this IQueryable<LabResult> query, DoctorScopeFilter scope) =>
        ApplyDoctor(query, scope, r => r.DoctorRecordId, r => r.DoctorName);

    public static IQueryable<RadiologyRequest> Apply(this IQueryable<RadiologyRequest> query, DoctorScopeFilter scope) =>
        ApplyDoctor(query, scope, r => r.DoctorRecordId, r => r.DoctorName);

    public static IQueryable<RadiologyResult> Apply(this IQueryable<RadiologyResult> query, DoctorScopeFilter scope) =>
        ApplyDoctor(query, scope, r => r.DoctorRecordId, r => r.DoctorName);

    public static IQueryable<PharmacyRequest> Apply(this IQueryable<PharmacyRequest> query, DoctorScopeFilter scope) =>
        ApplyDoctor(query, scope, r => r.DoctorRecordId, r => r.DoctorName);

    public static IQueryable<PharmacyBill> Apply(this IQueryable<PharmacyBill> query, DoctorScopeFilter scope) =>
        ApplyDoctor(query, scope, b => b.DoctorRecordId, b => b.DoctorName);

    public static IQueryable<ServiceIncomeRequest> Apply(this IQueryable<ServiceIncomeRequest> query, DoctorScopeFilter scope) =>
        ApplyDoctor(query, scope, r => r.DoctorRecordId, r => r.DoctorName);

    public static IQueryable<Prescription> Apply(this IQueryable<Prescription> query, DoctorScopeFilter scope) =>
        ApplyDoctor(query, scope, p => p.DoctorRecordId, p => p.DoctorName);

    public static IQueryable<Invoice> Apply(this IQueryable<Invoice> query, DoctorScopeFilter scope) =>
        ApplyDoctor(query, scope, i => i.DoctorRecordId, i => i.DoctorName);

    public static IQueryable<CashReceipt> Apply(this IQueryable<CashReceipt> query, DoctorScopeFilter scope) =>
        ApplyDoctor(query, scope, r => r.DoctorRecordId, r => r.DoctorName);

    public static IQueryable<CashPayment> Apply(this IQueryable<CashPayment> query, DoctorScopeFilter scope) =>
        ApplyDoctor(query, scope, p => p.DoctorRecordId, p => p.DoctorName);

    public static IQueryable<Appointment> Apply(this IQueryable<Appointment> query, DoctorScopeFilter scope) =>
        ApplyDoctor(query, scope, a => a.DoctorRecordId, a => a.DoctorName);

    public static bool Matches(DoctorScopeFilter scope, Guid? doctorRecordId, string? doctorName)
    {
        if (!scope.IsRestricted) return true;
        if (scope.DoctorRecordId.HasValue && doctorRecordId.HasValue)
            return scope.DoctorRecordId.Value == doctorRecordId.Value;
        if (!string.IsNullOrWhiteSpace(scope.DoctorName) && !string.IsNullOrWhiteSpace(doctorName))
        {
            var a = scope.DoctorName.Trim();
            var b = doctorName.Trim();
            return string.Equals(a, b, StringComparison.OrdinalIgnoreCase) ||
                   b.Contains(a, StringComparison.OrdinalIgnoreCase) ||
                   a.Contains(b, StringComparison.OrdinalIgnoreCase);
        }
        return false;
    }

    private static IQueryable<T> ApplyDoctor<T>(
        IQueryable<T> query,
        DoctorScopeFilter scope,
        System.Linq.Expressions.Expression<Func<T, Guid?>> recordId,
        System.Linq.Expressions.Expression<Func<T, string?>> name)
    {
        if (!scope.IsRestricted) return query;

        if (scope.DoctorRecordId.HasValue)
        {
            var id = scope.DoctorRecordId.Value;
            return query.Where(BuildRecordEquals(recordId, id));
        }

        if (!string.IsNullOrWhiteSpace(scope.DoctorName))
        {
            var doctorName = scope.DoctorName.Trim();
            return query.Where(BuildNameEquals(name, doctorName));
        }

        return query.Where(_ => false);
    }

    private static System.Linq.Expressions.Expression<Func<T, bool>> BuildRecordEquals<T>(
        System.Linq.Expressions.Expression<Func<T, Guid?>> selector,
        Guid id)
    {
        var param = selector.Parameters[0];
        var body = System.Linq.Expressions.Expression.Equal(
            selector.Body,
            System.Linq.Expressions.Expression.Constant((Guid?)id, typeof(Guid?)));
        return System.Linq.Expressions.Expression.Lambda<Func<T, bool>>(body, param);
    }

    private static System.Linq.Expressions.Expression<Func<T, bool>> BuildNameEquals<T>(
        System.Linq.Expressions.Expression<Func<T, string?>> selector,
        string doctorName)
    {
        var param = selector.Parameters[0];
        var notNull = System.Linq.Expressions.Expression.NotEqual(
            selector.Body,
            System.Linq.Expressions.Expression.Constant(null, typeof(string)));
        var equals = System.Linq.Expressions.Expression.Equal(
            selector.Body,
            System.Linq.Expressions.Expression.Constant(doctorName, typeof(string)));
        var body = System.Linq.Expressions.Expression.AndAlso(notNull, equals);
        return System.Linq.Expressions.Expression.Lambda<Func<T, bool>>(body, param);
    }
}
