using CampaignEngine.Application.Interfaces;
using CampaignEngine.Infrastructure.Dispatch;
using Moq;

namespace CampaignEngine.Infrastructure.Tests.Dispatch;

/// <summary>
/// Unit tests for EmailValidationService.
///
/// US-029 TASK-029-05: Email validation tests.
///
/// Covers:
///   - IsValid: valid and invalid email formats
///   - FilterValid: valid addresses are returned, invalid are skipped (not thrown)
///   - Edge cases: null, empty, whitespace, malformed
/// </summary>
public class EmailValidationServiceTests
{
    private readonly Mock<IAppLogger<EmailValidationService>> _loggerMock = new();
    private readonly EmailValidationService _sut;

    public EmailValidationServiceTests()
    {
        _sut = new EmailValidationService(_loggerMock.Object);
    }

    // ----------------------------------------------------------------
    // IsValid — valid addresses
    // ----------------------------------------------------------------

    [Theory]
    [InlineData("user@example.com")]
    [InlineData("user.name+tag@sub.domain.co.uk")]
    [InlineData("cc@company.org")]
    [InlineData("test@test.io")]
    [InlineData("First.Last@example.com")]
    public void IsValid_ValidAddress_ReturnsTrue(string email)
    {
        _sut.IsValid(email).Should().BeTrue();
    }

    // ----------------------------------------------------------------
    // IsValid — invalid addresses
    // ----------------------------------------------------------------

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("@nodomain")]          // starts with @
    [InlineData("two@@signs.com")]     // double @
    [InlineData("@broken")]            // missing local part
    [InlineData("test@")]              // missing domain
    [InlineData("@test")]              // missing local
    [InlineData("invalid@@double.com")]// double @
    public void IsValid_InvalidAddress_ReturnsFalse(string? email)
    {
        _sut.IsValid(email).Should().BeFalse();
    }

    // ----------------------------------------------------------------
    // FilterValid — returns only valid addresses
    // ----------------------------------------------------------------

    [Fact]
    public void FilterValid_AllValid_ReturnsAllAddresses()
    {
        var addresses = new[] { "a@example.com", "b@example.com", "c@example.com" };

        var result = _sut.FilterValid(addresses, "test");

        result.Should().HaveCount(3);
        result.Should().Contain("a@example.com");
        result.Should().Contain("b@example.com");
        result.Should().Contain("c@example.com");
    }

    [Fact]
    public void FilterValid_MixedValidInvalid_ReturnsOnlyValid()
    {
        // MimeKit rejects @-prefixed and double-@ addresses
        var addresses = new[] { "good@example.com", "@broken", "also-good@example.com", "" };

        var result = _sut.FilterValid(addresses, "StaticCC");

        result.Should().HaveCount(2);
        result.Should().Contain("good@example.com");
        result.Should().Contain("also-good@example.com");
        result.Should().NotContain("@broken");
    }

    [Fact]
    public void FilterValid_AllInvalid_ReturnsEmpty()
    {
        // MimeKit rejects addresses starting with @, double @@, missing domain
        var addresses = new[] { "@broken", "two@@signs.com", "test@" };

        var result = _sut.FilterValid(addresses, "StaticCC");

        result.Should().BeEmpty();
    }

    [Fact]
    public void FilterValid_EmptyCollection_ReturnsEmpty()
    {
        var result = _sut.FilterValid(Array.Empty<string>(), "test");

        result.Should().BeEmpty();
    }

    [Fact]
    public void FilterValid_WhitespaceEntries_AreSkipped()
    {
        var addresses = new[] { "  ", "valid@example.com", "" };

        var result = _sut.FilterValid(addresses, "test");

        result.Should().HaveCount(1);
        result.Should().Contain("valid@example.com");
    }

    [Fact]
    public void FilterValid_InvalidAddresses_AreLoggedAsWarning()
    {
        // Use a captured flag to verify logging — Moq params matching is tricky
        // so we use a callback setup to observe calls.
        var warningLogged = false;
        _loggerMock
            .Setup(l => l.LogWarning(It.IsAny<string>(), It.IsAny<object[]>()))
            .Callback(() => warningLogged = true);

        // @broken is rejected by MimeKit
        var addresses = new[] { "@broken", "valid@example.com" };

        _sut.FilterValid(addresses, "StaticCC");

        warningLogged.Should().BeTrue("a warning should be logged for invalid addresses");
    }

    [Fact]
    public void FilterValid_ValidAddresses_PreservesOriginalCasing()
    {
        var addresses = new[] { "User@Example.COM" };

        var result = _sut.FilterValid(addresses, "test");

        result.Should().HaveCount(1);
        result[0].Should().Be("User@Example.COM");
    }

    [Fact]
    public void FilterValid_NullCollection_ThrowsArgumentNullException()
    {
        var act = () => _sut.FilterValid(null!, "test");

        act.Should().Throw<ArgumentNullException>();
    }
}
