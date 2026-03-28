using CampaignEngine.Application.Interfaces;
using CampaignEngine.Infrastructure.Dispatch;
using Moq;

namespace CampaignEngine.Infrastructure.Tests.Dispatch;

/// <summary>
/// Unit tests for CcResolutionService.
///
/// US-029 TASK-029-06: Deduplication tests.
///
/// Covers:
///   - ResolveCc: static CC, dynamic CC, combined static + dynamic
///   - Deduplication: case-insensitive, preserves first occurrence
///   - Max 10 CC recipients cap
///   - ResolveBcc: static BCC validation and deduplication
///   - Edge cases: null inputs, empty data, invalid addresses
/// </summary>
public class CcResolutionServiceTests
{
    private readonly Mock<IEmailValidationService> _validationMock;
    private readonly Mock<IAppLogger<CcResolutionService>> _loggerMock;
    private readonly CcResolutionService _sut;

    public CcResolutionServiceTests()
    {
        _validationMock = new Mock<IEmailValidationService>();
        _loggerMock = new Mock<IAppLogger<CcResolutionService>>();

        // Default: FilterValid returns the inputs as-is (all valid)
        _validationMock
            .Setup(v => v.FilterValid(It.IsAny<IEnumerable<string>>(), It.IsAny<string>()))
            .Returns<IEnumerable<string>, string>((addresses, _) => addresses.ToList());

        _sut = new CcResolutionService(_validationMock.Object, _loggerMock.Object);
    }

    // ----------------------------------------------------------------
    // ResolveCc — static CC
    // ----------------------------------------------------------------

    [Fact]
    public void ResolveCc_WithStaticCcOnly_ReturnsParsedAddresses()
    {
        var staticCc = "a@example.com, b@example.com";
        var recipient = EmptyRecipient();

        var result = _sut.ResolveCc(staticCc, null, recipient);

        result.Should().HaveCount(2);
        result.Should().Contain("a@example.com");
        result.Should().Contain("b@example.com");
    }

    [Fact]
    public void ResolveCc_NullStaticCc_ReturnsEmpty()
    {
        var result = _sut.ResolveCc(null, null, EmptyRecipient());

        result.Should().BeEmpty();
    }

    [Fact]
    public void ResolveCc_EmptyStaticCc_ReturnsEmpty()
    {
        var result = _sut.ResolveCc("", null, EmptyRecipient());

        result.Should().BeEmpty();
    }

    // ----------------------------------------------------------------
    // ResolveCc — dynamic CC
    // ----------------------------------------------------------------

    [Fact]
    public void ResolveCc_WithDynamicCcField_ExtractsFromRecipientData()
    {
        var recipient = new Dictionary<string, object?>
        {
            ["cc_email"] = "dynamic@example.com"
        };

        var result = _sut.ResolveCc(null, "cc_email", recipient);

        result.Should().HaveCount(1);
        result.Should().Contain("dynamic@example.com");
    }

    [Fact]
    public void ResolveCc_DynamicCcFieldWithSemicolonSeparated_ParsesMultiple()
    {
        var recipient = new Dictionary<string, object?>
        {
            ["cc_email"] = "a@example.com;b@example.com;c@example.com"
        };

        var result = _sut.ResolveCc(null, "cc_email", recipient);

        result.Should().HaveCount(3);
        result.Should().Contain("a@example.com");
        result.Should().Contain("b@example.com");
        result.Should().Contain("c@example.com");
    }

    [Fact]
    public void ResolveCc_DynamicCcFieldWithCommaSeparated_ParsesMultiple()
    {
        var recipient = new Dictionary<string, object?>
        {
            ["cc_email"] = "a@example.com,b@example.com"
        };

        var result = _sut.ResolveCc(null, "cc_email", recipient);

        result.Should().HaveCount(2);
    }

    [Fact]
    public void ResolveCc_DynamicCcFieldMissingFromRecipient_ReturnsOnlyStatic()
    {
        var recipient = EmptyRecipient();
        var staticCc = "static@example.com";

        var result = _sut.ResolveCc(staticCc, "cc_email", recipient);

        result.Should().HaveCount(1);
        result.Should().Contain("static@example.com");
    }

    [Fact]
    public void ResolveCc_DynamicCcFieldIsNull_ReturnsOnlyStatic()
    {
        var recipient = new Dictionary<string, object?> { ["cc_email"] = null };

        var result = _sut.ResolveCc("static@example.com", "cc_email", recipient);

        result.Should().HaveCount(1);
        result.Should().Contain("static@example.com");
    }

    // ----------------------------------------------------------------
    // ResolveCc — combined static + dynamic
    // ----------------------------------------------------------------

