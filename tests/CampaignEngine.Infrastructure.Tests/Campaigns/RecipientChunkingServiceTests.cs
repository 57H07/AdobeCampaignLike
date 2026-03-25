using CampaignEngine.Application.Interfaces;
using CampaignEngine.Infrastructure.Campaigns;
using CampaignEngine.Infrastructure.Configuration;
using Microsoft.Extensions.Options;

namespace CampaignEngine.Infrastructure.Tests.Campaigns;

/// <summary>
/// Integration tests for RecipientChunkingService (TASK-026-09).
/// Validates the chunking algorithm: split sizes, edge cases, metadata correctness.
/// </summary>
public class RecipientChunkingServiceTests
{
    private static IOptions<CampaignEngineOptions> DefaultOptions(int chunkSize = 500) =>
        Options.Create(new CampaignEngineOptions
        {
            BatchProcessing = new BatchProcessingOptions
            {
                ChunkSize = chunkSize,
                WorkerCount = 8,
                MaxRetryAttempts = 3
            }
        });

    private static IReadOnlyList<IDictionary<string, object?>> BuildRecipients(int count) =>
        Enumerable.Range(0, count)
            .Select(i => (IDictionary<string, object?>)new Dictionary<string, object?>
            {
                ["email"] = $"user{i}@test.com",
                ["name"] = $"User {i}"
            })
            .ToList();

    // ------------------------------------------------------------------
    // Empty list
    // ------------------------------------------------------------------

    [Fact(DisplayName = "Empty recipient list returns single empty chunk")]
    public void Split_EmptyList_ReturnsSingleEmptyChunk()
    {
        var sut = new RecipientChunkingService(DefaultOptions());

        var result = sut.Split([]);

        result.Should().HaveCount(1);
        result[0].ChunkIndex.Should().Be(0);
        result[0].TotalChunks.Should().Be(1);
        result[0].Recipients.Should().BeEmpty();
    }

    // ------------------------------------------------------------------
    // Exact multiple of chunk size
    // ------------------------------------------------------------------

    [Fact(DisplayName = "1000 recipients with chunk 500 produces exactly 2 chunks of 500")]
    public void Split_ExactMultiple_ProducesEvenChunks()
    {
        var sut = new RecipientChunkingService(DefaultOptions(500));
        var recipients = BuildRecipients(1000);

        var result = sut.Split(recipients);

        result.Should().HaveCount(2);
        result[0].Recipients.Should().HaveCount(500);
        result[1].Recipients.Should().HaveCount(500);
    }

    // ------------------------------------------------------------------
    // Non-multiple — last chunk is smaller
    // ------------------------------------------------------------------

    [Theory(DisplayName = "Non-multiple remainder fills last chunk")]
    [InlineData(501, 500, 2)]   // 500 + 1
    [InlineData(1001, 500, 3)]  // 500 + 500 + 1
    [InlineData(7, 3, 3)]       // 3 + 3 + 1
    public void Split_NonMultiple_LastChunkContainsRemainder(
        int totalRecipients, int chunkSize, int expectedChunkCount)
    {
        var sut = new RecipientChunkingService(DefaultOptions(chunkSize));
        var recipients = BuildRecipients(totalRecipients);

        var result = sut.Split(recipients, chunkSize);

        result.Should().HaveCount(expectedChunkCount);

        // All chunks except last should have chunkSize recipients
        for (var i = 0; i < result.Count - 1; i++)
            result[i].Recipients.Should().HaveCount(chunkSize);

        // Last chunk has the remainder
        var expectedRemainder = totalRecipients % chunkSize;
        if (expectedRemainder == 0) expectedRemainder = chunkSize;
        result[^1].Recipients.Should().HaveCount(expectedRemainder);
    }

    // ------------------------------------------------------------------
    // ChunkIndex and TotalChunks metadata
    // ------------------------------------------------------------------

