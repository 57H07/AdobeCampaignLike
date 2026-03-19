using CampaignEngine.Infrastructure.Logging;

namespace CampaignEngine.Infrastructure.Tests.Logging;

public class PiiMaskerTests
{
    // ----------------------------------------------------------------
    // MaskEmail
    // ----------------------------------------------------------------

    [Theory]
    [InlineData("john.doe@example.com", "j***@example.com")]
    [InlineData("a@example.com", "***@example.com")]
    [InlineData("info@company.org", "i***@company.org")]
    public void MaskEmail_ShouldMaskLocalPart(string input, string expected)
    {
        var result = PiiMasker.MaskEmail(input);
        result.Should().Be(expected);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void MaskEmail_NullOrEmpty_ShouldReturnEmptyPlaceholder(string? input)
    {
        var result = PiiMasker.MaskEmail(input);
        result.Should().Be("[empty]");
    }

    [Fact]
    public void MaskEmail_InvalidFormat_ShouldReturnMaskedFallback()
    {
        var result = PiiMasker.MaskEmail("notanemailaddress");
        result.Should().Be("***@[masked]");
    }

    // ----------------------------------------------------------------
    // MaskPhone
    // ----------------------------------------------------------------

    [Theory]
    [InlineData("+33 6 12 34 56 78", "***5678")]
    [InlineData("0612345678", "***5678")]
    [InlineData("+1 (555) 867-5309", "***5309")]
    public void MaskPhone_ShouldKeepLastFourDigits(string input, string expected)
    {
        var result = PiiMasker.MaskPhone(input);
        result.Should().Be(expected);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void MaskPhone_NullOrEmpty_ShouldReturnEmptyPlaceholder(string? input)
    {
        var result = PiiMasker.MaskPhone(input);
        result.Should().Be("[empty]");
    }

    [Fact]
    public void MaskPhone_ShortNumber_ShouldReturnMasked()
    {
        // Only 3 digits — cannot show last 4
        var result = PiiMasker.MaskPhone("123");
        result.Should().Be("***");
    }

    // ----------------------------------------------------------------
    // MaskEmailsInText
    // ----------------------------------------------------------------

    [Fact]
    public void MaskEmailsInText_ShouldMaskAllEmailAddresses()
    {
        var text = "Send to john@example.com and jane.doe@corp.net for confirmation.";
        var result = PiiMasker.MaskEmailsInText(text);

        result.Should().NotContain("john@example.com");
        result.Should().NotContain("jane.doe@corp.net");
        result.Should().Contain("j***@example.com");
        result.Should().Contain("j***@corp.net");
    }

    [Fact]
    public void MaskEmailsInText_NoEmail_ShouldReturnUnchanged()
    {
        var text = "No email addresses here.";
        var result = PiiMasker.MaskEmailsInText(text);
        result.Should().Be(text);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    public void MaskEmailsInText_NullOrEmpty_ShouldReturnEmpty(string? input)
    {
        var result = PiiMasker.MaskEmailsInText(input);
        result.Should().Be(string.Empty);
    }

    // ----------------------------------------------------------------
    // SafeId
    // ----------------------------------------------------------------

    [Fact]
    public void SafeId_ShouldReturn8CharHexPrefix()
    {
        var id = Guid.NewGuid();
        var result = PiiMasker.SafeId(id);

        result.Should().HaveLength(8);
        result.Should().MatchRegex("^[0-9a-f]{8}$");
    }

    [Fact]
    public void SafeId_SameGuid_ShouldReturnSameResult()
    {
        var id = Guid.Parse("12345678-1234-1234-1234-123456789012");
        var result1 = PiiMasker.SafeId(id);
        var result2 = PiiMasker.SafeId(id);

        result1.Should().Be(result2);
        result1.Should().Be("12345678");
    }
}
