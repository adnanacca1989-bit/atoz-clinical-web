using AtoZClinical.Core.Entities;
using AtoZClinical.Infrastructure.Services;
using AtoZClinical.Web.Services;
using Microsoft.AspNetCore.Mvc;

namespace AtoZClinical.Web.Pages.Pharmacy;

public class RegistrationModel : ClinicFormPageModel
{
    private readonly PharmacyItemRegistrationService _service;
    private readonly ClinicLookupService _lookup;
    private readonly ChartAccountService _accounts;

    public RegistrationModel(ClinicContextService clinicContext, PharmacyItemRegistrationService service, ClinicLookupService lookup, ChartAccountService accounts) : base(clinicContext)
    {
        _service = service;
        _lookup = lookup;
        _accounts = accounts;
    }

    [BindProperty]
    public PharmacyItemInput Input { get; set; } = new();

    public List<PharmacyItem> Records { get; private set; } = [];
    public List<ClinicUom> UomOptions { get; private set; } = [];
    public List<ChartAccount> IncomeAccounts { get; private set; } = [];
    public List<ChartAccount> CostOfGoodsAccounts { get; private set; } = [];
    public List<ChartAccount> InventoryAccounts { get; private set; } = [];

    public async Task<IActionResult> OnGetAsync()
    {
        var clinicId = await RequireClinicIdAsync();
        if (clinicId is null) return Forbid();
        await LoadAsync(clinicId.Value);
        await LoadUomOptionsAsync(clinicId.Value);
        await LoadAccountsAsync(clinicId.Value);
        if (ShouldOpenExistingRecordOnGet())
            await LoadRecord(clinicId.Value, RecordId!.Value);
        else
            await PrepareNew(clinicId.Value);
        SetFormViewData("Pharmacy Registration Item Pharmacy", null, null, Input.UpdatedAt);
        return Page();
    }

    protected override Task<IActionResult> ExecuteSaveAsync() => SaveCoreAsync();
    public Task<IActionResult> OnPostNewAsync() => NewCoreAsync();
    public Task<IActionResult> OnPostClearAsync() => NewCoreAsync();
    public Task<IActionResult> OnPostDeleteAsync() => DeleteCoreAsync();
    public Task<IActionResult> OnPostBackAsync() => NavigateCoreAsync(-1);
    public Task<IActionResult> OnPostNextAsync() => NavigateCoreAsync(1);

    private async Task LoadAccountsAsync(Guid clinicId)
    {
        var all = await _accounts.ListAsync(clinicId);
        IncomeAccounts = all.Where(a => a.CategoryType.Equals("Income", StringComparison.OrdinalIgnoreCase)).ToList();
        CostOfGoodsAccounts = all.Where(a => a.CategoryType.Equals("Expense", StringComparison.OrdinalIgnoreCase)).ToList();
        InventoryAccounts = all.Where(a => a.CategoryType.Equals("Asset", StringComparison.OrdinalIgnoreCase)).ToList();
    }

    private async Task LoadUomOptionsAsync(Guid clinicId)
    {
        UomOptions = (await _lookup.ListUomsAsync(clinicId)).Where(u => u.IsActive).OrderBy(u => u.UomNo).ToList();
        if (UomOptions.Count == 0)
            UomOptions = [new ClinicUom { Code = "Pcs", Name = "Pieces" }, new ClinicUom { Code = "Box", Name = "Box" }];
    }

    private async Task LoadAsync(Guid clinicId)
    {
        Records = await _service.ListAsync(clinicId);
        if (!string.IsNullOrWhiteSpace(Search))
            Records = Records.Where(r =>
                r.Barcode.Contains(Search, StringComparison.OrdinalIgnoreCase) ||
                (r.MedicineCode?.Contains(Search, StringComparison.OrdinalIgnoreCase) == true) ||
                r.MedicineName.Contains(Search, StringComparison.OrdinalIgnoreCase)).ToList();
    }

    private async Task LoadRecord(Guid clinicId, Guid id)
    {
        var item = await _service.GetAsync(clinicId, id);
        if (item is null) return;
        RecordId = item.Id;
        Input = PharmacyItemInput.FromEntity(item);
    }

    private async Task PrepareNew(Guid clinicId)
    {
        RecordId = null;
        var next = await _service.NextItemNoAsync(clinicId);
        var defaultBase = UomOptions.FirstOrDefault(u => u.Code.Equals("Pcs", StringComparison.OrdinalIgnoreCase))?.Code
            ?? UomOptions.First().Code;
        var defaultAlt = UomOptions.FirstOrDefault(u => u.Code.Equals("Box", StringComparison.OrdinalIgnoreCase))?.Code
            ?? UomOptions.Skip(1).FirstOrDefault()?.Code;
        Input = new PharmacyItemInput { ItemNo = next, BaseUom = defaultBase, AlternateUom = defaultAlt, ConversionFactor = 1, IsActive = true };
    }

