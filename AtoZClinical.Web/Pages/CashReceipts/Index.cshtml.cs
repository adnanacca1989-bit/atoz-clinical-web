using AtoZClinical.Core.Entities;
using AtoZClinical.Infrastructure.Services;
using AtoZClinical.Web.Services;
using Microsoft.AspNetCore.Mvc;
using System.ComponentModel.DataAnnotations;

namespace AtoZClinical.Web.Pages.CashReceipts;

public class IndexModel : ClinicFormPageModel
{
    private readonly CashReceiptService _service;
    private readonly ChartAccountService _chartService;

    public IndexModel(ClinicContextService clinicContext, CashReceiptService service, ChartAccountService chartService) : base(clinicContext)
    {
        _service = service;
        _chartService = chartService;
    }

    [BindProperty]
    public CashReceiptInput Input { get; set; } = new();

    public List<CashReceipt> Records { get; private set; } = [];
    public List<ChartAccount> Accounts { get; private set; } = [];

    public async Task<IActionResult> OnGetAsync()
    {
        var clinicId = await RequireClinicIdAsync();
        if (clinicId is null) return Forbid();
        await LoadAsync(clinicId.Value);
        if (RecordId.HasValue)
            await LoadRecord(clinicId.Value, RecordId.Value);
        else if (NewRecord)
            await PrepareNew(clinicId.Value);
        else if (Records.Count > 0)
            await LoadRecord(clinicId.Value, Records[0].Id);
        else
            await PrepareNew(clinicId.Value);
        SetFormViewData("Cash Receipt", null, null, Input.UpdatedAt);
        ViewData["OpenPatientSelect"] = true;
        return Page();
    }

    protected override Task<IActionResult> ExecuteSaveAsync() => SaveCoreAsync();
    public Task<IActionResult> OnPostNewAsync() => NewCoreAsync();
    public Task<IActionResult> OnPostClearAsync() => NewCoreAsync();
    public Task<IActionResult> OnPostDeleteAsync() => DeleteCoreAsync();
    public Task<IActionResult> OnPostBackAsync() => NavigateCoreAsync(-1);
    public Task<IActionResult> OnPostNextAsync() => NavigateCoreAsync(1);

    private async Task LoadAsync(Guid clinicId)
    {
        Records = await _service.ListAsync(clinicId);
        Accounts = (await _chartService.ListAsync(clinicId))
            .OrderBy(a => a.CategoryType)
            .ThenBy(a => a.AccountNo)
            .ToList();
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
        if (Input.Amount > 0 && (string.IsNullOrWhiteSpace(Input.WrittenAmount) || Input.WrittenAmount.Equals("Zero", StringComparison.OrdinalIgnoreCase)))
            Input.WrittenAmount = AmountWords.Convert(Input.Amount);
    }

    private async Task PrepareNew(Guid clinicId)
    {
        RecordId = null;
        var next = await _service.NextReceiptNoAsync(clinicId);
        var accounts = await _chartService.ListAsync(clinicId);
        Input = new CashReceiptInput
        {
            ReceiptNo = next,
            ReceiptDate = DateTime.Today,
            PaymentMethod = ClinicLookup.PaymentMethods[0],
            BalanceStatus = "Due",
            WrittenAmount = "Zero",
            ChartAccountName = accounts.FirstOrDefault(a => a.Name.Contains("Cash", StringComparison.OrdinalIgnoreCase))?.Name
                ?? accounts.FirstOrDefault()?.Name
                ?? ClinicLookup.AccountNames[0]
        };
    }

