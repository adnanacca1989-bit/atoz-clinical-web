using AtoZClinical.Core.Entities;
using AtoZClinical.Infrastructure.Services;
using AtoZClinical.Web.Services;
using Microsoft.AspNetCore.Mvc;

namespace AtoZClinical.Web.Pages.Pharmacy;

public class OpeningBalanceModel : ClinicFormPageModel
{
    private readonly PharmacyOpeningBalanceService _service;
    private readonly PharmacyItemRegistrationService _items;
    private const int DefaultLineCount = 8;

    public OpeningBalanceModel(ClinicContextService clinicContext, PharmacyOpeningBalanceService service, PharmacyItemRegistrationService items) : base(clinicContext)
    {
        _service = service;
        _items = items;
    }

    [BindProperty]
    public OpeningBalanceInput Input { get; set; } = new();

    [BindProperty]
    public List<OpeningBalanceLineInput> Lines { get; set; } = [];

    public List<PharmacyOpeningBalance> Records { get; private set; } = [];
    public List<PharmacyItem> RegisteredItems { get; private set; } = [];

    public decimal LineTotal => Lines.Sum(l => l.Total);

    public async Task<IActionResult> OnGetAsync()
    {
        var clinicId = await RequireClinicIdAsync();
        if (clinicId is null) return Forbid();
        await LoadAsync(clinicId.Value);
        RegisteredItems = await _items.ListActiveAsync(clinicId.Value);
        if (RecordId.HasValue)
            await LoadRecord(clinicId.Value, RecordId.Value);
        else if (NewRecord)
            await PrepareNew(clinicId.Value);
        else if (Records.Count > 0 && Input.BalanceNo == 0)
            await LoadRecord(clinicId.Value, Records[0].Id);
        else
            await PrepareNew(clinicId.Value);
        SetFormViewData("Pharmacy Opening Balance", null, null, Input.UpdatedAt);
        ViewData["ShowAddLines"] = true;
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
        if (!string.IsNullOrWhiteSpace(Search))
            Records = Records.Where(r => r.BalanceNo.ToString().Contains(Search)).ToList();
    }

    private async Task LoadRecord(Guid clinicId, Guid id)
    {
        var item = await _service.GetAsync(clinicId, id);
        if (item is null) return;
        RecordId = item.Id;
        Input = OpeningBalanceInput.FromEntity(item);
        Lines = item.Lines.OrderBy(l => l.LineNo).Select(OpeningBalanceLineInput.FromEntity).ToList();
        EnsureLineRows();
    }

    private async Task PrepareNew(Guid clinicId)
    {
        RecordId = null;
        var all = await _service.ListAsync(clinicId);
        var next = (all.Count > 0 ? all.Max(b => b.BalanceNo) : 0) + 1;
        Input = new OpeningBalanceInput { BalanceNo = next, BalanceDate = DateTime.Today };
        Lines = CreateEmptyLines();
    }

    private async Task<IActionResult> SaveCoreAsync()
    {
        var clinicId = await RequireClinicIdAsync();
        if (clinicId is null) return Forbid();
        ResolveRecordIdForSave();
        var entity = Input.ToEntity(RecordIdForSave);
        var lines = Lines
            .Where(l => l.Qty > 0 && (!string.IsNullOrWhiteSpace(l.Barcode) || !string.IsNullOrWhiteSpace(l.MedicineName)))
            .Select(l => l.ToEntity())
            .ToList();
        var saved = await _service.SaveAsync(clinicId.Value, entity, lines, UserName);
        return RedirectToRecord(saved.Id);
    }

    protected override async Task ReloadAfterSaveFailureAsync()
    {
        var clinicId = await RequireClinicIdAsync();
        if (clinicId is null) return;
        RegisteredItems = await _items.ListActiveAsync(clinicId.Value);
        await LoadAsync(clinicId.Value);
        EnsureLineRows();
        SetFormViewData("Pharmacy Opening Balance", null, null, Input.UpdatedAt);
        ViewData["ShowAddLines"] = true;
        if (!RecordId.HasValue)
            await PrepareNew(clinicId.Value);
    }

    private Task<IActionResult> NewCoreAsync() => Task.FromResult(RedirectToNewForm());

    private async Task<IActionResult> DeleteCoreAsync()
    {
        var clinicId = await RequireClinicIdAsync();
        if (clinicId is null || !RecordId.HasValue) return RedirectToPage();
        await _service.DeleteAsync(clinicId.Value, RecordId.Value, UserName);
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
            Lines.Add(new OpeningBalanceLineInput { LineNo = Lines.Count + 1 });
    }

    private static List<OpeningBalanceLineInput> CreateEmptyLines() =>
        Enumerable.Range(1, DefaultLineCount).Select(i => new OpeningBalanceLineInput { LineNo = i }).ToList();

    public sealed class OpeningBalanceInput
    {
        public int BalanceNo { get; set; }
        public DateTime BalanceDate { get; set; } = DateTime.Today;
        public string? Notes { get; set; }
        public DateTime? UpdatedAt { get; set; }

        public static OpeningBalanceInput FromEntity(PharmacyOpeningBalance b) => new()
        {
            BalanceNo = b.BalanceNo,
            BalanceDate = b.BalanceDate,
            Notes = b.Notes,
            UpdatedAt = b.UpdatedAt
        };

        public PharmacyOpeningBalance ToEntity(Guid? id) => new()
        {
            Id = id ?? Guid.Empty,
            BalanceNo = BalanceNo,
            BalanceDate = BalanceDate,
            Notes = Notes
        };
    }

    public sealed class OpeningBalanceLineInput
    {
        public int LineNo { get; set; }
        public string? Barcode { get; set; }
        public string? MedicineCode { get; set; }
        public string? MedicineName { get; set; }
        public string? Dosage { get; set; }
        public string? Uom { get; set; }
        public int Qty { get; set; }
        public decimal UnitCost { get; set; }

        public decimal Total => Qty * UnitCost;

        public static OpeningBalanceLineInput FromEntity(PharmacyOpeningBalanceLine l) => new()
        {
            LineNo = l.LineNo,
            Barcode = l.Barcode,
            MedicineCode = l.MedicineCode,
            MedicineName = l.MedicineName,
            Dosage = l.Dosage,
            Uom = l.Uom,
            Qty = l.Qty,
            UnitCost = l.UnitCost
        };

        public PharmacyOpeningBalanceLine ToEntity() => new()
        {
            LineNo = LineNo,
            Barcode = Barcode,
            MedicineCode = MedicineCode,
            MedicineName = MedicineName,
            Dosage = Dosage,
            Uom = Uom,
            Qty = Qty,
            UnitCost = UnitCost,
            Total = Qty * UnitCost
        };
    }
}