    private async Task<IActionResult> SaveCoreAsync()
    {
        var clinicId = await RequireClinicIdAsync();
        if (clinicId is null) return Forbid();
        if (string.IsNullOrWhiteSpace(Input.Barcode) || string.IsNullOrWhiteSpace(Input.MedicineName))
        {
            ModelState.AddModelError(string.Empty, "Barcode and Medicine Name are required.");
            await LoadAsync(clinicId.Value);
            await LoadUomOptionsAsync(clinicId.Value);
            await LoadAccountsAsync(clinicId.Value);
            return Page();
        }
        try
        {
            var entity = Input.ToEntity(RecordId);
            var saved = await _service.SaveAsync(clinicId.Value, entity, UserName);
            return RedirectAfterSave(saved.Id);
        }
        catch (InvalidOperationException ex)
        {
            ModelState.AddModelError(string.Empty, ex.Message);
            await LoadAsync(clinicId.Value);
            await LoadUomOptionsAsync(clinicId.Value);
            await LoadAccountsAsync(clinicId.Value);
            return Page();
        }
    }

    private Task<IActionResult> NewCoreAsync()
    {
        RecordId = null;
        return Task.FromResult<IActionResult>(RedirectToNewForm());
    }

    private async Task<IActionResult> DeleteCoreAsync()
    {
        var clinicId = await RequireClinicIdAsync();
        if (clinicId is null || !RecordId.HasValue) return RedirectToPage();
        try
        {
            await _service.DeleteAsync(clinicId.Value, RecordId.Value, UserName);
        }
        catch (InvalidOperationException ex)
        {
            ModelState.AddModelError(string.Empty, ex.Message);
            await LoadAsync(clinicId.Value);
            await LoadUomOptionsAsync(clinicId.Value);
            await LoadAccountsAsync(clinicId.Value);
            return Page();
        }
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

    public sealed class PharmacyItemInput
    {
        public int ItemNo { get; set; }
        public string Barcode { get; set; } = string.Empty;
        public string MedicineCode { get; set; } = string.Empty;
        public string MedicineName { get; set; } = string.Empty;
        public string? Dosage { get; set; }
        public string BaseUom { get; set; } = "Pcs";
        public string? AlternateUom { get; set; }
        public decimal ConversionFactor { get; set; } = 1;
        public decimal DefaultUnitPrice { get; set; }
        public decimal ItemCost { get; set; }
        public int ReorderPoint { get; set; }
        public int QuantityOnHand { get; set; }
        public string? IncomeAccountName { get; set; }
        public string? CostAccountName { get; set; }
        public string? InventoryAccountName { get; set; }
        public bool IsActive { get; set; } = true;
        public DateTime? ExpiryDate { get; set; }
        public DateTime? UpdatedAt { get; set; }

        public static PharmacyItemInput FromEntity(PharmacyItem t) => new()
        {
            ItemNo = t.ItemNo,
            Barcode = t.Barcode,
            MedicineCode = t.MedicineCode,
            MedicineName = t.MedicineName,
            Dosage = t.Dosage,
            BaseUom = t.BaseUom,
            AlternateUom = t.AlternateUom,
            ConversionFactor = t.ConversionFactor,
            DefaultUnitPrice = t.DefaultUnitPrice,
            ItemCost = t.MovingAverageCost,
            ReorderPoint = t.ReorderPoint,
            QuantityOnHand = t.QuantityOnHand,
            IncomeAccountName = t.IncomeAccountName,
            CostAccountName = t.CostAccountName,
            InventoryAccountName = t.InventoryAccountName,
            IsActive = t.IsActive,
            ExpiryDate = t.ExpiryDate,
            UpdatedAt = t.UpdatedAt
        };

        public PharmacyItem ToEntity(Guid? id) => new()
        {
            Id = id ?? Guid.Empty,
            ItemNo = ItemNo,
            Barcode = (Barcode ?? string.Empty).Trim(),
            MedicineCode = (MedicineCode ?? string.Empty).Trim(),
            MedicineName = (MedicineName ?? string.Empty).Trim(),
            Dosage = Dosage,
            BaseUom = string.IsNullOrWhiteSpace(BaseUom) ? "Pcs" : BaseUom.Trim(),
            AlternateUom = AlternateUom,
            ConversionFactor = ConversionFactor,
            DefaultUnitPrice = DefaultUnitPrice,
            MovingAverageCost = ItemCost,
            ReorderPoint = ReorderPoint,
            IncomeAccountName = IncomeAccountName,
            CostAccountName = CostAccountName,
            InventoryAccountName = InventoryAccountName,
            IsActive = IsActive,
            ExpiryDate = ExpiryDate
        };
    }
}
