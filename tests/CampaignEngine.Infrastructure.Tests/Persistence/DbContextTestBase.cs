using CampaignEngine.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace CampaignEngine.Infrastructure.Tests.Persistence;

/// <summary>
/// Base class for DbContext integration tests using the EF Core in-memory provider.
/// Each test gets a fresh isolated database instance.
/// </summary>
public abstract class DbContextTestBase : IDisposable
{
    protected readonly CampaignEngineDbContext Context;

    protected DbContextTestBase()
    {
        var options = new DbContextOptionsBuilder<CampaignEngineDbContext>()
            .UseInMemoryDatabase(databaseName: Guid.NewGuid().ToString())
            .Options;

        Context = new CampaignEngineDbContext(options);
    }

    public void Dispose()
    {
        Context.Dispose();
        GC.SuppressFinalize(this);
    }
}
