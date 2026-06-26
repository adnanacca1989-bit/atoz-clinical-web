using AtoZClinical.Core.Entities;
using AtoZClinical.Infrastructure.Services;
using AtoZClinical.Web.Services;
using Microsoft.AspNetCore.Mvc;
using System.ComponentModel.DataAnnotations;

namespace AtoZClinical.Web.Pages.Invoices;

public class IndexModel : ClinicFormPageModel
{
    private readonly InvoiceService _invoiceService;
    private readonly ServiceIncomeService _serviceIncomeService;
    private readonly PatientInvoiceService _patientInvoices;
    private const int DefaultLineCount = 8;

    public IndexModel(ClinicContextService clinicContext, InvoiceService invoiceService, ServiceIncomeService serviceIncomeService, PatientInvoiceService patientInvoices) : base(clinicContext)
    {
        _invoiceService = invoiceService;
        _serviceIncomeService = serviceIncomeService;
        _patientInvoices = patientInvoices;
    }

    [BindProperty]
    public InvoiceInput Input { get; set; } = new();

    [BindProperty]
    public List<InvoiceLineInput> Lines { get; set; } = [];

    public List<Invoice> Records { get; private set; } = [];
    public List<ServiceIncome> Services { get; private set; } = [];

    public decimal LineSubTotal => Lines.Sum(l => l.LineTotal);
    public decimal NetAmount => LineSubTotal - Input.Discount;
    public decimal Balance => NetAmount - Input.AmountPaid;

    public string ClinicName { get; private set; } = "Clinic";
    public string? ClinicAddress { get; private set; }
    public string? ClinicPhone { get; private set; }

    public async Task<IActionResult> OnGetAsync()
    {
        var clinicId = await RequireClinicIdAsync();
        if (clinicId is null) return Forbid();
        await LoadClinicBrandingAsync();
        await LoadAsync(clinicId.Value);
        if (RecordId.HasValue)
        {
            var loaded = await LoadRecord(clinicId.Value, RecordId.Value);
            if (!loaded)
            {
                TempData["Error"] = "Invoice record was not found. It may have been removed.";
                return RedirectToPage();
            }
        }
        else if (NewRecord)
            await PrepareNew(clinicId.Value);
        else if (Records.Count > 0)
            await LoadRecord(clinicId.Value, Records[0].Id);
        else
            await PrepareNew(clinicId.Value);
        SetFormViewData("Invoice / Billing", null, null, Input.UpdatedAt);
        ViewData["OpenPatientSelect"] = true;
        ViewData["ShowAddLines"] = true;
        return Page();
    }

    protected override Task<IActionResult> ExecuteSaveAsync() => SaveCoreAsync();
    public Task<IActionResult> OnPostNewAsync() => NewCoreAsync();
    public Task<IActionResult> OnPostClearAsync() => NewCoreAsync();
    public Task<IActionResult> OnPostDeleteAsync() => DeleteCoreAsync();
    public Task<IActionResult> OnPostBackAsync() => NavigateCoreAsync(-1);
    public Task<IActionResult> OnPostNextAsync() => NavigateCoreAsync(1);

    private async Task LoadClinicBrandingAsync()
    {
        var access = await ClinicContext.GetClinicAccessAsync();
        var clinic = access.Clinic;
        if (clinic is null) return;
        ClinicName = clinic.Name;
        ClinicAddress = string.Join(", ", new[] { clinic.Address, clinic.City, clinic.Country }.Where(s => !string.IsNullOrWhiteSpace(s)));
        ClinicPhone = clinic.Phone;
    }

    private async Task LoadAsync(Guid clinicId)
    {
        Records = await _invoiceService.ListAsync(clinicId);
        Services = await _serviceIncomeService.ListAsync(clinicId);
        if (!string.IsNullOrWhiteSpace(Search))
            Records = Records.Where(r =>
                (r.PatientName?.Contains(Search, StringComparison.OrdinalIgnoreCase) == true) ||
                (r.DoctorName?.Contains(Search, StringComparison.OrdinalIgnoreCase) == true) ||
                r.InvoiceNo.ToString().Contains(Search)).ToList();
    }

    private async Task<bool> LoadRecord(Guid clinicId, Guid id)
    {
        var item = await _invoiceService.GetAsync(clinicId, id);
        if (item is null) return false;
        await _patientInvoices.RecalculateInvoicePaymentsAsync(clinicId, item.PatientName, item.PatientId, item.DoctorName);
        item = await _invoiceService.GetAsync(clinicId, id);
        if (item is null) return false;
        RecordId = item.Id;
        Input = InvoiceInput.FromEntity(item);
        Lines = item.Lines.OrderBy(l => l.LineNo).Select(InvoiceLineInput.FromEntity).ToList();
        EnsureLineRows();
        return true;
    }

    private async Task PrepareNew(Guid clinicId)
    {
        RecordId = null;
        var next = await _invoiceService.NextInvoiceNoAsync(clinicId);
        Input = new InvoiceInput
        {
            InvoiceNo = next,
            InvoiceDate = DateTime.Today,
            PaymentMethod = ClinicLookup.PaymentMethods[0],
            PaymentStatus = "Unpaid"
        };
        Lines = CreateEmptyLines();
    }

