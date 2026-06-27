using System.Security.Claims;
using Microsoft.AspNetCore.SignalR;

namespace AtoZClinical.Web.Hubs;

/// <summary>Maps SignalR connections to ASP.NET Identity user ids for Clients.User().</summary>
public sealed class SignalRUserIdProvider : IUserIdProvider
{
    public string? GetUserId(HubConnectionContext connection) =>
        connection.User?.FindFirstValue(ClaimTypes.NameIdentifier);
}
