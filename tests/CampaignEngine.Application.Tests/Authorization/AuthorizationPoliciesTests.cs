using CampaignEngine.Application.DependencyInjection;
using CampaignEngine.Domain.Enums;
using Microsoft.AspNetCore.Authorization;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using System.Security.Claims;

namespace CampaignEngine.Application.Tests.Authorization;

/// <summary>
/// Unit tests verifying that authorization policies enforce correct role requirements.
/// Tests use IAuthorizationService with in-memory policy evaluation.
/// </summary>
public class AuthorizationPoliciesTests
{
    private readonly IAuthorizationService _authorizationService;

    public AuthorizationPoliciesTests()
    {
        var services = new ServiceCollection();
        services.AddLogging(); // DefaultAuthorizationService requires ILogger
        services.AddApplication(); // registers all policies via AddAuthorizationCore
        var sp = services.BuildServiceProvider();
        _authorizationService = sp.GetRequiredService<IAuthorizationService>();
    }

    // -------------------------------------------------------------------------
    // Helper: build a ClaimsPrincipal with a given set of roles
    // -------------------------------------------------------------------------
    private static ClaimsPrincipal BuildPrincipal(params string[] roles)
    {
        var claims = new List<Claim> { new(ClaimTypes.Name, "testuser") };
        claims.AddRange(roles.Select(r => new Claim(ClaimTypes.Role, r)));
        var identity = new ClaimsIdentity(claims, "TestAuthentication");
        return new ClaimsPrincipal(identity);
    }

    private static ClaimsPrincipal UnauthenticatedPrincipal()
        => new(new ClaimsIdentity()); // no authentication type = not authenticated

    // -------------------------------------------------------------------------
    // RequireAdmin
    // -------------------------------------------------------------------------

    [Fact]
    public async Task RequireAdmin_AdminUser_Authorized()
    {
        var user = BuildPrincipal(UserRole.Admin);
        var result = await _authorizationService.AuthorizeAsync(user, null, AuthorizationPolicies.RequireAdmin);
        result.Succeeded.Should().BeTrue();
    }

    [Fact]
    public async Task RequireAdmin_DesignerUser_Denied()
    {
        var user = BuildPrincipal(UserRole.Designer);
        var result = await _authorizationService.AuthorizeAsync(user, null, AuthorizationPolicies.RequireAdmin);
        result.Succeeded.Should().BeFalse();
    }

    [Fact]
    public async Task RequireAdmin_OperatorUser_Denied()
    {
        var user = BuildPrincipal(UserRole.Operator);
        var result = await _authorizationService.AuthorizeAsync(user, null, AuthorizationPolicies.RequireAdmin);
        result.Succeeded.Should().BeFalse();
    }

    [Fact]
    public async Task RequireAdmin_UnauthenticatedUser_Denied()
    {
        var user = UnauthenticatedPrincipal();
        var result = await _authorizationService.AuthorizeAsync(user, null, AuthorizationPolicies.RequireAdmin);
        result.Succeeded.Should().BeFalse();
    }

    // -------------------------------------------------------------------------
    // RequireDesigner
    // -------------------------------------------------------------------------

    [Fact]
    public async Task RequireDesigner_DesignerUser_Authorized()
    {
        var user = BuildPrincipal(UserRole.Designer);
        var result = await _authorizationService.AuthorizeAsync(user, null, AuthorizationPolicies.RequireDesigner);
        result.Succeeded.Should().BeTrue();
    }

    [Fact]
    public async Task RequireDesigner_OperatorUser_Denied()
    {
        var user = BuildPrincipal(UserRole.Operator);
        var result = await _authorizationService.AuthorizeAsync(user, null, AuthorizationPolicies.RequireDesigner);
        result.Succeeded.Should().BeFalse();
    }

    [Fact]
    public async Task RequireDesigner_AdminUser_Denied()
    {
        var user = BuildPrincipal(UserRole.Admin);
        var result = await _authorizationService.AuthorizeAsync(user, null, AuthorizationPolicies.RequireDesigner);
        result.Succeeded.Should().BeFalse();
    }

    // -------------------------------------------------------------------------
    // RequireOperator
    // -------------------------------------------------------------------------

    [Fact]
    public async Task RequireOperator_OperatorUser_Authorized()
    {
        var user = BuildPrincipal(UserRole.Operator);
        var result = await _authorizationService.AuthorizeAsync(user, null, AuthorizationPolicies.RequireOperator);
        result.Succeeded.Should().BeTrue();
    }

