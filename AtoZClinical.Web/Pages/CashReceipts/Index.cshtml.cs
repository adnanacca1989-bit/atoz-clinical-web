using AtoZClinical.Core.Entities;
using AtoZClinical.Infrastructure.Services;
using AtoZClinical.Web.Services;
using Microsoft.AspNetCore.Mvc;

namespace AtoZClinical.Web.Pages.CashReceipts;

public class IndexModel : ClinicFormPageModel
{
    private readonly CashReceiptService _service;

    public IndexModel(ClinicContextService clinicContext, CashReceiptService service) : base(clinicContext)
    {
        _service = service;
    }

    [BindProperty]
    public CashReceiptInput Input { get; set; } = new();

    public List<CashReceipt> Records { get; private set; } = [];

    public async Task<IActionResult> OnGetAsync()
    {
        var clinicId = await RequireClinicIdAsync();
        if (clinicId is null) return Forbid();
        await LoadAsync(clinicId.Value);
        if (RecordId.HasValue)
            await LoadRecord(clinicId.Value, RecordId.Value);
        else if (Records.Count > 0 && Input.ReceiptNo == 0)
            await LoadRecord(clinicId.Value, Records[0].Id);
        else
            await PrepareNew(clinicId.Value);
        SetFormViewData("Cash Receipt", null, null, Input.UpdatedAt);
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
        Records = await _service.ListAsync(clinicId);
        if (!string.IsNullOrWhiteSpace(Search))
            Records = Records.Where(r =>
                (r.PatientName?.Contains(Search, StringComparison.OrdinalIgnoreCase) == true) ||
                (r.PatientId?.Contains(Search, StringComparison.OrdinalIgnoreCase) == true) ||
                r.ReceiptNo.ToString().Contains(Search)).ToList();
    }

    private async Task LoadRecord(Guid clinicId, Guid id)
    {
        var item = await _service.GetAsync(clinicId, id);
        if (item is null) return;
        RecordId = item.Id;
        Input = CashReceiptInput.FromEntity(item);
    }

    private async Task PrepareNew(Guid clinicId)
    {
        RecordId = null;
        var all = await _service.ListAsync(clinicId);
        var next = (all.Count > 0 ? all.Max(c => c.ReceiptNo) : 0) + 1;
        Input = new CashReceiptInput
        {
            ReceiptNo = next,
            ReceiptDate = DateTime.Today,
            PaymentMethod = ClinicLookup.PaymentMethods[0],
            BalanceStatus = "Due",
            WrittenAmount = "Zero"
        };
    }

    private async Task<IActionResult> SaveCoreAsync()
    {
        var clinicId = await RequireClinicIdAsync();
        if (clinicId is null) return Forbid();
        var entity = Input.ToEntity(RecordId);
        var saved = await _service.SaveAsync(clinicId.Value, entity);
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
        await _service.DeleteAsync(clinicId.Value, RecordId.Value);
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

    public sealed class CashReceiptInput
    {
        public int ReceiptNo { get; set; }
        public DateTime ReceiptDate { get; set; } = DateTime.Today;
        public string? PatientSearch { get; set; }
        public string? PatientName { get; set; }
        public string? PatientId { get; set; }
        public DateTime? AppointmentDate { get; set; }
        public TimeSpan? AppointmentTime { get; set; }
        public string? DoctorName { get; set; }
        public decimal BalanceDue { get; set; }
        public string? BalanceStatus { get; set; }
        public decimal? EndingBalance { get; set; }
        public decimal Amount { get; set; }
        public string WrittenAmount { get; set; } = "Zero";
        public string PaymentMethod { get; set; } = "Cash";
        public string? ReferenceNo { get; set; }
        public string? Description { get; set; }
        public DateTime? UpdatedAt { get; set; }

        public static CashReceiptInput FromEntity(CashReceipt c) => new()
        {
            ReceiptNo = c.ReceiptNo,
            ReceiptDate = c.ReceiptDate,
            PatientSearch = c.PatientSearch,
            PatientName = c.PatientName,
            PatientId = c.PatientId,
            AppointmentDate = c.AppointmentDate,
            AppointmentTime = c.AppointmentTime,
            DoctorName = c.DoctorName,
            BalanceDue = c.BalanceDue,
            BalanceStatus = c.BalanceStatus,
            EndingBalance = c.EndingBalance,
            Amount = c.Amount,
            WrittenAmount = c.WrittenAmount,
            PaymentMethod = c.PaymentMethod,
            ReferenceNo = c.ReferenceNo,
            Description = c.Description,
            UpdatedAt = c.UpdatedAt
        };

        public CashReceipt ToEntity(Guid? id) => new()
        {
            Id = id ?? Guid.Empty,
            ReceiptNo = ReceiptNo,
            ReceiptDate = ReceiptDate,
            PatientSearch = PatientSearch,
            PatientName = PatientName,
            PatientId = PatientId,
            AppointmentDate = AppointmentDate,
            AppointmentTime = AppointmentTime,
            DoctorName = DoctorName,
            BalanceDue = BalanceDue,
            BalanceStatus = BalanceStatus,
            EndingBalance = EndingBalance,
            Amount = Amount,
            WrittenAmount = WrittenAmount,
            PaymentMethod = PaymentMethod,
            ReferenceNo = ReferenceNo,
            Description = Description
        };
    }
}