    [Fact]
    public void ResolveCc_StaticAndDynamic_CombinesBoth()
    {
        var recipient = new Dictionary<string, object?>
        {
            ["cc_field"] = "dynamic@example.com"
        };

        var result = _sut.ResolveCc("static@example.com", "cc_field", recipient);

        result.Should().HaveCount(2);
        result.Should().Contain("static@example.com");
        result.Should().Contain("dynamic@example.com");
    }

    // ----------------------------------------------------------------
    // Deduplication (BR-5)
    // ----------------------------------------------------------------

    [Fact]
    public void ResolveCc_DuplicateAddresses_AreDeduplicatedCaseInsensitive()
    {
        var recipient = new Dictionary<string, object?>
        {
            ["cc_field"] = "CC@EXAMPLE.COM"  // same as static but different case
        };

        var result = _sut.ResolveCc("cc@example.com", "cc_field", recipient);

        // Both map to the same address — deduplication should keep only one
        result.Should().HaveCount(1);
        result[0].Should().Be("cc@example.com");  // first occurrence preserved
    }

    [Fact]
    public void ResolveCc_StaticWithDuplicates_Deduplicates()
    {
        var staticCc = "a@example.com, A@EXAMPLE.COM, b@example.com";

        var result = _sut.ResolveCc(staticCc, null, EmptyRecipient());

        result.Should().HaveCount(2);
        result.Should().ContainSingle(a => a.Equals("a@example.com", StringComparison.OrdinalIgnoreCase));
        result.Should().Contain("b@example.com");
    }

    // ----------------------------------------------------------------
    // Max 10 CC recipients cap (BR-4)
    // ----------------------------------------------------------------

    [Fact]
    public void ResolveCc_ExceedsMaxCap_TruncatesTo10()
    {
        // Build 15 distinct valid addresses
        var addresses = Enumerable.Range(1, 15)
            .Select(i => $"recipient{i}@example.com")
            .ToArray();

        var staticCc = string.Join(", ", addresses);

        var result = _sut.ResolveCc(staticCc, null, EmptyRecipient());

        result.Should().HaveCount(10);
    }

    [Fact]
    public void ResolveCc_ExactlyMaxCap_DoesNotTruncate()
    {
        var addresses = Enumerable.Range(1, 10)
            .Select(i => $"r{i}@example.com")
            .ToArray();

        var staticCc = string.Join(", ", addresses);

        var result = _sut.ResolveCc(staticCc, null, EmptyRecipient());

        result.Should().HaveCount(10);
    }

    [Fact]
    public void ResolveCc_ExceedsCapAfterDedup_LogsWarning()
    {
        // Set up 11 addresses (after dedup still > 10)
        var addresses = Enumerable.Range(1, 11)
            .Select(i => $"r{i}@example.com")
            .ToArray();

        var staticCc = string.Join(", ", addresses);

        _sut.ResolveCc(staticCc, null, EmptyRecipient());

        _loggerMock.Verify(
            l => l.LogWarning(It.IsAny<string>(), It.IsAny<object[]>()),
            Times.AtLeastOnce);
    }

    // ----------------------------------------------------------------
    // ResolveBcc
    // ----------------------------------------------------------------

    [Fact]
    public void ResolveBcc_WithStaticBcc_ReturnsAddresses()
    {
        var result = _sut.ResolveBcc("bcc@example.com");

        result.Should().HaveCount(1);
        result.Should().Contain("bcc@example.com");
    }

    [Fact]
    public void ResolveBcc_NullInput_ReturnsEmpty()
    {
        var result = _sut.ResolveBcc(null);

        result.Should().BeEmpty();
    }

    [Fact]
    public void ResolveBcc_EmptyInput_ReturnsEmpty()
    {
        var result = _sut.ResolveBcc("");

        result.Should().BeEmpty();
    }

    [Fact]
    public void ResolveBcc_WithDuplicates_Deduplicates()
    {
        var bcc = "audit@example.com, AUDIT@EXAMPLE.COM";

        var result = _sut.ResolveBcc(bcc);

        result.Should().HaveCount(1);
    }

    [Fact]
    public void ResolveBcc_MultipleBcc_ReturnsAll()
    {
        var bcc = "a@example.com, b@example.com, c@example.com";

        var result = _sut.ResolveBcc(bcc);

        result.Should().HaveCount(3);
    }

    // ----------------------------------------------------------------
    // Null guard
    // ----------------------------------------------------------------

    [Fact]
    public void ResolveCc_NullRecipientData_ThrowsArgumentNullException()
    {
        var act = () => _sut.ResolveCc("static@example.com", null, null!);

        act.Should().Throw<ArgumentNullException>();
    }

    // ----------------------------------------------------------------
    // Helpers
    // ----------------------------------------------------------------

    private static IDictionary<string, object?> EmptyRecipient()
        => new Dictionary<string, object?>();
}