    [Fact]
    public async Task RequireOperator_DesignerUser_Denied()
    {
        var user = BuildPrincipal(UserRole.Designer);
        var result = await _authorizationService.AuthorizeAsync(user, null, AuthorizationPolicies.RequireOperator);
        result.Succeeded.Should().BeFalse();
    }

    [Fact]
    public async Task RequireOperator_AdminUser_Denied()
    {
        var user = BuildPrincipal(UserRole.Admin);
        var result = await _authorizationService.AuthorizeAsync(user, null, AuthorizationPolicies.RequireOperator);
        result.Succeeded.Should().BeFalse();
    }

    // -------------------------------------------------------------------------
    // RequireDesignerOrAdmin
    // -------------------------------------------------------------------------

    [Fact]
    public async Task RequireDesignerOrAdmin_DesignerUser_Authorized()
    {
        var user = BuildPrincipal(UserRole.Designer);
        var result = await _authorizationService.AuthorizeAsync(user, null, AuthorizationPolicies.RequireDesignerOrAdmin);
        result.Succeeded.Should().BeTrue();
    }

    [Fact]
    public async Task RequireDesignerOrAdmin_AdminUser_Authorized()
    {
        var user = BuildPrincipal(UserRole.Admin);
        var result = await _authorizationService.AuthorizeAsync(user, null, AuthorizationPolicies.RequireDesignerOrAdmin);
        result.Succeeded.Should().BeTrue();
    }

    [Fact]
    public async Task RequireDesignerOrAdmin_OperatorUser_Denied()
    {
        var user = BuildPrincipal(UserRole.Operator);
        var result = await _authorizationService.AuthorizeAsync(user, null, AuthorizationPolicies.RequireDesignerOrAdmin);
        result.Succeeded.Should().BeFalse();
    }

    // -------------------------------------------------------------------------
    // RequireOperatorOrAdmin
    // -------------------------------------------------------------------------

    [Fact]
    public async Task RequireOperatorOrAdmin_OperatorUser_Authorized()
    {
        var user = BuildPrincipal(UserRole.Operator);
        var result = await _authorizationService.AuthorizeAsync(user, null, AuthorizationPolicies.RequireOperatorOrAdmin);
        result.Succeeded.Should().BeTrue();
    }

    [Fact]
    public async Task RequireOperatorOrAdmin_AdminUser_Authorized()
    {
        var user = BuildPrincipal(UserRole.Admin);
        var result = await _authorizationService.AuthorizeAsync(user, null, AuthorizationPolicies.RequireOperatorOrAdmin);
        result.Succeeded.Should().BeTrue();
    }

    [Fact]
    public async Task RequireOperatorOrAdmin_DesignerUser_Denied()
    {
        var user = BuildPrincipal(UserRole.Designer);
        var result = await _authorizationService.AuthorizeAsync(user, null, AuthorizationPolicies.RequireOperatorOrAdmin);
        result.Succeeded.Should().BeFalse();
    }

    // -------------------------------------------------------------------------
    // RequireAuthenticated
    // -------------------------------------------------------------------------

    [Fact]
    public async Task RequireAuthenticated_AnyAuthenticatedUser_Authorized()
    {
        foreach (var role in UserRole.All)
        {
            var user = BuildPrincipal(role);
            var result = await _authorizationService.AuthorizeAsync(user, null, AuthorizationPolicies.RequireAuthenticated);
            result.Succeeded.Should().BeTrue($"role '{role}' should pass RequireAuthenticated");
        }
    }

    [Fact]
    public async Task RequireAuthenticated_UnauthenticatedUser_Denied()
    {
        var user = UnauthenticatedPrincipal();
        var result = await _authorizationService.AuthorizeAsync(user, null, AuthorizationPolicies.RequireAuthenticated);
        result.Succeeded.Should().BeFalse();
    }

    // -------------------------------------------------------------------------
    // UserRole constants
    // -------------------------------------------------------------------------

    [Fact]
    public void UserRole_AllRoles_ContainsThreeRoles()
    {
        UserRole.All.Should().HaveCount(3);
        UserRole.All.Should().Contain(UserRole.Admin);
        UserRole.All.Should().Contain(UserRole.Designer);
        UserRole.All.Should().Contain(UserRole.Operator);
    }

    [Fact]
    public void UserRole_Default_IsOperator()
    {
        UserRole.Default.Should().Be(UserRole.Operator);
    }
}
