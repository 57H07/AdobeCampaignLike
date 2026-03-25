using Hangfire.Dashboard;

namespace CampaignEngine.Web.Middleware;

/// <summary>
/// Restricts Hangfire dashboard access to authenticated Admin users only.
/// Business rule: Hangfire dashboard accessible only to Admin role (US-026).
/// Falls back to allowing authenticated users in Development for convenience.
/// </summary>
public sealed class HangfireAdminAuthorizationFilter : IDashboardAuthorizationFilter
{
    public bool Authorize(DashboardContext context)
    {
        var httpContext = context.GetHttpContext();

        // Must be authenticated
        if (httpContext.User.Identity?.IsAuthenticated != true)
            return false;

        // Must be in Admin role
        return httpContext.User.IsInRole("Admin");
    }
}
