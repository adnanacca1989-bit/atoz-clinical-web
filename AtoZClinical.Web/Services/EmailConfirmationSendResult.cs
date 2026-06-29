namespace AtoZClinical.Web.Services;

public enum EmailConfirmationSendResult
{
    Sent,
    AlreadyConfirmed,
    NotConfigured,
    Failed
}
