using System.ComponentModel.DataAnnotations;
using AtoZClinical.Core.Entities;
using AtoZClinical.Infrastructure.Services;
using AtoZClinical.Web.Services;
using Microsoft.AspNetCore.Mvc;

namespace AtoZClinical.Web.Pages.Expenses;

public class IndexModel : ClinicFormPageModel
{
    private readonly ExpenseVoucherService _service;
    private readonly ChartAccountService _chartService;
    private const int DefaultLineCount = 4;

    public IndexModel(ClinicContextService clinicContext, ExpenseVoucherService service, ChartAccountService chartService)
        : base(clinicContext)
    {
        _service = service;
        _chartService = chartService;
    }

    [BindProperty]
    public ExpenseVoucherInput Input { get; set; } = new();

    [BindProperty]
    public List<ExpenseLineInput> Lines { get; set; } = [];

    public List<ExpenseVoucher> Records { get; private set; } = [];
    public List<ChartAccount> ExpenseAccounts { get; private set; } = [];
    public List<ChartAccount> PaymentAccounts { get; private set; } = [];

    public decimal LineTotal => Lines.Sum(l => l.Amount);

    public async Task<IActionResult> OnGetAsync()
    {
        var clinicId = await RequireClinicIdAsync();
        if (clinicId is null) return Forbid();
        await LoadFormDataAsync(clinicId.Value);
        if (ShouldOpenExistingRecordOnGet())
            await LoadRecord(clinicId.Value, RecordId!.Value);
        else
            await PrepareNew(clinicId.Value);
        ViewData["ShowAddLines"] = true;
        SetFormViewData("Expenses", null, null, Input.UpdatedAt, Input.ExpenseNo.ToString());
        return Page();
    }

    protected override Task<IActionResult> ExecuteSaveAsync() => SaveCoreAsync();
    public Task<IActionResult> OnPostNewAsync() => NewCoreAsync();
    public Task<IActionResult> OnPostClearAsync() => NewCoreAsync();
    public Task<IActionResult> OnPostDeleteAsync() => DeleteCoreAsync();
    public Task<IActionResult> OnPostBackAsync() => NavigateCoreAsync(-1);
    public Task<IActionResult> OnPostNextAsync() => NavigateCoreAsync(1);

    private async Task LoadFormDataAsync(Guid clinicId)
    {
        Records = await _service.ListAsync(clinicId);
        if (!string.IsNullOrWhiteSpace(Search))
            Records = Records.Where(r =>
                r.ExpenseNo.ToString().Contains(Search) ||
                (r.Description?.Contains(Search, StringComparison.OrdinalIgnoreCase) == true) ||
                (r.PayeeName?.Contains(Search, StringComparison.OrdinalIgnoreCase) == true) ||
                r.PaymentMethod.Contains(Search, StringComparison.OrdinalIgnoreCase)).ToList();

        var allAccounts = await _chartService.ListAsync(clinicId);
        ExpenseAccounts = allAccounts
            .Where(a => string.Equals(a.CategoryType, "Expense", StringComparison.OrdinalIgnoreCase))
            .OrderBy(a => a.AccountNo)
            .ToList();
        PaymentAccounts = allAccounts
            .Where(IsPaymentCreditAccount)
            .OrderBy(a => a.AccountNo)
            .ToList();
    }

    private static bool IsPaymentCreditAccount(ChartAccount a)
    {
        if (string.Equals(a.CategoryType, "Asset", StringComparison.OrdinalIgnoreCase))
            return string.Equals(a.Name, "Cash", StringComparison.OrdinalIgnoreCase) ||
                   string.Equals(a.DetailType, "Cash", StringComparison.OrdinalIgnoreCase) ||
                   string.Equals(a.Name, "Bank", StringComparison.OrdinalIgnoreCase) ||
                   string.Equals(a.DetailType, "Bank", StringComparison.OrdinalIgnoreCase);

        if (string.Equals(a.CategoryType, "Liability", StringComparison.OrdinalIgnoreCase))
            return string.Equals(a.Name, "Account Payable", StringComparison.OrdinalIgnoreCase) ||
                   string.Equals(a.DetailType, "Account Payable", StringComparison.OrdinalIgnoreCase);

        return false;
    }

    private async Task LoadRecord(Guid clinicId, Guid id)
    {
        var item = await _service.GetAsync(clinicId, id);
        if (item is null) return;
        RecordId = item.Id;
        Input = ExpenseVoucherInput.FromEntity(item);
        Lines = item.Lines.OrderBy(l => l.LineNo).Select(ExpenseLineInput.FromEntity).ToList();
        EnsureLineRows();
    }

    private async Task PrepareNew(Guid clinicId)
    {
        RecordId = null;
        var chartAccounts = await _chartService.ListAsync(clinicId);
        var defaultCredit = chartAccounts.FirstOrDefault(a =>
            string.Equals(a.Name, "Cash", StringComparison.OrdinalIgnoreCase))?.Name ?? "Cash";
        Input = new ExpenseVoucherInput
        {
            ExpenseNo = await _service.NextExpenseNoAsync(clinicId),
            ExpenseDate = DateTime.Today,
            PaymentMethod = ClinicLookup.ExpensePaymentMethods[0],
            CreditAccountName = defaultCredit
        };
        Lines = CreateEmptyLines();
    }

    private void EnsureLineRows()
    {
        while (Lines.Count < DefaultLineCount)
            Lines.Add(new ExpenseLineInput { LineNo = Lines.Count + 1 });
        for (var i = 0; i < Lines.Count; i++)
            Lines[i].LineNo = i + 1;
    }

