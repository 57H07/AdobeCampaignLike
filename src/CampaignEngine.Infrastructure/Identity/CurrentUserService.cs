using CampaignEngine.Application.Interfaces;
using Microsoft.AspNetCore.Http;
using System.Security.Claims;

namespace CampaignEngine.Infrastructure.Identity;

/// <summary>
/// Resolves the current authenticated user from the ASP.NET Core HttpContext.
/// Works with both ASP.NET Core Identity (cookie-based) and Windows Authentication.
/// </summary>
public class CurrentUserService : ICurrentUserService
{
    private readonly IHttpContextAccessor _httpContextAccessor;

    public CurrentUserService(IHttpContextAccessor httpContextAccessor)
    {
        _httpContextAccessor = httpContextAccessor;
    }

    private ClaimsPrincipal? User => _httpContextAccessor.HttpContext?.User;

    public string? UserId =>
        User?.FindFirstValue(ClaimTypes.NameIdentifier)
        ?? User?.FindFirstValue("sub");

    public string? UserName =>
        User?.FindFirstValue(ClaimTypes.Name)
        ?? User?.Identity?.Name;

    public IReadOnlyList<string> Roles =>
        User?.FindAll(ClaimTypes.Role)
             .Select(c => c.Value)
             .ToList()
             .AsReadOnly()
        ?? (IReadOnlyList<string>)Array.Empty<string>();

    public bool IsAuthenticated =>
        User?.Identity?.IsAuthenticated ?? false;
}
