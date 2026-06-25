using AtoZClinical.Core.Entities;
using AtoZClinical.Core.Enums;
using AtoZClinical.Infrastructure.Data;
using AtoZClinical.Infrastructure.Services;
using AtoZClinical.Web.Services;
using Microsoft.EntityFrameworkCore;

namespace AtoZClinical.Web.Api;

public static class ClinicalApiEndpoints
{
    public static void MapClinicalApi(this WebApplication app)
    {
        var api = app.MapGroup("/api/v1");

        api.MapGet("/patients", async (HttpContext ctx, ClinicalDbContext db) =>
        {
            if (!IsApiAuthorized(ctx, out var clinicId)) return Results.Unauthorized();

            var search = ctx.Request.Query["search"].FirstOrDefault();
            var query = db.Patients.AsNoTracking().Where(p => p.ClinicId == clinicId);
            if (!string.IsNullOrWhiteSpace(search))
            {
                var term = search.Trim();
                query = query.Where(p =>
                    p.PatientNo.Contains(term)
                    || p.FirstName.Contains(term)
                    || p.LastName.Contains(term)
                    || (p.Phone != null && p.Phone.Contains(term)));
            }

            var patients = await query
                .OrderByDescending(p => p.CreatedAt)
                .Take(100)
                .Select(p => new
                {
                    p.Id,
                    p.PatientNo,
                    p.FirstName,
                    p.LastName,
                    p.Phone,
                    p.Email,
                    p.DateOfBirth,
                    p.Status
                })
                .ToListAsync();

            return Results.Ok(patients);
        });

        api.MapGet("/patients/{id:guid}", async (Guid id, HttpContext ctx, ClinicalDbContext db) =>
        {
            if (!IsApiAuthorized(ctx, out var clinicId)) return Results.Unauthorized();

            var patient = await db.Patients.AsNoTracking()
                .Where(p => p.ClinicId == clinicId && p.Id == id)
                .Select(p => new
                {
                    p.Id,
                    p.PatientNo,
                    p.FirstName,
                    p.LastName,
                    p.Phone,
                    p.Email,
                    p.DateOfBirth,
                    p.Gender,
                    p.Address,
                    p.City,
                    p.Status
                })
                .FirstOrDefaultAsync();

            return patient is null ? Results.NotFound() : Results.Ok(patient);
        });

        api.MapPost("/patients", async (
            CreatePatientApiRequest body,
            HttpContext ctx,
            PatientService patients) =>
        {
            if (!IsApiAuthorized(ctx, out var clinicId)) return Results.Unauthorized();
            if (string.IsNullOrWhiteSpace(body.FirstName) || string.IsNullOrWhiteSpace(body.LastName))
                return Results.BadRequest(new { error = "FirstName and LastName are required." });

            var patient = new Patient
            {
                FirstName = body.FirstName.Trim(),
                LastName = body.LastName.Trim(),
                Phone = body.Phone?.Trim(),
                Email = body.Email?.Trim(),
                DateOfBirth = body.DateOfBirth,
                Gender = body.Gender?.Trim()
            };

            try
            {
                var saved = await patients.SaveAsync(clinicId, patient, "api");
                return Results.Created($"/api/v1/patients/{saved.Id}", new
                {
                    saved.Id,
                    saved.PatientNo,
                    saved.FirstName,
                    saved.LastName
                });
            }
            catch (InvalidOperationException ex)
            {
                return Results.BadRequest(new { error = ex.Message });
            }
        });

        api.MapGet("/doctors", async (HttpContext ctx, ClinicalDbContext db) =>
        {
            if (!IsApiAuthorized(ctx, out var clinicId)) return Results.Unauthorized();

            var doctors = await db.Doctors.AsNoTracking()
                .Where(d => d.ClinicId == clinicId)
                .OrderBy(d => d.Name)
                .Take(100)
                .Select(d => new
                {
                    d.Id,
                    d.DoctorNo,
                    d.Name,
                    d.Specialty,
                    d.Phone,
                    d.Email,
                    d.ConsultationFee,
                    d.Status
                })
                .ToListAsync();

            return Results.Ok(doctors);
        });

        api.MapGet("/appointments", async (HttpContext ctx, ClinicalDbContext db) =>
        {
            if (!IsApiAuthorized(ctx, out var clinicId)) return Results.Unauthorized();

            var from = DateTime.TryParse(ctx.Request.Query["from"], out var f) ? f.Date : DateTime.UtcNow.Date;
            var to = DateTime.TryParse(ctx.Request.Query["to"], out var t) ? t.Date : from.AddDays(30);

            var items = await db.Appointments.AsNoTracking()
                .Where(a => a.ClinicId == clinicId && a.AppointmentDate >= from && a.AppointmentDate <= to)
                .OrderBy(a => a.AppointmentDate)
                .ThenBy(a => a.StartTime)
                .Take(200)
                .Select(a => new
                {
                    a.Id,
                    a.PatientId,
                    a.AppointmentDate,
                    a.StartTime,
                    a.EndTime,
                    a.DoctorName,
                    a.Department,
                    a.Status,
                    a.Reason
                })
                .ToListAsync();

            return Results.Ok(items);
        });

        api.MapPost("/appointments", async (
            CreateAppointmentApiRequest body,
            HttpContext ctx,
            ClinicalDbContext db,
            AppointmentService appointments) =>
        {
            if (!IsApiAuthorized(ctx, out var clinicId)) return Results.Unauthorized();

            var patientExists = await db.Patients.AnyAsync(p => p.ClinicId == clinicId && p.Id == body.PatientId);
            if (!patientExists)
                return Results.BadRequest(new { error = "Patient not found." });

            if (body.AppointmentDate.Date < DateTime.UtcNow.Date)
                return Results.BadRequest(new { error = "AppointmentDate must be today or later." });

            var appointment = new Appointment
            {
                PatientId = body.PatientId,
                AppointmentDate = body.AppointmentDate.Date,
                StartTime = body.StartTime,
                DoctorName = body.DoctorName?.Trim(),
                Department = body.Department?.Trim(),
                Reason = body.Reason?.Trim(),
                Status = AppointmentStatus.Scheduled,
                Notes = "Created via API"
            };

            var saved = await appointments.SaveAsync(clinicId, appointment);
            return Results.Created($"/api/v1/appointments/{saved.Id}", new
            {
                saved.Id,
                saved.PatientId,
                saved.AppointmentDate,
                saved.StartTime,
                saved.Status
            });
        });

        api.MapGet("/invoices", async (HttpContext ctx, ClinicalDbContext db) =>
        {
            if (!IsApiAuthorized(ctx, out var clinicId)) return Results.Unauthorized();

            var from = DateTime.TryParse(ctx.Request.Query["from"], out var f) ? f.Date : DateTime.UtcNow.Date.AddMonths(-1);
            var to = DateTime.TryParse(ctx.Request.Query["to"], out var t) ? t.Date : DateTime.UtcNow.Date;

            var items = await db.Invoices.AsNoTracking()
                .Where(i => i.ClinicId == clinicId && i.InvoiceDate >= from && i.InvoiceDate <= to)
                .OrderByDescending(i => i.InvoiceDate)
                .Take(200)
                .Select(i => new
                {
                    i.Id,
                    i.InvoiceNo,
                    i.InvoiceDate,
                    i.PatientName,
                    i.TotalAmount,
                    i.AmountPaid,
                    i.BalanceDue,
                    i.PaymentStatus,
                    i.PaymentMethod
                })
                .ToListAsync();

            return Results.Ok(items);
        });

        api.MapGet("/invoices/{id:guid}", async (Guid id, HttpContext ctx, ClinicalDbContext db) =>
        {
            if (!IsApiAuthorized(ctx, out var clinicId)) return Results.Unauthorized();

            var invoice = await db.Invoices.AsNoTracking()
                .Where(i => i.ClinicId == clinicId && i.Id == id)
                .Select(i => new
                {
                    i.Id,
                    i.InvoiceNo,
                    i.InvoiceDate,
                    i.PatientName,
                    i.Phone,
                    i.DoctorName,
                    i.SubTotal,
                    i.Discount,
                    i.TaxAmount,
                    i.TotalAmount,
                    i.AmountPaid,
                    i.BalanceDue,
                    i.PaymentStatus,
                    i.PaymentMethod,
                    i.Notes
                })
                .FirstOrDefaultAsync();

            return invoice is null ? Results.NotFound() : Results.Ok(invoice);
        });

        api.MapGet("/lab-results", async (HttpContext ctx, ClinicalDbContext db) =>
        {
            if (!IsApiAuthorized(ctx, out var clinicId)) return Results.Unauthorized();

            var patientName = ctx.Request.Query["patient"].FirstOrDefault();
            var query = db.LabResults.AsNoTracking().Where(r => r.ClinicId == clinicId);
            if (!string.IsNullOrWhiteSpace(patientName))
            {
                var term = patientName.Trim();
                query = query.Where(r => r.PatientName != null && r.PatientName.Contains(term));
            }

            var items = await query
                .OrderByDescending(r => r.ResultDate)
                .Take(100)
                .Select(r => new
                {
                    r.Id,
                    r.ResultNo,
                    r.PatientName,
                    r.DoctorName,
                    r.ResultDate,
                    r.Notes
                })
                .ToListAsync();

            return Results.Ok(items);
        });
    }

    private static bool IsApiAuthorized(HttpContext ctx, out Guid clinicId)
    {
        clinicId = Guid.Empty;
        if (ctx.Items["ApiKeyAuthenticated"] is not true) return false;
        if (ctx.Items[HttpContextClinicProvider.TenantClinicIdKey] is not Guid id) return false;
        clinicId = id;
        return true;
    }
}
