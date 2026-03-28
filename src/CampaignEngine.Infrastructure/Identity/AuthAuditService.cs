using CampaignEngine.Application.Interfaces;
using CampaignEngine.Application.Interfaces.Repositories;
using CampaignEngine.Domain.Entities;

namespace CampaignEngine.Infrastructure.Identity;

/// <summary>
/// Persists authentication and authorization events to the AuthAuditLogs table.
/// </summary>
public class AuthAuditService : IAuthAuditService
{
    private readonly IAuthAuditLogRepository _auditLogRepository;
    private readonly IUnitOfWork _unitOfWork;

    public AuthAuditService(IAuthAuditLogRepository auditLogRepository, IUnitOfWork unitOfWork)
    {
        _auditLogRepository = auditLogRepository;
        _unitOfWork = unitOfWork;
    }

    public Task LogLoginAsync(string userId, string userName, string? ipAddress, CancellationToken ct = default)
        => LogEventAsync(AuthEventType.Login, userId, userName, null, ipAddress, succeeded: true, ct);

    public Task LogLoginFailedAsync(string userName, string? ipAddress, CancellationToken ct = default)
        => LogEventAsync(AuthEventType.LoginFailed, null, userName, null, ipAddress, succeeded: false, ct);

    public Task LogLogoutAsync(string userId, string userName, string? ipAddress, CancellationToken ct = default)
        => LogEventAsync(AuthEventType.Logout, userId, userName, null, ipAddress, succeeded: true, ct);

    public Task LogRoleAssignedAsync(
        string adminUserId, string adminUserName,
        string targetUserId, string targetUserName,
        string role, CancellationToken ct = default)
    {
        var details = $"Role '{role}' assigned to user '{targetUserName}' (id={targetUserId}) by admin '{adminUserName}'";
        return LogEventAsync(AuthEventType.RoleAssigned, adminUserId, adminUserName, details, null, succeeded: true, ct);
    }

    public Task LogRoleRemovedAsync(
        string adminUserId, string adminUserName,
        string targetUserId, string targetUserName,
        string role, CancellationToken ct = default)
    {
        var details = $"Role '{role}' removed from user '{targetUserName}' (id={targetUserId}) by admin '{adminUserName}'";
        return LogEventAsync(AuthEventType.RoleRemoved, adminUserId, adminUserName, details, null, succeeded: true, ct);
    }

    public Task LogUserCreatedAsync(
        string adminUserId, string adminUserName,
        string newUserId, string newUserName,
        CancellationToken ct = default)
    {
        var details = $"User '{newUserName}' (id={newUserId}) created by admin '{adminUserName}'";
        return LogEventAsync(AuthEventType.UserCreated, adminUserId, adminUserName, details, null, succeeded: true, ct);
    }

    public async Task LogEventAsync(
        string eventType,
        string? userId,
        string? userName,
        string? details,
        string? ipAddress,
        bool succeeded,
        CancellationToken ct = default)
    {
        var log = new AuthAuditLog
        {
            EventType = eventType,
            UserId = userId,
            UserName = userName,
            Details = details,
            IpAddress = ipAddress,
            Succeeded = succeeded,
            OccurredAt = DateTime.UtcNow
        };

        await _auditLogRepository.AddAsync(log, ct);
        await _unitOfWork.CommitAsync(ct);
    }
}
