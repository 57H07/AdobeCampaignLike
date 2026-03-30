using DocumentFormat.OpenXml.Packaging;
using DocumentFormat.OpenXml.Wordprocessing;
using DocumentFormat.OpenXml;

namespace CampaignEngine.Infrastructure.Rendering;

/// <summary>
/// Merges adjacent split Word XML runs so that template placeholders such as
/// <c>{{ firstName }}</c> are recognised even when Word has fragmented them
/// across multiple <c>&lt;w:r&gt;</c> elements.
///
/// F-301 behaviour:
/// - Traverses the main document body, all HeaderParts and all FooterParts.
/// - Adjacent <c>&lt;w:r&gt;</c> elements that share the same <c>&lt;w:rPr&gt;</c>
///   (identical XML serialisation, or both absent) are merged into one run.
/// - <c>&lt;w:bookmarkStart&gt;</c> and <c>&lt;w:bookmarkEnd&gt;</c> elements
///   that fall between runs in the same merge-group are preserved by relocating
///   them immediately after the merged run.
/// - Smart quotes (U+201C LEFT DOUBLE QUOTATION MARK and U+201D RIGHT DOUBLE
///   QUOTATION MARK) in run text are normalised to U+0022 QUOTATION MARK.
///
/// Edge-case note:
/// Bookmark elements that appear inside a split placeholder run sequence are
/// discarded from within the merged run text but are kept as sibling elements
/// immediately after the merged run. This means the bookmark anchor is
/// preserved at the paragraph level even though it no longer points precisely
/// at the placeholder text character position. This is an acceptable trade-off
/// per Business Rule 2: valid template authoring should not place bookmarks
/// inside placeholder tokens.
/// </summary>
public sealed class DocxRunMerger
{
    /// <summary>
    /// Merges all paragraphs reachable from the document: main body, all
    /// registered header parts, and all registered footer parts.
    /// </summary>
    /// <param name="doc">The open <see cref="WordprocessingDocument"/>.</param>
    /// <exception cref="ArgumentNullException">
    /// Thrown when <paramref name="doc"/> is <c>null</c>.
    /// </exception>
    public void MergeRuns(WordprocessingDocument doc)
    {
        ArgumentNullException.ThrowIfNull(doc);

        // Main document body
        var mainPart = doc.MainDocumentPart;
        if (mainPart?.Document?.Body is not null)
        {
            ProcessContainer(mainPart.Document.Body);
        }

        // All header parts
        if (mainPart is not null)
        {
            foreach (var headerPart in mainPart.HeaderParts)
            {
                if (headerPart.Header is not null)
                {
                    ProcessContainer(headerPart.Header);
                }
            }

            // All footer parts
            foreach (var footerPart in mainPart.FooterParts)
            {
                if (footerPart.Footer is not null)
                {
                    ProcessContainer(footerPart.Footer);
                }
            }
        }
    }

    // ---------------------------------------------------------------
    // Internal helpers
    // ---------------------------------------------------------------

    /// <summary>
    /// Visits every paragraph inside <paramref name="container"/> and merges
    /// its runs.
    /// </summary>
    private static void ProcessContainer(OpenXmlElement container)
    {
        // ToList() prevents modification-during-enumeration issues.
        foreach (var para in container.Descendants<Paragraph>().ToList())
        {
            MergeParagraphRuns(para);
        }
    }

