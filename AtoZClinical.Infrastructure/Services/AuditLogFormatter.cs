namespace AtoZClinical.Infrastructure.Services;

public static class AuditLogFormatter
{
    public static string NormalizeAction(string? type) => (type ?? "").Trim() switch
    {
        "Create" or "Add" or "Insert" => "Add",
        "Update" or "Edit" => "Edit",
        "Delete" or "Remove" => "Delete",
        "" => "—",
        _ => type!.Trim()
    };
}
