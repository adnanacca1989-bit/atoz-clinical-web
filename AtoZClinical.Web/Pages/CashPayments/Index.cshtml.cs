using AtoZClinical.Core.Entities;
using AtoZClinical.Infrastructure.Services;
using AtoZClinical.Web.Services;
using Microsoft.AspNetCore.Mvc;
using System.ComponentModel.DataAnnotations;

namespace AtoZClinical.Web.Pages.CashPayments;

public class IndexModel : ClinicFormPageModel
{
    private readonly CashPaymentService _service;
    private readonly ChartAccountService _chartService;
    private readonly ClinicLookupService _lookup;

    public IndexModel(
        ClinicContextService clinicContext,
        CashPaymentService service,
        ChartAccountService chartService,
        ClinicLookupService lookup) : base(clinicContext)
    {
        _service = service;
        _chartService = chartService;
        _lookup = lookup;
    }

    [BindProperty]
    public CashPaymentInput Input { get; set; } = new();

    public List<CashPayment> Records { get; private set; } = [];
    public List<ChartAccount> Accounts { get; private set; } = [];
    public List<ChartAccount> ExpenseAccounts { get; private set; } = [];
    public List<ClinicVendor> RegisteredVendors { get; private set; } = [];

    public bool IsVendorPayment => Input.VendorId.HasValue;

    public async Task<IActionResult> OnGetAsync()
    {
        var clinicId = await RequireClinicIdAsync();
        if (clinicId is null) return Forbid();
        await LoadAsync(clinicId.Value);
        if (ShouldOpenExistingRecordOnGet())
            await LoadRecord(clinicId.Value, RecordId!.Value);
        else
            await PrepareNew(clinicId.Value);
        SetFormViewData("Cash Payment", null, null, Input.UpdatedAt, Input.PaymentNo.ToString());
        ViewData["OpenPatientSelect"] = !IsVendorPayment;
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
        RegisteredVendors = await _lookup.ListVendorsAsync(clinicId, activeOnly: true);
        var allAccounts = await _chartService.ListAsync(clinicId);
        Accounts = allAccounts.OrderBy(a => a.CategoryType).ThenBy(a => a.AccountNo).ToList();
        ExpenseAccounts = allAccounts
            .Where(a => string.Equals(a.CategoryType, "Expense", StringComparison.OrdinalIgnoreCase))
            .OrderBy(a => a.AccountNo)
            .ToList();
        if (!string.IsNullOrWhiteSpace(Search))
            Records = Records.Where(r =>
                (r.PayeeName?.Contains(Search, StringComparison.OrdinalIgnoreCase) == true) ||
                (r.ChartAccountName?.Contains(Search, StringComparison.OrdinalIgnoreCase) == true) ||
                r.PaymentNo.ToString().Contains(Search)).ToList();
    }

    private async Task LoadRecord(Guid clinicId, Guid id)
    {
        var item = await _service.GetAsync(clinicId, id);
        if (item is null) return;
        RecordId = item.Id;
        Input = CashPaymentInput.FromEntity(item);
        if (Input.Amount > 0 && (string.IsNullOrWhiteSpace(Input.WrittenAmount) || Input.WrittenAmount.Equals("Zero", StringComparison.OrdinalIgnoreCase)))
            Input.WrittenAmount = AmountWords.Convert(Input.Amount);
    }

    private async Task PrepareNew(Guid clinicId)
    {
        RecordId = null;
        var next = await _service.NextPaymentNoAsync(clinicId);
        var accounts = await _chartService.ListAsync(clinicId);
        Input = new CashPaymentInput
        {
            PaymentNo = next,
            PaymentDate = DateTime.Today,
            PaymentMethod = ClinicLookup.PaymentMethods[0],
            WrittenAmount = "Zero",
            BalanceStatus = "Due",
            ChartAccountName = accounts.FirstOrDefault()?.Name ?? ClinicLookup.AccountNames[0]
        };
    }