    [Fact(DisplayName = "ChunkIndex is sequential zero-based and TotalChunks is consistent")]
    public void Split_Metadata_ChunkIndexIsZeroBasedAndTotalConsistent()
    {
        var sut = new RecipientChunkingService(DefaultOptions(100));
        var recipients = BuildRecipients(350);

        var result = sut.Split(recipients, 100);

        result.Should().HaveCount(4);
        for (var i = 0; i < result.Count; i++)
        {
            result[i].ChunkIndex.Should().Be(i);
            result[i].TotalChunks.Should().Be(4);
        }
    }

    // ------------------------------------------------------------------
    // Single recipient
    // ------------------------------------------------------------------

    [Fact(DisplayName = "Single recipient produces one chunk of 1")]
    public void Split_SingleRecipient_ProducesSingleChunk()
    {
        var sut = new RecipientChunkingService(DefaultOptions(500));
        var recipients = BuildRecipients(1);

        var result = sut.Split(recipients);

        result.Should().HaveCount(1);
        result[0].Recipients.Should().HaveCount(1);
        result[0].ChunkIndex.Should().Be(0);
        result[0].TotalChunks.Should().Be(1);
    }

    // ------------------------------------------------------------------
    // Custom chunk size override
    // ------------------------------------------------------------------

    [Fact(DisplayName = "Explicit chunkSize overrides configured default")]
    public void Split_ExplicitChunkSize_OverridesDefault()
    {
        var sut = new RecipientChunkingService(DefaultOptions(500));
        var recipients = BuildRecipients(100);

        var result = sut.Split(recipients, chunkSize: 10);

        result.Should().HaveCount(10);
        result.All(c => c.Recipients.Count == 10).Should().BeTrue();
    }

    // ------------------------------------------------------------------
    // Chunk size clamping
    // ------------------------------------------------------------------

    [Fact(DisplayName = "ChunkSize below 1 is clamped to 1")]
    public void Split_ChunkSizeBelowMinimum_IsClampedTo1()
    {
        var sut = new RecipientChunkingService(DefaultOptions(500));
        var recipients = BuildRecipients(5);

        var result = sut.Split(recipients, chunkSize: -10);

        // Clamped to 1 means each recipient is its own chunk
        result.Should().HaveCount(5);
        result.All(c => c.Recipients.Count == 1).Should().BeTrue();
    }

    [Fact(DisplayName = "ChunkSize above 10000 is clamped to 10000")]
    public void Split_ChunkSizeAboveMaximum_IsClampedTo10000()
    {
        var sut = new RecipientChunkingService(DefaultOptions(500));
        var recipients = BuildRecipients(9999);

        var result = sut.Split(recipients, chunkSize: 99999);

        // All recipients fit in one chunk when clamped to 10000
        result.Should().HaveCount(1);
        result[0].Recipients.Should().HaveCount(9999);
    }

    // ------------------------------------------------------------------
    // Null guard
    // ------------------------------------------------------------------

    [Fact(DisplayName = "Null recipients argument throws ArgumentNullException")]
    public void Split_NullRecipients_ThrowsArgumentNullException()
    {
        var sut = new RecipientChunkingService(DefaultOptions(500));

        var act = () => sut.Split(null!);

        act.Should().Throw<ArgumentNullException>();
    }

    // ------------------------------------------------------------------
    // Large dataset — all recipients accounted for
    // ------------------------------------------------------------------

    [Fact(DisplayName = "100K recipients are fully partitioned with no recipient lost")]
    public void Split_100KRecipients_AllRecipientsCovered()
    {
        const int total = 100_000;
        const int chunkSize = 500;

        var sut = new RecipientChunkingService(DefaultOptions(chunkSize));
        var recipients = BuildRecipients(total);

        var result = sut.Split(recipients, chunkSize);

        var totalRecipientsCounted = result.Sum(c => c.Recipients.Count);
        totalRecipientsCounted.Should().Be(total);
        result.Should().HaveCount(total / chunkSize);
    }
}