    private async Task<IActionResult> SaveCoreAsync()
    {
        var clinicId = await RequireClinicIdAsync();
        if (clinicId is null) return Forbid();

        if (!ModelState.IsValid)
        {
            await LoadAsync(clinicId.Value);
            SetFormViewData("Cash Receipt", null, null, Input.UpdatedAt);
            ViewData["OpenPatientSelect"] = true;
            return Page();
        }

        if (string.IsNullOrWhiteSpace(Input.WrittenAmount) || Input.WrittenAmount.Equals("Zero", StringComparison.OrdinalIgnoreCase))
            Input.WrittenAmount = AmountWords.Convert(Input.Amount);

        var applied = Math.Min(Input.Amount, Math.Max(0, Input.BalanceDue));
        Input.EndingBalance = Math.Max(0, Input.BalanceDue - applied);
        Input.PatientCredit = Math.Max(0, Input.Amount - applied);
        if (Input.BalanceDue <= 0 && Input.Amount > 0)
            Input.BalanceStatus = Input.PatientCredit > 0 ? "Paid (Credit)" : "Paid";

        var isNew = string.Equals(SaveMode, "New", StringComparison.OrdinalIgnoreCase) || !RecordId.HasValue;
        var entity = Input.ToEntity(isNew ? null : RecordId);
        var saved = await _service.SaveAsync(clinicId.Value, entity);
        return RedirectAfterSave(saved.Id);
    }

    private Task<IActionResult> NewCoreAsync() =>
        Task.FromResult<IActionResult>(RedirectToNewForm());

    private async Task<IActionResult> DeleteCoreAsync()
    {
        var clinicId = await RequireClinicIdAsync();
        if (clinicId is null || !RecordId.HasValue) return RedirectToPage();
        return await SafeDeleteAsync(
            () => _service.DeleteAsync(clinicId.Value, RecordId.Value),
            async () =>
            {
                await LoadAsync(clinicId.Value);
                if (RecordId.HasValue) await LoadRecord(clinicId.Value, RecordId.Value);
            });
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

        [Required(ErrorMessage = "Date is required.")]
        [Display(Name = "Date")]
        public DateTime ReceiptDate { get; set; } = DateTime.Today;

        public string? PatientSearch { get; set; }
        public string? PatientName { get; set; }
        public string? PatientId { get; set; }
        public int? Age { get; set; }
        public string? Gender { get; set; }
        public string? Phone { get; set; }
        public string? City { get; set; }
        public string? Specialty { get; set; }
        public DateTime? AppointmentDate { get; set; }
        public TimeSpan? AppointmentTime { get; set; }
        public string? DoctorName { get; set; }
        public decimal BalanceDue { get; set; }
        public string? BalanceStatus { get; set; }
        public decimal? EndingBalance { get; set; }
        public decimal PatientCredit { get; set; }

        [Required(ErrorMessage = "Amount is required.")]
        [Range(0.01, double.MaxValue, ErrorMessage = "Amount must be greater than zero.")]
        public decimal Amount { get; set; }

        public string WrittenAmount { get; set; } = "Zero";

        [Required(ErrorMessage = "Payment Method is required.")]
        public string PaymentMethod { get; set; } = "Cash";

        [Required(ErrorMessage = "Account Name is required.")]
        [Display(Name = "Account Name")]
        public string? ChartAccountName { get; set; }

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
            Age = c.Age,
            Gender = c.Gender,
            Phone = c.Phone,
            City = c.City,
            Specialty = c.Specialty,
            AppointmentDate = c.AppointmentDate,
            AppointmentTime = c.AppointmentTime,
            DoctorName = c.DoctorName,
            BalanceDue = c.BalanceDue,
            BalanceStatus = c.BalanceStatus,
            EndingBalance = Math.Max(0, (c.EndingBalance ?? 0)),
            PatientCredit = Math.Max(0, c.Amount - Math.Min(c.Amount, Math.Max(0, c.BalanceDue))),
            Amount = c.Amount,
            WrittenAmount = c.WrittenAmount,
            PaymentMethod = c.PaymentMethod,
            ChartAccountName = c.ChartAccountName,
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
            Age = Age,
            Gender = Gender,
            Phone = Phone,
            City = City,
            Specialty = Specialty,
            AppointmentDate = AppointmentDate,
            AppointmentTime = AppointmentTime,
            DoctorName = DoctorName,
            BalanceDue = BalanceDue,
            BalanceStatus = BalanceStatus,
            EndingBalance = EndingBalance,
            PatientCredit = PatientCredit,
            Amount = Amount,
            WrittenAmount = WrittenAmount,
            PaymentMethod = PaymentMethod,
            ChartAccountName = ChartAccountName,
            ReferenceNo = ReferenceNo,
            Description = Description
        };
    }
}
