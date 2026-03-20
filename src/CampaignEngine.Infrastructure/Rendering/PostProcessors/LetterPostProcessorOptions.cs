namespace CampaignEngine.Infrastructure.Rendering.PostProcessors;

/// <summary>
/// Configuration options for the Letter post-processor (DinkToPdf / wkhtmltopdf).
/// Bound from appsettings.json section "LetterPostProcessor".
/// </summary>
public sealed class LetterPostProcessorOptions
{
    public const string SectionName = "LetterPostProcessor";

    /// <summary>Top margin in millimeters. Default = 20 mm.</summary>
    public double MarginTopMm { get; set; } = 20;

    /// <summary>Bottom margin in millimeters. Default = 20 mm.</summary>
    public double MarginBottomMm { get; set; } = 20;

    /// <summary>Left margin in millimeters. Default = 25 mm.</summary>
    public double MarginLeftMm { get; set; } = 25;

    /// <summary>Right margin in millimeters. Default = 25 mm.</summary>
    public double MarginRightMm { get; set; } = 25;

    /// <summary>Output DPI (dots per inch). Default = 96.</summary>
    public int Dpi { get; set; } = 96;
}
