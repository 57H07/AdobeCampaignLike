using CampaignEngine.Application.Interfaces.Repositories;
using CampaignEngine.Domain.Entities;

namespace CampaignEngine.Infrastructure.Persistence.Repositories;

/// <summary>
/// EF Core implementation of IAuthAuditLogRepository.
/// Uses AddAsync inherited from RepositoryBase&lt;AuthAuditLog&gt;.
/// </summary>
public sealed class AuthAuditLogRepository
    : RepositoryBase<AuthAuditLog>, IAuthAuditLogRepository
{
    public AuthAuditLogRepository(CampaignEngineDbContext dbContext) : base(dbContext) { }
}