    /// <summary>
    /// Merges adjacent runs with identical <c>&lt;w:rPr&gt;</c> inside a
    /// single paragraph.
    ///
    /// Algorithm:
    /// 1. Walk the direct children of the paragraph in order.
    /// 2. Accumulate consecutive Run elements whose rPr serialises identically
    ///    into a "group".
    /// 3. When the sequence is broken (different rPr, or a non-Run / non-bookmark
    ///    element), flush the accumulated group: keep the first run, append all
    ///    text from later runs into it, then remove the later runs.
    /// 4. Bookmark elements (bookmarkStart, bookmarkEnd) between runs are
    ///    collected and re-inserted after the merged run to preserve them.
    /// 5. After merging, apply smart-quote normalisation to every text node.
    /// </summary>
    private static void MergeParagraphRuns(Paragraph para)
    {
        // ---- Pass 1: merge runs ----
        // We build a list of "slots": each slot is either a Run (merge
        // candidate) or a non-Run element. We process the children once and
        // re-write the paragraph.

        var children = para.ChildElements.ToList();

        // Groups: List<(List<Run> runs, List<OpenXmlElement> bookmarks)>
        // A group holds consecutive runs (same rPr) plus any bookmarks that
        // were interspersed between them.
        var result = new List<OpenXmlElement>(); // final ordered element list

        Run? pendingRun = null;                  // first run of current group
        string? pendingRprXml = null;            // serialised rPr of that group
        var pendingBookmarks = new List<OpenXmlElement>(); // bookmarks in group

        void FlushGroup()
        {
            if (pendingRun is null) return;

            // Normalise smart quotes in the accumulated text of the merged run.
            foreach (var text in pendingRun.Elements<Text>())
            {
                text.Text = NormalizeSmartQuotes(text.Text);
            }

            result.Add(pendingRun);

            // Re-append bookmarks after the merged run so they are preserved.
            result.AddRange(pendingBookmarks);

            pendingRun = null;
            pendingRprXml = null;
            pendingBookmarks.Clear();
        }

        foreach (var child in children)
        {
            if (child is BookmarkStart or BookmarkEnd)
            {
                // Bookmarks can appear between runs. If we have an open group
                // collect the bookmark; it will be emitted after the merged run.
                if (pendingRun is not null)
                {
                    pendingBookmarks.Add((OpenXmlElement)child.CloneNode(true));
                }
                else
                {
                    // Bookmark outside any run group — keep in place.
                    result.Add((OpenXmlElement)child.CloneNode(true));
                }
                continue;
            }

            if (child is Run run)
            {
                var rprXml = SerializeRpr(run);

                if (pendingRun is null)
                {
                    // Start a new group.
                    pendingRun = (Run)run.CloneNode(true);
                    pendingRprXml = rprXml;
                }
                else if (rprXml == pendingRprXml)
                {
                    // Same formatting — append text to the open group's run.
                    foreach (var text in run.Elements<Text>())
                    {
                        var cloned = (Text)text.CloneNode(true);
                        pendingRun.AppendChild(cloned);
                    }
                    // Bookmarks collected above stay in pendingBookmarks.
                }
                else
                {
                    // Different formatting — flush current group and start fresh.
                    FlushGroup();
                    pendingRun = (Run)run.CloneNode(true);
                    pendingRprXml = rprXml;
                }
                continue;
            }

            // Any other element (w:proofErr, w:ins, paragraph mark, etc.)
            // — flush the current run group and keep the element.
            FlushGroup();
            result.Add((OpenXmlElement)child.CloneNode(true));
        }

        // Flush the last open group.
        FlushGroup();

        // ---- Pass 2: rewrite paragraph children ----
        // Remove all existing children then re-add in new order.
        para.RemoveAllChildren();
        foreach (var elem in result)
        {
            para.AppendChild(elem);
        }
    }

    /// <summary>
    /// Returns a canonical XML string for the <c>&lt;w:rPr&gt;</c> of a run,
    /// or an empty string when the run has no rPr element. Used to determine
    /// whether two adjacent runs have identical formatting.
    /// </summary>
    private static string SerializeRpr(Run run)
    {
        var rpr = run.GetFirstChild<RunProperties>();
        return rpr?.OuterXml ?? string.Empty;
    }

    /// <summary>
    /// Replaces U+201C (LEFT DOUBLE QUOTATION MARK) and U+201D (RIGHT DOUBLE
    /// QUOTATION MARK) with U+0022 (QUOTATION MARK) in the given string.
    /// Returns the original reference when no replacement is needed.
    /// </summary>
    private static string NormalizeSmartQuotes(string? text)
    {
        if (string.IsNullOrEmpty(text)) return text ?? string.Empty;
        if (!text.Contains('\u201C') && !text.Contains('\u201D')) return text;
        return text.Replace('\u201C', '"').Replace('\u201D', '"');
    }
}
