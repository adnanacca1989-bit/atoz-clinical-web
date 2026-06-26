using System.ComponentModel.DataAnnotations;
using AtoZClinical.Core.Entities;
using AtoZClinical.Infrastructure.Services;
using AtoZClinical.Web.Services;
using Microsoft.AspNetCore.Mvc;

namespace AtoZClinical.Web.Pages.Pharmacy;

public class BillModel : ClinicFormPageModel
{
    private readonly PharmacyBillService _service;
    private readonly PharmacyRequestService _requests;
    private readonly PharmacyItemRegistrationService _items;
    private const int DefaultLineCount = 8;

    public BillModel(
        ClinicContextService clinicContext,
        PharmacyBillService service,
        PharmacyRequestService requests,
        PharmacyItemRegistrationService items) : base(clinicContext)
    {
        _service = service;
        _requests = requests;
        _items = items;
    }

    [BindProperty(SupportsGet = true)]
    public int? LoadRequestNo { get; set; }

    [BindProperty(SupportsGet = true)]
    public string? LoadPatientId { get; set; }

    [BindProperty]
    public PharmacyBillInput Input { get; set; } = new();

    [BindProperty]
    public List<PharmacyBillLineInput> Lines { get; set; } = [];

    public List<PharmacyBill> Records { get; private set; } = [];
    public List<PharmacyItem> RegisteredItems { get; private set; } = [];

    public decimal LineSubTotal => Lines.Sum(l => l.LineTotal);
    public decimal NetAmount => LineSubTotal - Input.Discount;
    public decimal Balance => NetAmount - Input.AmountPaid;

