using CampaignEngine.Application.DTOs.Dispatch;
using CampaignEngine.Application.Interfaces;
using CampaignEngine.Domain.Enums;
using CampaignEngine.Domain.Exceptions;
using CampaignEngine.Infrastructure.Configuration;
using MailKit.Net.Smtp;
using MailKit.Security;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using MimeKit;
using MimeKit.Text;

namespace CampaignEngine.Infrastructure.Dispatch;

/// <summary>
/// IChannelDispatcher implementation for the Email channel using MailKit.
///
/// TASK-019-01: EmailDispatcher with MailKit.
/// TASK-019-03: Attachment handling from file paths (whitelist: PDF, DOCX, XLSX, PNG, JPG).
/// TASK-019-04: CC/BCC recipient list handling.
/// TASK-019-05: SMTP error categorization (transient vs permanent).
///
/// Business rules:
///   1. From address configurable per environment (SmtpOptions.FromAddress).
///   2. Reply-To address optional per campaign (DispatchRequest.ReplyToAddress overrides SmtpOptions.ReplyToAddress).
///   3. Attachment whitelist: PDF, DOCX, XLSX, PNG, JPG.
///   4. Attachment size limits: 10 MB per file, 25 MB total.
///
/// The class is not sealed to allow test subclasses to override SendViaSmtpAsync
/// for unit-testing message construction without a live SMTP server.
/// </summary>
public class EmailDispatcher : IChannelDispatcher
{
    private readonly SmtpOptions _smtpOptions;
    private readonly ILogger<EmailDispatcher> _logger;

    // Allowed attachment file extensions (business rule 3)
    private static readonly HashSet<string> AllowedExtensions =
        new(StringComparer.OrdinalIgnoreCase)
        {
            ".pdf", ".docx", ".xlsx", ".png", ".jpg", ".jpeg"
        };

    // Transient SMTP status codes (4xx responses indicate temporary failures)
    // 421: Service not available
    // 450: Requested mail action not taken — mailbox unavailable
    // 451: Requested action aborted — local error
    // 452: Requested action not taken — insufficient system storage
    private static readonly HashSet<int> TransientSmtpCodes = [421, 450, 451, 452];

    public ChannelType Channel => ChannelType.Email;

    public EmailDispatcher(
        IOptions<SmtpOptions> smtpOptions,
        ILogger<EmailDispatcher> logger)
    {
        _smtpOptions = smtpOptions.Value;
        _logger = logger;
    }

    /// <summary>
    /// Sends an HTML email to the specified recipient via the configured SMTP server.
    /// Supports CC, BCC, Reply-To, and multiple attachments.
    /// </summary>
    public async Task<DispatchResult> SendAsync(
        DispatchRequest request,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(request.Recipient.Email))
        {
            return DispatchResult.Fail(
                "Recipient email address is required for the Email channel.",
                isTransient: false);
        }

