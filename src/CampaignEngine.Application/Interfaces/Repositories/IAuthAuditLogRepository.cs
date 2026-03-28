using CampaignEngine.Domain.Entities;

namespace CampaignEngine.Application.Interfaces.Repositories;

/// <summary>
/// Repository for AuthAuditLog records.
/// Uses AddAsync inherited from IRepository&lt;AuthAuditLog&gt;.
/// </summary>
public interface IAuthAuditLogRepository : IRepository<AuthAuditLog>
{
}
