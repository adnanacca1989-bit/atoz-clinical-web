using AtoZClinical.Core.Entities;
using AtoZClinical.Infrastructure.Services;
using AtoZClinical.Web.Services;
using Microsoft.AspNetCore.Mvc;

namespace AtoZClinical.Web.Pages.Invoices;

public class IndexModel : ClinicFormPageModel
{
    private readonly InvoiceService _invoiceService;
    private readonly ServiceIncomeService _serviceIncomeService;
    private const int DefaultLineCount = 8;

    public IndexModel(ClinicContextService clinicContext, InvoiceService invoiceService, ServiceIncomeService serviceIncomeService) : base(clinicContext)
    {
        _invoiceService = invoiceService;
        _serviceIncomeService = serviceIncomeService;
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

    public async Task<IActionResult> OnGetAsync()
    {
        var clinicId = await RequireClinicIdAsync();
        if (clinicId is null) return Forbid();
        await LoadAsync(clinicId.Value);
        if (RecordId.HasValue)
            await LoadRecord(clinicId.Value, RecordId.Value);
        else if (Records.Count > 0 && Input.InvoiceNo == 0)
            await LoadRecord(clinicId.Value, Records[0].Id);
        else
            await PrepareNew(clinicId.Value);
        SetFormViewData("Invoice / Billing", null, null, Input.UpdatedAt);
        ViewData["OpenPatientSelect"] = true;
        ViewData["ShowAddLines"] = true;
        return Page();
    }

    public Task<IActionResult> OnPostSaveAsync() => SaveCoreAsync();
    public Task<IActionResult> OnPostNewAsync() => NewCoreAsync();
    public Task<IActionResult> OnPostClearAsync() => NewCoreAsync();
    public Task<IActionResult> OnPostDeleteAsync() => DeleteCoreAsync();
    public Task<IActionResult> OnPostBackAsync() => NavigateCoreAsync(-1);
    public Task<IActionResult> OnPostNextAsync() => NavigateCoreAsync(1);

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

    private async Task LoadRecord(Guid clinicId, Guid id)
    {
        var item = await _invoiceService.GetAsync(clinicId, id);
        if (item is null) return;
        RecordId = item.Id;
        Input = InvoiceInput.FromEntity(item);
        Lines = item.Lines.OrderBy(l => l.LineNo).Select(InvoiceLineInput.FromEntity).ToList();
        EnsureLineRows();
    }

    private async Task PrepareNew(Guid clinicId)
    {
        RecordId = null;
        var all = await _invoiceService.ListAsync(clinicId);
        var next = (all.Count > 0 ? all.Max(i => i.InvoiceNo) : 0) + 1;
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
        var entity = Input.ToEntity(RecordId);
        var lines = Lines
            .Where(l => !string.IsNullOrWhiteSpace(l.ServiceName) || l.UnitFee > 0)
            .Select(l =>
            {
                if (string.IsNullOrWhiteSpace(l.ServiceName))
                    l.ServiceName = l.UnitFee > 0 ? $"Charge line {l.LineNo}" : l.ServiceName;
                return l.ToEntity();
            })
            .ToList();
        var saved = await _invoiceService.SaveAsync(clinicId.Value, entity, lines, UserName);
        return RedirectAfterSave(saved.Id);
    }

    private Task<IActionResult> NewCoreAsync()
    {
        RecordId = null;
        return Task.FromResult<IActionResult>(RedirectToPage());
    }

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
        public DateTime InvoiceDate { get; set; } = DateTime.Today;
        public string? PatientName { get; set; }
        public string? PatientId { get; set; }
        public int? Age { get; set; }
        public string? Gender { get; set; }
        public string? Phone { get; set; }
        public string? City { get; set; }
        public string? DoctorName { get; set; }
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
