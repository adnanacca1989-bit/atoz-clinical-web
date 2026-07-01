using AtoZClinical.Infrastructure.Services;
using AtoZClinical.Web.Services;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace AtoZClinical.Web.Pages.Ward;

public class PatientRoomModel : PageModel
{
    private readonly ClinicContextService _clinicContext;
    private readonly WardRoomService _wardRooms;

    public PatientRoomModel(ClinicContextService clinicContext, WardRoomService wardRooms)
    {
        _clinicContext = clinicContext;
        _wardRooms = wardRooms;
    }

    public WardRoomBoard Board { get; private set; } = new();

    public async Task<IActionResult> OnGetAsync()
    {
        var clinicId = await _clinicContext.GetClinicIdAsync();
        if (clinicId is null) return Forbid();
        Board = await _wardRooms.GetBoardAsync(clinicId.Value);
        return Page();
    }

    public async Task<IActionResult> OnPostMarkAvailableAsync(int roomNo)
    {
        var clinicId = await _clinicContext.GetClinicIdAsync();
        if (clinicId is null) return Forbid();
        await _wardRooms.MarkRoomAvailableAsync(clinicId.Value, roomNo);
        return RedirectToPage();
    }
}
