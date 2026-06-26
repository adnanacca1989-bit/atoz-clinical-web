using AtoZClinical.Core.Entities;
using AtoZClinical.Infrastructure.Services;
using AtoZClinical.Web.Services;
using Microsoft.AspNetCore.Mvc;

namespace AtoZClinical.Web.Pages.ServiceIncomes;

public class IndexModel : ClinicFormPageModel
{
    private readonly ServiceIncomeService _service;
    private readonly ChartAccountService _accounts;

    public IndexModel(ClinicContextService clinicContext, ServiceIncomeService service, ChartAccountService accounts) : base(clinicContext)
    {
        _service = service;
        _accounts = accounts;
    }

    [BindProperty]
    public ServiceIncomeInput Input { get; set; } = new();

    public List<ServiceIncome> Records { get; private set; } = [];
    public List<ChartAccount> IncomeAccounts { get; private set; } = [];

    public async Task<IActionResult> OnGetAsync()
    {
        var clinicId = await RequireClinicIdAsync();
        if (clinicId is null) return Forbid();
        await LoadAsync(clinicId.Value);
        if (ShouldLoadExistingRecord())
            await LoadRecord(clinicId.Value, RecordId!.Value);
        else
            await PrepareNew(clinicId.Value);
        SetFormViewData("Service Income", null, null, Input.UpdatedAt);
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
        await LoadIncomeAccountsAsync(clinicId);
        if (!string.IsNullOrWhiteSpace(Search))
            Records = Records.Where(r =>
                r.Name.Contains(Search, StringComparison.OrdinalIgnoreCase) ||
                r.AccountName.Contains(Search, StringComparison.OrdinalIgnoreCase)).ToList();
    }

    private async Task LoadRecord(Guid clinicId, Guid id)
    {
        var item = await _service.GetAsync(clinicId, id);
        if (item is null) return;
        RecordId = item.Id;
        Input = ServiceIncomeInput.FromEntity(item);
    }

    private async Task PrepareNew(Guid clinicId)
    {
        RecordId = null;
        var all = await _service.ListAsync(clinicId);
        var next = (all.Count > 0 ? all.Max(s => s.ServiceNo) : 0) + 1;
        await LoadIncomeAccountsAsync(clinicId);
        Input = new ServiceIncomeInput
        {
            ServiceNo = next,
            AccountName = IncomeAccounts.FirstOrDefault()?.Name ?? ClinicLookup.GetAccountNamesForCategory("Income").FirstOrDefault() ?? "Clinical Income"
        };
    }

    private async Task LoadIncomeAccountsAsync(Guid clinicId)
    {
        IncomeAccounts = (await _accounts.ListAsync(clinicId))
            .Where(a => a.CategoryType.Equals("Income", StringComparison.OrdinalIgnoreCase))
            .OrderBy(a => a.AccountNo)
            .ToList();
    }

    private async Task<IActionResult> SaveCoreAsync()
    {
        var clinicId = await RequireClinicIdAsync();
        if (clinicId is null) return Forbid();
        ResolveRecordIdForSave();
        var entity = Input.ToEntity(RecordIdForSave);
        var saved = await _service.SaveAsync(clinicId.Value, entity);
        return RedirectAfterSave(saved.Id);
    }

    private Task<IActionResult> NewCoreAsync() => Task.FromResult(RedirectToNewForm());

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

    public sealed class ServiceIncomeInput
    {
        public int ServiceNo { get; set; }
        public string Name { get; set; } = string.Empty;
        public string AccountName { get; set; } = "Clinical Revenue";
        public decimal Fee { get; set; }
        public DateTime? UpdatedAt { get; set; }

        public static ServiceIncomeInput FromEntity(ServiceIncome s) => new()
        {
            ServiceNo = s.ServiceNo,
            Name = s.Name,
            AccountName = s.AccountName,
            Fee = s.Fee,
            UpdatedAt = s.UpdatedAt
        };

        public ServiceIncome ToEntity(Guid? id) => new()
        {
            Id = id ?? Guid.Empty,
            ServiceNo = ServiceNo,
            Name = Name.Trim(),
            AccountName = AccountName,
            Fee = Fee
        };
    }
}
