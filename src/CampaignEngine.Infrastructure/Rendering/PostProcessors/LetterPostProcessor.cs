using CampaignEngine.Application.Interfaces;
using CampaignEngine.Application.Models;
using CampaignEngine.Domain.Enums;
using CampaignEngine.Domain.Exceptions;
using DinkToPdf;
using DinkToPdf.Contracts;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace CampaignEngine.Infrastructure.Rendering.PostProcessors;

/// <summary>
/// Post-processor for the Letter channel.
/// Converts rendered HTML to a PDF document using DinkToPdf (libwkhtmltox wrapper).
///
/// POC Decision (TASK-013-01): DinkToPdf was selected over alternatives because:
/// - In-process conversion (no spawning of external wkhtmltopdf process)
/// - Faithful HTML/CSS rendering (WebKit engine)
/// - Windows IIS compatible via libwkhtmltox.dll native library
/// - Supports A4 format, embedded fonts, print CSS
///
/// Deployment requirement: libwkhtmltox.dll (x64, ~10 MB) must be present in the
/// application root directory on Windows Server. Download from https://wkhtmltopdf.org/downloads.html
///
/// Business rules (US-013):
///   BR-2: A4 format, embedded fonts, max 10 MB per file.
/// </summary>
public sealed class LetterPostProcessor : IChannelPostProcessor
{
    /// <summary>Maximum PDF file size: 10 MB.</summary>
    public const int MaxFileSizeBytes = 10 * 1024 * 1024;

    private readonly IConverter _converter;
    private readonly LetterPostProcessorOptions _options;
    private readonly ILogger<LetterPostProcessor> _logger;

    public ChannelType Channel => ChannelType.Letter;

    public LetterPostProcessor(
        IConverter converter,
        IOptions<LetterPostProcessorOptions> options,
        ILogger<LetterPostProcessor> logger)
    {
        _converter = converter;
        _options = options.Value;
        _logger = logger;
    }

    /// <inheritdoc/>
    public Task<PostProcessingResult> ProcessAsync(
        string renderedHtml,
        PostProcessingContext? context = null,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(renderedHtml))
        {
            throw new PostProcessingException(
                "Cannot convert empty HTML content to PDF.",
                channel: "Letter",
                isTransient: false);
        }

        try
        {
            _logger.LogDebug(
                "Starting HTML-to-PDF conversion. CampaignId={CampaignId} RecipientId={RecipientId}",
                context?.CampaignId,
                context?.RecipientId);

            var document = BuildDocument(renderedHtml, context?.BaseUrl);
            var pdfBytes = _converter.Convert(document);

            if (pdfBytes is null || pdfBytes.Length == 0)
            {
                throw new PostProcessingException(
                    "PDF converter returned an empty result.",
                    channel: "Letter",
                    isTransient: true);
            }

            if (pdfBytes.Length > MaxFileSizeBytes)
            {
                _logger.LogWarning(
                    "Generated PDF exceeds 10 MB limit ({SizeBytes} bytes). " +
                    "CampaignId={CampaignId}",
                    pdfBytes.Length,
                    context?.CampaignId);

                throw new PostProcessingException(
                    $"Generated PDF ({pdfBytes.Length / 1024 / 1024} MB) exceeds the 10 MB limit per file.",
                    channel: "Letter",
                    isTransient: false);
            }

            _logger.LogDebug(
                "HTML-to-PDF conversion completed. Size={SizeKb} KB. CampaignId={CampaignId}",
                pdfBytes.Length / 1024,
                context?.CampaignId);

            return Task.FromResult(PostProcessingResult.Binary(pdfBytes, "application/pdf"));
        }
        catch (PostProcessingException)
        {
            throw;
        }
        catch (Exception ex)
        {
            _logger.LogError(
                ex,
                "HTML-to-PDF conversion failed. CampaignId={CampaignId}",
                context?.CampaignId);

            // DinkToPdf throws various exceptions when the native library is unavailable or
            // when input HTML is malformed — treat as transient to allow retry.
            throw new PostProcessingException(
                $"PDF conversion failed: {ex.Message}",
                ex,
                channel: "Letter",
                isTransient: true);
        }
    }

    private HtmlToPdfDocument BuildDocument(string html, string? baseUrl)
    {
        return new HtmlToPdfDocument
        {
            GlobalSettings =
            {
                ColorMode = ColorMode.Color,
                Orientation = Orientation.Portrait,
                PaperSize = PaperKind.A4,       // Business rule: A4 format
                Margins = new MarginSettings
                {
                    Top = _options.MarginTopMm,
                    Bottom = _options.MarginBottomMm,
                    Left = _options.MarginLeftMm,
                    Right = _options.MarginRightMm,
                    Unit = Unit.Millimeters
                },
                DPI = _options.Dpi,
                UseCompression = true
            },
            Objects =
            {
                new ObjectSettings
                {
                    PagesCount = true,
                    HtmlContent = html,
                    WebSettings =
                    {
                        DefaultEncoding = "utf-8",
                        // Base URL for loading embedded CSS and images
                        UserStyleSheet = null
                    },
                    HeaderSettings =
                    {
                        FontSize = 0,         // No header
                        Line = false
                    },
                    FooterSettings =
                    {
                        FontSize = 0,         // No footer
                        Line = false
                    }
                }
            }
        };
    }
}