    private List<ExpenseLineInput> CreateEmptyLines() =>
        Enumerable.Range(1, DefaultLineCount).Select(i => new ExpenseLineInput { LineNo = i }).ToList();

    private async Task<IActionResult> SaveCoreAsync()
    {
        var clinicId = await RequireClinicIdAsync();
        if (clinicId is null) return Forbid();

        ResolveRecordIdForSave();
        ValidateLines();

        if (!ModelState.IsValid)
        {
            await LoadFormDataAsync(clinicId.Value);
            ViewData["ShowAddLines"] = true;
            SetFormViewData("Expenses", null, null, Input.UpdatedAt, Input.ExpenseNo.ToString());
            return Page();
        }

        try
        {
            var wasNew = IsNewSave;
            var entity = Input.ToEntity(RecordIdForSave);
            var lineEntities = Lines
                .Where(l => l.Amount > 0 && !string.IsNullOrWhiteSpace(l.ChartAccountName))
                .Select(l => l.ToEntity())
                .ToList();
            var saved = await _service.SaveAsync(clinicId.Value, entity, lineEntities, UserName);
            return RedirectAfterSave(saved.Id, wasNew);
        }
        catch (InvalidOperationException ex)
        {
            ModelState.AddModelError(string.Empty, ex.Message);
            await LoadFormDataAsync(clinicId.Value);
            ViewData["ShowAddLines"] = true;
            SetFormViewData("Expenses", null, null, Input.UpdatedAt, Input.ExpenseNo.ToString());
            return Page();
        }
    }

    private void ValidateLines()
    {
        var valid = Lines.Where(l => l.Amount > 0 || !string.IsNullOrWhiteSpace(l.ChartAccountName)).ToList();
        foreach (var line in valid)
        {
            if (line.Amount <= 0)
                ModelState.AddModelError(string.Empty, "Each expense line with an account must have an amount greater than zero.");
            if (string.IsNullOrWhiteSpace(line.ChartAccountName))
                ModelState.AddModelError(string.Empty, "Select an expense account for each line with an amount.");
        }

        if (!valid.Any(l => l.Amount > 0 && !string.IsNullOrWhiteSpace(l.ChartAccountName)))
            ModelState.AddModelError(string.Empty, "Add at least one expense line.");
    }

    protected override async Task ReloadAfterSaveFailureAsync()
    {
        var clinicId = await RequireClinicIdAsync();
        if (clinicId is null) return;
        await LoadFormDataAsync(clinicId.Value);
        ViewData["ShowAddLines"] = true;
        SetFormViewData("Expenses", null, null, Input.UpdatedAt, Input.ExpenseNo.ToString());
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
                await LoadFormDataAsync(clinicId.Value);
                if (RecordId.HasValue) await LoadRecord(clinicId.Value, RecordId.Value);
            });
    }

    private async Task<IActionResult> NavigateCoreAsync(int delta)
    {
        var clinicId = await RequireClinicIdAsync();
        if (clinicId is null) return Forbid();
        await LoadFormDataAsync(clinicId.Value);
        if (Records.Count == 0) return RedirectToPage();
        var idx = RecordId.HasValue ? Records.FindIndex(r => r.Id == RecordId.Value) : 0;
        if (idx < 0) idx = 0;
        idx = Math.Clamp(idx + delta, 0, Records.Count - 1);
        return RedirectToRecord(Records[idx].Id);
    }

    public sealed class ExpenseVoucherInput
    {
        public int ExpenseNo { get; set; }

        [Required(ErrorMessage = "Date is required.")]
        public DateTime ExpenseDate { get; set; } = DateTime.Today;

        [Required(ErrorMessage = "Payment method is required.")]
        public string PaymentMethod { get; set; } = "Cash";

        [Display(Name = "Account Name")]
        public string? CreditAccountName { get; set; }

        [Display(Name = "Payee")]
        public string? PayeeName { get; set; }

        public string? Description { get; set; }
        public DateTime? UpdatedAt { get; set; }

        public static ExpenseVoucherInput FromEntity(ExpenseVoucher e) => new()
        {
            ExpenseNo = e.ExpenseNo,
            ExpenseDate = e.ExpenseDate,
            PaymentMethod = e.PaymentMethod,
            CreditAccountName = e.CreditAccountName,
            PayeeName = e.PayeeName,
            Description = e.Description,
            UpdatedAt = e.UpdatedAt
        };

        public ExpenseVoucher ToEntity(Guid? id) => new()
        {
            Id = id ?? Guid.Empty,
            ExpenseNo = ExpenseNo,
            ExpenseDate = ExpenseDate,
            PaymentMethod = PaymentMethod,
            CreditAccountName = CreditAccountName,
            PayeeName = PayeeName,
            Description = Description
        };
    }

    public sealed class ExpenseLineInput
    {
        public int LineNo { get; set; }

        [Display(Name = "Expense Account")]
        public string? ChartAccountName { get; set; }

        [Range(0.01, double.MaxValue, ErrorMessage = "Amount must be greater than zero.")]
        public decimal Amount { get; set; }

        public string? Description { get; set; }

        public static ExpenseLineInput FromEntity(ExpenseVoucherLine line) => new()
        {
            LineNo = line.LineNo,
            ChartAccountName = line.ChartAccountName,
            Amount = line.Amount,
            Description = line.Description
        };

        public ExpenseVoucherLine ToEntity() => new()
        {
            LineNo = LineNo,
            ChartAccountName = ChartAccountName?.Trim() ?? "",
            Amount = Amount,
            Description = Description
        };
    }
}