    private async Task<IActionResult> SaveCoreAsync()
    {
        var clinicId = await RequireClinicIdAsync();
        if (clinicId is null) return Forbid();

        if (!ModelState.IsValid)
        {
            await LoadClinicBrandingAsync();
            await LoadAsync(clinicId.Value);
            EnsureLineRows();
            SetFormViewData("Invoice / Billing", null, null, Input.UpdatedAt);
            ViewData["OpenPatientSelect"] = true;
            ViewData["ShowAddLines"] = true;
            return Page();
        }

        var isNew = string.Equals(SaveMode, "New", StringComparison.OrdinalIgnoreCase) || !RecordId.HasValue;
        var entity = Input.ToEntity(isNew ? null : RecordId);
        var lines = Lines
            .Where(l => !string.IsNullOrWhiteSpace(l.ServiceName))
            .Select(l => l.ToEntity())
            .ToList();
        var saved = await _invoiceService.SaveAsync(clinicId.Value, entity, lines, UserName);
        await _patientInvoices.RecalculateInvoicePaymentsAsync(clinicId.Value, saved.PatientName, saved.PatientId, saved.DoctorName);
        return RedirectAfterSave(saved.Id);
    }

    private Task<IActionResult> NewCoreAsync() =>
        Task.FromResult<IActionResult>(RedirectToNewForm());

    private async Task<IActionResult> DeleteCoreAsync()
    {
        var clinicId = await RequireClinicIdAsync();
        if (clinicId is null || !RecordId.HasValue) return RedirectToPage();
        await _invoiceService.DeleteAsync(clinicId.Value, RecordId.Value, UserName);
        return RedirectToPage();
    }

    private async Task<IActionResult> NavigateCoreAsync(int delta)
    {
        var clinicId = await RequireClinicIdAsync();
        if (clinicId is null) return Forbid();
        await LoadAsync(clinicId.Value);
        if (Records.Count == 0) return RedirectToPage();
        var idx = RecordId.HasValue ? Records.FindIndex(r => r.Id == RecordId.Value) : 0;
        if (idx < 0) idx = 0;
        idx = Math.Clamp(idx + delta, 0, Records.Count - 1);
        return RedirectToRecord(Records[idx].Id);
    }

    private void EnsureLineRows()
    {
        while (Lines.Count < DefaultLineCount)
            Lines.Add(new InvoiceLineInput { LineNo = Lines.Count + 1 });
    }

    private static List<InvoiceLineInput> CreateEmptyLines() =>
        Enumerable.Range(1, DefaultLineCount).Select(i => new InvoiceLineInput { LineNo = i }).ToList();

    public sealed class InvoiceInput
    {
        public int InvoiceNo { get; set; }

        [Required(ErrorMessage = "Invoice Date is required.")]
        [Display(Name = "Invoice Date")]
        public DateTime InvoiceDate { get; set; } = DateTime.Today;

        [Required(ErrorMessage = "Patient Name is required.")]
        [Display(Name = "Patient Name")]
        public string? PatientName { get; set; }

        public string? PatientId { get; set; }

        [Required(ErrorMessage = "Age is required.")]
        [Range(0, 150, ErrorMessage = "Age is required.")]
        public int? Age { get; set; }

        public string? Gender { get; set; }

        [Required(ErrorMessage = "Phone is required.")]
        public string? Phone { get; set; }

        [Required(ErrorMessage = "City is required.")]
        public string? City { get; set; }

        [Required(ErrorMessage = "Doctor Name is required.")]
        [Display(Name = "Doctor Name")]
        public string? DoctorName { get; set; }

        [Required(ErrorMessage = "Specialty is required.")]
        public string? Specialty { get; set; }
        public decimal Discount { get; set; }
        public decimal AmountPaid { get; set; }
        public string PaymentMethod { get; set; } = "Cash";
        public string? PaymentStatus { get; set; }
        public string? Notes { get; set; }
        public DateTime? UpdatedAt { get; set; }

        public static InvoiceInput FromEntity(Invoice i) => new()
        {
            InvoiceNo = i.InvoiceNo,
            InvoiceDate = i.InvoiceDate,
            PatientName = i.PatientName,
            PatientId = i.PatientId,
            Age = i.Age,
            Gender = i.Gender,
            Phone = i.Phone,
            City = i.City,
            DoctorName = i.DoctorName,
            Specialty = i.Specialty,
            Discount = i.Discount,
            AmountPaid = i.AmountPaid,
            PaymentMethod = i.PaymentMethod,
            PaymentStatus = i.PaymentStatus,
            Notes = i.Notes,
            UpdatedAt = i.UpdatedAt
        };

        public Invoice ToEntity(Guid? id) => new()
        {
            Id = id ?? Guid.Empty,
            InvoiceNo = InvoiceNo,
            InvoiceDate = InvoiceDate,
            PatientName = PatientName,
            PatientId = PatientId,
            Age = Age,
            Gender = Gender,
            Phone = Phone,
            City = City,
            DoctorName = DoctorName,
            Specialty = Specialty,
            Discount = Discount,
            AmountPaid = AmountPaid,
            PaymentMethod = PaymentMethod,
            PaymentStatus = PaymentStatus,
            Notes = Notes
        };
    }

    public sealed class InvoiceLineInput
    {
        public int LineNo { get; set; }
        public int? ServiceNo { get; set; }
        public string? ServiceName { get; set; }
        public int Qty { get; set; } = 1;
        public decimal UnitFee { get; set; }

        public decimal LineTotal => Qty * UnitFee;

        public static InvoiceLineInput FromEntity(InvoiceLine l) => new()
        {
            LineNo = l.LineNo,
            ServiceNo = l.ServiceNo,
            ServiceName = l.ServiceName,
            Qty = l.Qty,
            UnitFee = l.UnitFee
        };

        public InvoiceLine ToEntity() => new()
        {
            LineNo = LineNo,
            ServiceNo = ServiceNo,
            ServiceName = ServiceName,
            AccountName = null,
            Qty = Qty,
            UnitFee = UnitFee,
            LineTotal = Qty * UnitFee
        };
    }
}