    private async Task<IActionResult> SaveCoreAsync()
    {
        var clinicId = await RequireClinicIdAsync();
        if (clinicId is null) return Forbid();

        await LoadAsync(clinicId.Value);
        ResolveRecordIdForSave();

        if (Input.VendorId.HasValue)
        {
            var vendor = RegisteredVendors.FirstOrDefault(v => v.Id == Input.VendorId);
            if (vendor is null)
                ModelState.AddModelError(string.Empty, "Select a valid vendor from Settings → Define Vendor.");
            else
                Input.PayeeName = vendor.Name;

            var expenseAccount = ExpenseAccounts.FirstOrDefault(a =>
                string.Equals(a.Name, Input.ChartAccountName, StringComparison.OrdinalIgnoreCase));
            if (expenseAccount is null)
                ModelState.AddModelError(nameof(Input.ChartAccountName), "Vendor payments require an expense account.");
        }

        if (!ModelState.IsValid)
        {
            await LoadAsync(clinicId.Value);
            SetFormViewData("Cash Payment", null, null, Input.UpdatedAt, Input.PaymentNo.ToString());
            ViewData["OpenPatientSelect"] = !IsVendorPayment;
            return Page();
        }

        if (string.IsNullOrWhiteSpace(Input.WrittenAmount) || Input.WrittenAmount.Equals("Zero", StringComparison.OrdinalIgnoreCase))
            Input.WrittenAmount = AmountWords.Convert(Input.Amount);

        var wasNew = IsNewSave;
        var entity = Input.ToEntity(RecordIdForSave);
        var saved = await _service.SaveAsync(clinicId.Value, entity, UserName);
        return RedirectAfterSave(saved.Id, wasNew);
    }

    protected override async Task ReloadAfterSaveFailureAsync()
    {
        var clinicId = await RequireClinicIdAsync();
        if (clinicId is null) return;
        await LoadAsync(clinicId.Value);
        SetFormViewData("Cash Payment", null, null, Input.UpdatedAt, Input.PaymentNo.ToString());
        ViewData["OpenPatientSelect"] = !IsVendorPayment;
    }

    private Task<IActionResult> NewCoreAsync() =>
        Task.FromResult<IActionResult>(RedirectToNewForm());

    private async Task<IActionResult> DeleteCoreAsync()
    {
        var clinicId = await RequireClinicIdAsync();
        if (clinicId is null || !RecordId.HasValue) return RedirectToPage();
        return await SafeDeleteAsync(
            () => _service.DeleteAsync(clinicId.Value, RecordId.Value, UserName),
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

    public sealed class CashPaymentInput
    {
        public int PaymentNo { get; set; }

        [Required(ErrorMessage = "Date is required.")]
        [Display(Name = "Date")]
        public DateTime PaymentDate { get; set; } = DateTime.Today;

        public Guid? VendorId { get; set; }
        public string? PatientId { get; set; }
        public string? PayeeName { get; set; }
        public int? Age { get; set; }
        public string? Gender { get; set; }
        public string? Phone { get; set; }
        public string? City { get; set; }
        public string? DoctorName { get; set; }
        public string? Specialty { get; set; }
        public decimal BalanceDue { get; set; }
        public string? BalanceStatus { get; set; }

        [Required(ErrorMessage = "Amount is required.")]
        [Range(0.01, double.MaxValue, ErrorMessage = "Amount must be greater than zero.")]
        public decimal Amount { get; set; }

        [Required(ErrorMessage = "Payment Method is required.")]
        public string PaymentMethod { get; set; } = "Cash";

        public string? Description { get; set; }
        public decimal? EndingBalance { get; set; }

        [Required(ErrorMessage = "Account Name is required.")]
        [Display(Name = "Account Name")]
        public string? ChartAccountName { get; set; }
        public string WrittenAmount { get; set; } = "Zero";
        public string? ReferenceNo { get; set; }
        public DateTime? UpdatedAt { get; set; }

        public static CashPaymentInput FromEntity(CashPayment c) => new()
        {
            PaymentNo = c.PaymentNo,
            PaymentDate = c.PaymentDate,
            VendorId = c.VendorId,
            PatientId = c.PatientId,
            PayeeName = c.PayeeName,
            DoctorName = c.DoctorName,
            BalanceStatus = c.PayeeType,
            Amount = c.Amount,
            PaymentMethod = c.PaymentMethod,
            Description = c.Description,
            ChartAccountName = c.ChartAccountName,
            WrittenAmount = c.WrittenAmount,
            ReferenceNo = c.ReferenceNo,
            UpdatedAt = c.UpdatedAt
        };

        public CashPayment ToEntity(Guid? id) => new()
        {
            Id = id ?? Guid.Empty,
            PaymentNo = PaymentNo,
            PaymentDate = PaymentDate,
            VendorId = VendorId,
            PatientId = PatientId,
            PayeeName = PayeeName,
            DoctorName = DoctorName,
            PayeeType = VendorId.HasValue ? "Vendor" : BalanceStatus,
            ChartAccountName = ChartAccountName,
            Amount = Amount,
            WrittenAmount = WrittenAmount,
            PaymentMethod = PaymentMethod,
            ReferenceNo = ReferenceNo,
            Description = Description
        };
    }
}
