using AtoZClinical.Core.Entities;
using AtoZClinical.Infrastructure.Services;
using AtoZClinical.Web.Services;
using Microsoft.AspNetCore.Mvc;

namespace AtoZClinical.Web.Pages.Pharmacy;

public class PurchaseModel : ClinicFormPageModel
{
    private readonly PharmacyPurchaseBillService _service;
    private readonly PharmacyItemRegistrationService _items;
    private readonly ClinicLookupService _lookup;
    private const int DefaultLineCount = 8;

    public PurchaseModel(
        ClinicContextService clinicContext,
        PharmacyPurchaseBillService service,
        PharmacyItemRegistrationService items,
        ClinicLookupService lookup) : base(clinicContext)
    {
        _service = service;
        _items = items;
        _lookup = lookup;
    }

    [BindProperty] public PurchaseInput Input { get; set; } = new();
    [BindProperty] public List<PurchaseLineInput> Lines { get; set; } = [];
    public List<PharmacyPurchaseBill> Records { get; private set; } = [];
    public List<PharmacyItem> RegisteredItems { get; private set; } = [];
    public List<ClinicVendor> RegisteredVendors { get; private set; } = [];

    public decimal LineSubTotal => Lines.Sum(l => l.LineTotal);
    public decimal NetAmount => LineSubTotal - Input.DiscountAmount;

    public async Task<IActionResult> OnGetAsync()
    {
        var clinicId = await RequireClinicIdAsync();
        if (clinicId is null) return Forbid();
        await LoadAsync(clinicId.Value);
        RegisteredItems = await _items.ListActiveAsync(clinicId.Value);
        RegisteredVendors = await _lookup.ListVendorsAsync(clinicId.Value, activeOnly: true);
        if (RecordId.HasValue) await LoadRecord(clinicId.Value, RecordId.Value);
        else if (NewRecord) await PrepareNew(clinicId.Value);
        else if (Records.Count > 0 && Input.PurchaseNo == 0) await LoadRecord(clinicId.Value, Records[0].Id);
        else await PrepareNew(clinicId.Value);
        SetFormViewData("Pharmacy Purchase Bill", null, null, Input.UpdatedAt);
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
        Records = await _service.ListAsync(clinicId);
        if (!string.IsNullOrWhiteSpace(Search))
            Records = Records.Where(r =>
                r.PurchaseNo.ToString().Contains(Search) ||
                (r.SupplierName?.Contains(Search, StringComparison.OrdinalIgnoreCase) == true)).ToList();
    }

    private async Task LoadRecord(Guid clinicId, Guid id)
    {
        var item = await _service.GetAsync(clinicId, id);
        if (item is null) return;
        RecordId = item.Id;
        Input = PurchaseInput.FromEntity(item);
        Lines = item.Lines.OrderBy(l => l.LineNo).Select(PurchaseLineInput.FromEntity).ToList();
        EnsureLineRows();
    }