        try
        {
            var message = BuildMimeMessage(request);
            await SendViaSmtpAsync(message, cancellationToken);

            _logger.LogInformation(
                "Email sent successfully to {Recipient} for campaign {CampaignId}",
                request.Recipient.Email, request.CampaignId);

            return DispatchResult.Ok(messageId: message.MessageId);
        }
        catch (AttachmentValidationException ex)
        {
            _logger.LogWarning(
                "Attachment validation failed for {FileName}: {Message}",
                ex.AttachmentFileName, ex.Message);
            return DispatchResult.Fail(ex.Message, isTransient: false);
        }
        catch (SmtpDispatchException ex)
        {
            _logger.LogError(ex,
                "SMTP dispatch failed (transient={IsTransient}, code={SmtpCode}) for {Recipient}",
                ex.IsTransient, ex.SmtpStatusCode, request.Recipient.Email);
            return DispatchResult.Fail(ex.Message, isTransient: ex.IsTransient);
        }
        catch (OperationCanceledException)
        {
            _logger.LogWarning("Email dispatch cancelled for {Recipient}", request.Recipient.Email);
            return DispatchResult.Fail("Send operation was cancelled.", isTransient: true);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Unexpected error sending email to {Recipient}", request.Recipient.Email);
            return DispatchResult.Fail(
                $"Unexpected error: {ex.Message}",
                isTransient: false);
        }
    }

    // ----------------------------------------------------------------
    // Message construction
    // ----------------------------------------------------------------

    /// <summary>
    /// Builds a MimeMessage from the dispatch request.
    /// Populates: From, To, CC, BCC, Reply-To, Subject, HTML body, attachments.
    /// </summary>
    private MimeMessage BuildMimeMessage(DispatchRequest request)
    {
        var message = new MimeMessage();

        // From (business rule 1: configurable per environment)
        message.From.Add(new MailboxAddress(_smtpOptions.FromName, _smtpOptions.FromAddress));

        // To
        message.To.Add(BuildMailboxAddress(
            request.Recipient.DisplayName,
            request.Recipient.Email!));

        // CC (TASK-019-04)
        foreach (var cc in request.CcAddresses)
        {
            if (IsValidEmail(cc))
                message.Cc.Add(MailboxAddress.Parse(cc));
            else
                _logger.LogWarning("Skipping invalid CC address: {Address}", cc);
        }

        // BCC (TASK-019-04)
        foreach (var bcc in request.BccAddresses)
        {
            if (IsValidEmail(bcc))
                message.Bcc.Add(MailboxAddress.Parse(bcc));
            else
                _logger.LogWarning("Skipping invalid BCC address: {Address}", bcc);
        }

        // Reply-To (business rule 2: optional per campaign, overrides SMTP default)
        var replyTo = request.ReplyToAddress ?? _smtpOptions.ReplyToAddress;
        if (!string.IsNullOrWhiteSpace(replyTo) && IsValidEmail(replyTo))
        {
            message.ReplyTo.Add(MailboxAddress.Parse(replyTo));
        }

        // Subject
        message.Subject = request.Subject ?? "(No Subject)";

        // Body + Attachments
        message.Body = BuildBody(request);

        return message;
    }

    /// <summary>
    /// Builds the MIME body part. When attachments are present, creates a multipart/mixed message.
    /// </summary>
    private static MimePart BuildHtmlPart(string htmlContent)
    {
        return new TextPart(TextFormat.Html)
        {
            Text = htmlContent
        };
    }

    private MimeEntity BuildBody(DispatchRequest request)
    {
        var htmlPart = BuildHtmlPart(request.Content ?? string.Empty);

        if (request.Attachments.Count == 0)
        {
            return htmlPart;
        }

        // Validate and load attachments (TASK-019-03)
        ValidateAttachments(request.Attachments);

        var multipart = new Multipart("mixed");
        multipart.Add(htmlPart);

        foreach (var attachment in request.Attachments)
        {
            multipart.Add(BuildAttachmentPart(attachment));
        }

        return multipart;
    }

    // ----------------------------------------------------------------
    // Attachment handling (TASK-019-03)
    // ----------------------------------------------------------------

    /// <summary>
    /// Validates all attachments against the whitelist and size limits.
    /// Throws AttachmentValidationException for any violation.
    /// Business rules 3 and 4.
    /// </summary>
    private void ValidateAttachments(List<AttachmentInfo> attachments)
    {
        long totalSize = 0;

        foreach (var attachment in attachments)
        {
            // Resolve file content from path if not already loaded
            if (attachment.Data.Length == 0 && !string.IsNullOrWhiteSpace(attachment.FilePath))
            {
                if (!File.Exists(attachment.FilePath))
                {
                    throw new AttachmentValidationException(
                        $"Attachment file not found: '{attachment.FilePath}'",
                        attachment.FileName);
                }
                attachment.Data = File.ReadAllBytes(attachment.FilePath);
            }

            // Extension whitelist (business rule 3)
            var ext = Path.GetExtension(attachment.FileName);
            if (!AllowedExtensions.Contains(ext))
            {
                throw new AttachmentValidationException(
                    $"Attachment '{attachment.FileName}' has disallowed extension '{ext}'. " +
                    $"Allowed: {string.Join(", ", AllowedExtensions)}",
                    attachment.FileName);
            }

            // Per-file size limit (business rule 4: 10 MB)
            var maxFileSize = _smtpOptions.MaxAttachmentFileSizeBytes;
            if (attachment.Data.Length > maxFileSize)
            {
                throw new AttachmentValidationException(
                    $"Attachment '{attachment.FileName}' exceeds the maximum file size of {maxFileSize / (1024 * 1024)} MB " +
                    $"(actual: {attachment.Data.Length / (1024 * 1024.0):F1} MB).",
                    attachment.FileName);
            }

            totalSize += attachment.Data.Length;
        }

        // Total size limit (business rule 4: 25 MB)
        var maxTotal = _smtpOptions.MaxAttachmentTotalSizeBytes;
        if (totalSize > maxTotal)
        {
            throw new AttachmentValidationException(
                $"Total attachment size {totalSize / (1024 * 1024.0):F1} MB exceeds the maximum of {maxTotal / (1024 * 1024)} MB.",
                "(all attachments)");
        }
    }

    /// <summary>
    /// Builds a MIME attachment part from an AttachmentInfo.
    /// </summary>
    private static MimePart BuildAttachmentPart(AttachmentInfo attachment)
    {
        var mimeType = string.IsNullOrWhiteSpace(attachment.MimeType)
            ? AttachmentInfo.GetMimeType(Path.GetExtension(attachment.FileName))
            : attachment.MimeType;

        // Parse content type
        ContentType.TryParse(mimeType, out var contentType);
        contentType ??= new ContentType("application", "octet-stream");

        var part = new MimePart(contentType)
        {
            Content = new MimeContent(new MemoryStream(attachment.Data)),
            ContentDisposition = new ContentDisposition(ContentDisposition.Attachment),
            ContentTransferEncoding = ContentEncoding.Base64,
            FileName = attachment.FileName
        };

        return part;
    }

    // ----------------------------------------------------------------
    // SMTP transport (TASK-019-01)
    // Protected virtual to allow test overrides without live SMTP server.
    // ----------------------------------------------------------------

    /// <summary>
    /// Connects to the SMTP server and sends the message.
    /// TASK-019-05: Categorizes exceptions as transient or permanent.
    /// Virtual to allow test subclasses to override SMTP transport.
    /// </summary>
    protected virtual async Task SendViaSmtpAsync(MimeMessage message, CancellationToken cancellationToken)
    {
        using var client = new SmtpClient();
        client.Timeout = _smtpOptions.TimeoutSeconds * 1000;

        try
        {
            var secureSocketOptions = _smtpOptions.UseSsl
                ? SecureSocketOptions.StartTls
                : SecureSocketOptions.None;

            await client.ConnectAsync(
                _smtpOptions.Host,
                _smtpOptions.Port,
                secureSocketOptions,
                cancellationToken);

            // Authenticate if credentials are provided
            if (!string.IsNullOrWhiteSpace(_smtpOptions.UserName))
            {
                await client.AuthenticateAsync(
                    _smtpOptions.UserName,
                    _smtpOptions.Password,
                    cancellationToken);
            }

            await client.SendAsync(message, cancellationToken);
        }
        catch (SmtpCommandException ex)
        {
            // TASK-019-05: Categorize SMTP command errors
            var statusCode = (int)ex.StatusCode;
            var isTransient = IsTransientSmtpCode(statusCode);

            throw new SmtpDispatchException(
                $"SMTP command error ({statusCode}): {ex.Message}",
                isTransient: isTransient,
                innerException: ex,
                smtpStatusCode: statusCode);
        }
        catch (SmtpProtocolException ex)
        {
            // Protocol errors are generally transient (connection issues)
            throw new SmtpDispatchException(
                $"SMTP protocol error: {ex.Message}",
                isTransient: true,
                innerException: ex);
        }
        catch (MailKit.Security.AuthenticationException ex)
        {
            // Authentication failures are permanent (wrong credentials)
            throw new SmtpDispatchException(
                $"SMTP authentication failed: {ex.Message}",
                isTransient: false,
                innerException: ex);
        }
        catch (System.Net.Sockets.SocketException ex)
        {
            // Network errors are transient
            throw new SmtpDispatchException(
                $"SMTP connection error: {ex.Message}",
                isTransient: true,
                innerException: ex);
        }
        catch (TimeoutException ex)
        {
            // Timeouts are transient
            throw new SmtpDispatchException(
                $"SMTP connection timed out: {ex.Message}",
                isTransient: true,
                innerException: ex);
        }
        finally
        {
            if (client.IsConnected)
            {
                await client.DisconnectAsync(quit: true, cancellationToken: CancellationToken.None);
            }
        }
    }

    // ----------------------------------------------------------------
    // Helper methods
    // ----------------------------------------------------------------

    /// <summary>
    /// Determines whether an SMTP response code indicates a transient failure.
    /// 4xx codes = transient (retry). 5xx codes = permanent (don't retry).
    /// </summary>
    private static bool IsTransientSmtpCode(int code)
    {
        // 4xx response codes indicate temporary failures
        return code is >= 400 and < 500
               || TransientSmtpCodes.Contains(code);
    }

    private static MailboxAddress BuildMailboxAddress(string? displayName, string email)
    {
        return string.IsNullOrWhiteSpace(displayName)
            ? new MailboxAddress(email, email)
            : new MailboxAddress(displayName, email);
    }

    /// <summary>
    /// Validates an email address using MailboxAddress.TryParse.
    /// Invalid addresses are logged and skipped rather than failing the send.
    /// </summary>
    private static bool IsValidEmail(string address)
    {
        return MailboxAddress.TryParse(address, out _);
    }
}
