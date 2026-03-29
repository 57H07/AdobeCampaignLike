using CampaignEngine.Application.DTOs.Dispatch;
using CampaignEngine.Domain.Enums;

namespace CampaignEngine.Application.Tests.DTOs;

/// <summary>
/// Unit tests for DispatchRequest — US-022 (F-403b).
/// Covers: BinaryContent property presence, Content nullable, mutual exclusivity patterns.
/// </summary>
public class DispatchRequestTests
{
    // ----------------------------------------------------------------
    // TASK-022-01: BinaryContent property
    // ----------------------------------------------------------------

    [Fact]
    public void DispatchRequest_BinaryContent_DefaultsToNull()
    {
        var request = new DispatchRequest();

        request.BinaryContent.Should().BeNull();
    }

    [Fact]
    public void DispatchRequest_BinaryContent_CanBeSetToByteArray()
    {
        var docxBytes = new byte[] { 0x50, 0x4B, 0x03, 0x04 }; // DOCX magic bytes (ZIP header)

        var request = new DispatchRequest
        {
            Channel = ChannelType.Letter,
            BinaryContent = docxBytes
        };

        request.BinaryContent.Should().NotBeNull();
        request.BinaryContent.Should().BeSameAs(docxBytes);
        request.BinaryContent!.Length.Should().Be(4);
    }

    [Fact]
    public void DispatchRequest_Content_DefaultsToNull()
    {
        var request = new DispatchRequest();

        request.Content.Should().BeNull();
    }

    [Fact]
    public void DispatchRequest_Content_CanBeSetToString()
    {
        var htmlContent = "<html><body>Hello</body></html>";

        var request = new DispatchRequest
        {
            Channel = ChannelType.Email,
            Content = htmlContent
        };

        request.Content.Should().Be(htmlContent);
        request.BinaryContent.Should().BeNull();
    }

    // ----------------------------------------------------------------
    // Mutual exclusivity: Letter sets BinaryContent, Content null
    // ----------------------------------------------------------------

    [Fact]
    public void DispatchRequest_LetterChannel_BinaryContentSetContentNull()
    {
        var docxBytes = new byte[] { 1, 2, 3, 4 };

        var request = new DispatchRequest
        {
            Channel = ChannelType.Letter,
            BinaryContent = docxBytes,
            Content = null
        };

        request.BinaryContent.Should().NotBeNull();
        request.Content.Should().BeNull();
        request.Channel.Should().Be(ChannelType.Letter);
    }

    // ----------------------------------------------------------------
    // Mutual exclusivity: Email sets Content, BinaryContent null
    // ----------------------------------------------------------------

    [Fact]
    public void DispatchRequest_EmailChannel_ContentSetBinaryContentNull()
    {
        var request = new DispatchRequest
        {
            Channel = ChannelType.Email,
            Content = "<p>Hello</p>",
            BinaryContent = null
        };

        request.Content.Should().NotBeNullOrWhiteSpace();
        request.BinaryContent.Should().BeNull();
        request.Channel.Should().Be(ChannelType.Email);
    }

    // ----------------------------------------------------------------
    // Mutual exclusivity: SMS sets Content, BinaryContent null
    // ----------------------------------------------------------------

    [Fact]
    public void DispatchRequest_SmsChannel_ContentSetBinaryContentNull()
    {
        var request = new DispatchRequest
        {
            Channel = ChannelType.Sms,
            Content = "Your promo code is ABC123",
            BinaryContent = null
        };

        request.Content.Should().NotBeNullOrWhiteSpace();
        request.BinaryContent.Should().BeNull();
        request.Channel.Should().Be(ChannelType.Sms);
    }

    // ----------------------------------------------------------------
    // Business Rule 1: only one of the two should be non-null.
    // We verify that dispatchers can determine which to use via null checks.
    // ----------------------------------------------------------------

    [Fact]
    public void DispatchRequest_NullCheckDeterminesDispatchPath_BinaryContent()
    {
        var request = new DispatchRequest
        {
            Channel = ChannelType.Letter,
            BinaryContent = new byte[] { 0x01 }
        };

        // Dispatcher logic: check BinaryContent first (non-null = binary path)
        var useBinaryPath = request.BinaryContent is not null;
        var useContentPath = !useBinaryPath && request.Content is not null;

        useBinaryPath.Should().BeTrue();
        useContentPath.Should().BeFalse();
    }

    [Fact]
    public void DispatchRequest_NullCheckDeterminesDispatchPath_Content()
    {
        var request = new DispatchRequest
        {
            Channel = ChannelType.Email,
            Content = "<html>test</html>"
        };

        // Dispatcher logic: BinaryContent null → fall through to Content
        var useBinaryPath = request.BinaryContent is not null;
        var useContentPath = !useBinaryPath && request.Content is not null;

        useBinaryPath.Should().BeFalse();
        useContentPath.Should().BeTrue();
    }

    [Fact]
    public void DispatchRequest_BothNull_NeitherPathSelected()
    {
        var request = new DispatchRequest
        {
            Channel = ChannelType.Email
        };

        var useBinaryPath = request.BinaryContent is not null;
        var useContentPath = !useBinaryPath && request.Content is not null;

        useBinaryPath.Should().BeFalse();
        useContentPath.Should().BeFalse();
    }
}
