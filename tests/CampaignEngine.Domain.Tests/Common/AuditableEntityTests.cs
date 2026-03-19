using CampaignEngine.Domain.Common;

namespace CampaignEngine.Domain.Tests.Common;

// Concrete implementation for testing purposes only
file sealed class TestEntity : AuditableEntity { }

public class AuditableEntityTests
{
    [Fact]
    public void NewAuditableEntity_ShouldHaveNonEmptyId()
    {
        var entity = new TestEntity();
        entity.Id.Should().NotBeEmpty();
    }

    [Fact]
    public void NewAuditableEntity_ShouldSetCreatedAtToUtcNow()
    {
        var before = DateTime.UtcNow.AddSeconds(-1);
        var entity = new TestEntity();
        var after = DateTime.UtcNow.AddSeconds(1);

        entity.CreatedAt.Should().BeAfter(before).And.BeBefore(after);
    }

    [Fact]
    public void TwoNewEntities_ShouldHaveDifferentIds()
    {
        var entity1 = new TestEntity();
        var entity2 = new TestEntity();

        entity1.Id.Should().NotBe(entity2.Id);
    }
}