    public async Task<IActionResult> OnGetAsync()
    {
        var clinicId = await RequireClinicIdAsync();
        if (clinicId is null) return Forbid();
        await LoadAsync(clinicId.Value);
        RegisteredItems = await _items.ListActiveAsync(clinicId.Value);
        if (RecordId.HasValue)
            await LoadRecord(clinicId.Value, RecordId.Value);
        else if (NewRecord || LoadRequestNo.HasValue || !string.IsNullOrWhiteSpace(LoadPatientId))
            await PrepareNew(clinicId.Value);
        else if (Records.Count > 0 && Input.BillNo == 0)
            await LoadRecord(clinicId.Value, Records[0].Id);
        else
            await PrepareNew(clinicId.Value);

        if (!RecordId.HasValue)
        {
            if (LoadRequestNo.HasValue && LoadRequestNo.Value > 0)
                await ApplyPharmacyRequestAsync(clinicId.Value, LoadRequestNo.Value);
            else if (!string.IsNullOrWhiteSpace(LoadPatientId))
            {
                var request = await _requests.GetLatestByPatientAsync(clinicId.Value, null, LoadPatientId.Trim());
                if (request is not null)
                    await ApplyPharmacyRequestAsync(clinicId.Value, request.RequestNo);
            }
        }

        SetFormViewData("Pharmacy Bill", null, null, Input.UpdatedAt);
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

    private async Task ApplyPharmacyRequestAsync(Guid clinicId, int requestNo)
    {
        var request = await _requests.GetByRequestNoAsync(clinicId, requestNo);
        if (request is null) return;

        Input.RequestNo = request.RequestNo;
        Input.PatientName = request.PatientName;
        Input.PatientId = request.PatientId;
        Input.Age = request.Age;
        Input.Gender = request.Gender;
        Input.Phone = request.Phone;
        Input.City = request.City;
        Input.DoctorName = request.DoctorName;
        Input.Specialty = request.Specialty;

        var requestLines = request.Lines
            .Where(l => l.Qty > 0 && (!string.IsNullOrWhiteSpace(l.MedicineName) || !string.IsNullOrWhiteSpace(l.MedicineCode) || !string.IsNullOrWhiteSpace(l.Barcode)))
            .OrderBy(l => l.LineNo)
            .ToList();

        if (requestLines.Count == 0) return;

        var items = await _items.ListActiveAsync(clinicId);

        Lines = requestLines.Select(l =>
        {
            var registered = PharmacyItemRegistrationService.ResolveForLine(
                items, l.Barcode, l.MedicineCode, l.MedicineName);
            var defaultPrice = registered?.DefaultUnitPrice ?? l.UnitPrice;
            if (defaultPrice <= 0 && registered is not null)
                defaultPrice = registered.DefaultUnitPrice;

            return new PharmacyBillLineInput
            {
                LineNo = l.LineNo,
                Barcode = registered?.Barcode ?? l.Barcode,
                MedicineCode = registered?.MedicineCode ?? l.MedicineCode,
                MedicineName = registered?.MedicineName ?? l.MedicineName,
                Dosage = registered?.Dosage ?? l.Dosage,
                Uom = registered?.BaseUom ?? l.Uom,
                Qty = l.Qty,
                UnitPrice = defaultPrice > 0 ? defaultPrice : l.UnitPrice,
                ResolvedItemId = registered?.Id
            };
        }).ToList();
        EnsureLineRows();
    }

    private async Task LoadAsync(Guid clinicId)
    {
        Records = await _service.ListAsync(clinicId);
        if (!string.IsNullOrWhiteSpace(Search))
            Records = Records.Where(r =>
                (r.PatientName?.Contains(Search, StringComparison.OrdinalIgnoreCase) == true) ||
                (r.DoctorName?.Contains(Search, StringComparison.OrdinalIgnoreCase) == true) ||
                r.BillNo.ToString().Contains(Search)).ToList();
    }

    private async Task LoadRecord(Guid clinicId, Guid id)
    {
        var item = await _service.GetAsync(clinicId, id);
        if (item is null) return;
        RecordId = item.Id;
        Input = PharmacyBillInput.FromEntity(item);
        Lines = item.Lines.OrderBy(l => l.LineNo).Select(PharmacyBillLineInput.FromEntity).ToList();
        EnsureLineRows();
    }

    private async Task PrepareNew(Guid clinicId)
    {
        RecordId = null;
        var next = await _service.NextBillNoAsync(clinicId);
        Input = new PharmacyBillInput
        {
            BillNo = next,
            BillDate = DateTime.Today,
            PaymentMethod = ClinicLookup.PaymentMethods[0],
            PaymentStatus = "Unpaid"
        };
        Lines = CreateEmptyLines();
    }

    private async Task<IActionResult> SaveCoreAsync()
    {
        var clinicId = await RequireClinicIdAsync();
        if (clinicId is null) return Forbid();
        ResolveRecordIdForSave();
        if (!ModelState.IsValid)
        {
            await ReloadFormAsync(clinicId.Value);
            return Page();
        }
        var entity = Input.ToEntity(RecordIdForSave);
        var lines = Lines
            .Where(l => !string.IsNullOrWhiteSpace(l.MedicineName))
            .Select(l => l.ToEntity())
            .ToList();
        var saved = await _service.SaveAsync(clinicId.Value, entity, lines, UserName);
        return RedirectAfterSave(saved.Id);
    }

    protected override async Task ReloadAfterSaveFailureAsync()
    {
        var clinicId = await RequireClinicIdAsync();
        if (clinicId is null) return;
        await ReloadFormAsync(clinicId.Value);
        if (!RecordId.HasValue)
            await PrepareNew(clinicId.Value);
    }

    private Task<IActionResult> NewCoreAsync() => Task.FromResult(RedirectToNewForm());

    private async Task ReloadFormAsync(Guid clinicId)
    {
        await LoadAsync(clinicId);
        RegisteredItems = await _items.ListActiveAsync(clinicId);
        EnsureLineRows();
        SetFormViewData("Pharmacy Bill", null, null, Input.UpdatedAt);
        ViewData["OpenPatientSelect"] = true;
        ViewData["ShowAddLines"] = true;
    }

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

    private void EnsureLineRows()
    {
        while (Lines.Count < DefaultLineCount)
            Lines.Add(new PharmacyBillLineInput { LineNo = Lines.Count + 1 });
    }

    private static List<PharmacyBillLineInput> CreateEmptyLines() =>
        Enumerable.Range(1, DefaultLineCount).Select(i => new PharmacyBillLineInput { LineNo = i }).ToList();

    public sealed class PharmacyBillInput
    {
        public int BillNo { get; set; }

        [Required(ErrorMessage = "Bill Date is required.")]
        public DateTime BillDate { get; set; } = DateTime.Today;

        public int? RequestNo { get; set; }

        [Required(ErrorMessage = "Patient Name is required.")]
        public string? PatientName { get; set; }

        public string? PatientId { get; set; }

        [Required(ErrorMessage = "Age is required.")]
        public int? Age { get; set; }

        [Required(ErrorMessage = "Sex is required.")]
        public string? Gender { get; set; }

        public string? Phone { get; set; }
        public string? City { get; set; }

        [Required(ErrorMessage = "Doctor is required.")]
        public string? DoctorName { get; set; }

        [Required(ErrorMessage = "Specialty is required.")]
        public string? Specialty { get; set; }
        public decimal Discount { get; set; }
        public decimal AmountPaid { get; set; }
        public string PaymentMethod { get; set; } = "Cash";
        public string? PaymentStatus { get; set; }
        public string? Notes { get; set; }
        public DateTime? UpdatedAt { get; set; }

        public static PharmacyBillInput FromEntity(PharmacyBill b) => new()
        {
            BillNo = b.BillNo,
            BillDate = b.BillDate,
            RequestNo = b.RequestNo,
            PatientName = b.PatientName,
            PatientId = b.PatientId,
            DoctorName = b.DoctorName,
            Specialty = b.Specialty,
            Discount = b.Discount,
            AmountPaid = b.AmountPaid,
            PaymentMethod = b.PaymentMethod,
            PaymentStatus = b.PaymentStatus,
            Notes = b.Notes,
            UpdatedAt = b.UpdatedAt
        };

        public PharmacyBill ToEntity(Guid? id) => new()
        {
            Id = id ?? Guid.Empty,
            BillNo = BillNo,
            BillDate = BillDate,
            RequestNo = RequestNo,
            PatientName = PatientName,
            PatientId = PatientId,
            DoctorName = DoctorName,
            Specialty = Specialty,
            Discount = Discount,
            AmountPaid = AmountPaid,
            PaymentMethod = PaymentMethod,
            PaymentStatus = PaymentStatus,
            Notes = Notes
        };
    }

    public sealed class PharmacyBillLineInput
    {
        public int LineNo { get; set; }
        public string? Barcode { get; set; }
        public string? MedicineCode { get; set; }
        public string? MedicineName { get; set; }
        public string? Dosage { get; set; }
        public string? Uom { get; set; }
        public int Qty { get; set; } = 1;
        public decimal UnitPrice { get; set; }
        public Guid? ResolvedItemId { get; set; }

        public decimal LineTotal => Qty * UnitPrice;

        public static PharmacyBillLineInput FromEntity(PharmacyBillLine l) => new()
        {
            LineNo = l.LineNo,
            Barcode = l.Barcode,
            MedicineCode = l.MedicineCode,
            MedicineName = l.MedicineName,
            Dosage = l.Dosage,
            Uom = l.Uom,
            Qty = l.Qty,
            UnitPrice = l.UnitPrice
        };

        public PharmacyBillLine ToEntity() => new()
        {
            LineNo = LineNo,
            Barcode = Barcode,
            MedicineCode = MedicineCode,
            MedicineName = MedicineName,
            Dosage = Dosage,
            Uom = Uom,
            Qty = Qty,
            UnitPrice = UnitPrice,
            LineTotal = Qty * UnitPrice
        };
    }
}