    private async Task PrepareNew(Guid clinicId)
    {
        RecordId = null;
        var all = await _service.ListAsync(clinicId);
        Input = new PurchaseInput
        {
            PurchaseNo = (all.Count > 0 ? all.Max(b => b.PurchaseNo) : 0) + 1,
            PurchaseDate = DateTime.Today,
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
            .Where(l => l.Qty > 0 && (!string.IsNullOrWhiteSpace(l.Barcode) || !string.IsNullOrWhiteSpace(l.MedicineName)))
            .Select(l => l.ToEntity())
            .ToList();

        if (lines.Count == 0)
        {
            ModelState.AddModelError(string.Empty, "Add at least one purchase line with quantity and a registered pharmacy item.");
            await ReloadFormAsync(clinicId.Value);
            return Page();
        }

        try
        {
            var saved = await _service.SaveAsync(clinicId.Value, entity, lines, UserName);
            return RedirectAfterSave(saved.Id);
        }
        catch (InvalidOperationException ex)
        {
            ModelState.AddModelError(string.Empty, ex.Message);
            await ReloadFormAsync(clinicId.Value);
            EnsureLineRows();
            return Page();
        }
    }

    private async Task ReloadFormAsync(Guid clinicId)
    {
        await LoadAsync(clinicId);
        RegisteredItems = await _items.ListActiveAsync(clinicId);
        RegisteredVendors = await _lookup.ListVendorsAsync(clinicId, activeOnly: true);
        SetFormViewData("Pharmacy Purchase Bill", null, null, Input.UpdatedAt);
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
            Lines.Add(new PurchaseLineInput { LineNo = Lines.Count + 1 });
    }

    private static List<PurchaseLineInput> CreateEmptyLines() =>
        Enumerable.Range(1, DefaultLineCount).Select(i => new PurchaseLineInput { LineNo = i }).ToList();

    public sealed class PurchaseInput
    {
        public int PurchaseNo { get; set; }
        public DateTime PurchaseDate { get; set; } = DateTime.Today;
        public string? SupplierName { get; set; }
        public string? SupplierPhone { get; set; }
        public string? SupplierInvoiceNo { get; set; }
        public decimal DiscountAmount { get; set; }
        public decimal DiscountPercent { get; set; }
        public decimal AmountPaid { get; set; }
        public string PaymentMethod { get; set; } = "Cash";
        public string? PaymentStatus { get; set; }
        public string? Notes { get; set; }
        public DateTime? UpdatedAt { get; set; }

        public static PurchaseInput FromEntity(PharmacyPurchaseBill b) => new()
        {
            PurchaseNo = b.PurchaseNo,
            PurchaseDate = b.PurchaseDate,
            SupplierName = b.SupplierName,
            SupplierPhone = b.SupplierPhone,
            SupplierInvoiceNo = b.SupplierInvoiceNo,
            DiscountAmount = b.DiscountAmount,
            DiscountPercent = b.DiscountPercent,
            AmountPaid = b.AmountPaid,
            PaymentMethod = b.PaymentMethod,
            PaymentStatus = b.PaymentStatus,
            Notes = b.Notes,
            UpdatedAt = b.UpdatedAt
        };

        public PharmacyPurchaseBill ToEntity(Guid? id) => new()
        {
            Id = id ?? Guid.Empty,
            PurchaseNo = PurchaseNo,
            PurchaseDate = PurchaseDate,
            SupplierName = SupplierName,
            SupplierPhone = SupplierPhone,
            SupplierInvoiceNo = SupplierInvoiceNo,
            DiscountAmount = DiscountAmount,
            DiscountPercent = DiscountPercent,
            AmountPaid = AmountPaid,
            PaymentMethod = PaymentMethod,
            PaymentStatus = PaymentStatus,
            Notes = Notes
        };
    }

    public sealed class PurchaseLineInput
    {
        public int LineNo { get; set; }
        public string? Barcode { get; set; }
        public string? MedicineCode { get; set; }
        public string? MedicineName { get; set; }
        public string? Dosage { get; set; }
        public string? Uom { get; set; }
        public int Qty { get; set; }
        public decimal UnitCost { get; set; }
        public decimal LineTotal => Qty * UnitCost;

        public static PurchaseLineInput FromEntity(PharmacyPurchaseBillLine l) => new()
        {
            LineNo = l.LineNo, Barcode = l.Barcode, MedicineCode = l.MedicineCode,
            MedicineName = l.MedicineName, Dosage = l.Dosage, Uom = l.Uom, Qty = l.Qty, UnitCost = l.UnitCost
        };

        public PharmacyPurchaseBillLine ToEntity() => new()
        {
            LineNo = LineNo, Barcode = Barcode, MedicineCode = MedicineCode, MedicineName = MedicineName,
            Dosage = Dosage, Uom = Uom, Qty = Qty, UnitCost = UnitCost, LineTotal = Qty * UnitCost
        };
    }
}
