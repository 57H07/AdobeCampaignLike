using CampaignEngine.Application.Interfaces;
using CampaignEngine.Domain.Exceptions;
using Microsoft.Extensions.Logging;
using Scriban;
using Scriban.Runtime;
using AppTemplateContext = CampaignEngine.Application.Models.TemplateContext;
using ScribanRenderContext = Scriban.TemplateContext;

namespace CampaignEngine.Infrastructure.Rendering;

/// <summary>
/// Scriban-based implementation of ITemplateRenderer.
///
/// Security model:
/// - All data values are HTML-encoded by default (prevents XSS — BR-001).
/// - Template HTML itself is trusted (only Designers can create templates — BR-002).
/// - Engine is sandboxed: no file system, no reflection, no .NET object access (BR-003).
/// - Stateless and thread-safe: templates are parsed per call, no shared mutable state (BR-003).
/// - Timeout enforced via CancellationToken (default 10 seconds — BR-004).
///
/// Usage:
///   var result = await renderer.RenderAsync("Hello {{ name }}!", new Dictionary&lt;string, object?&gt; { ["name"] = "World" });
/// </summary>
public sealed class ScribanTemplateRenderer : ITemplateRenderer
{
    private readonly ILogger<ScribanTemplateRenderer> _logger;

    /// <summary>
    /// Default render timeout (10 seconds per BR-004).
    /// </summary>
    public static readonly TimeSpan DefaultTimeout = TimeSpan.FromSeconds(10);

    public ScribanTemplateRenderer(ILogger<ScribanTemplateRenderer> logger)
    {
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    /// <inheritdoc/>
    public Task<string> RenderAsync(
        string templateBody,
        IDictionary<string, object?> data,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(data);

        var context = AppTemplateContext.FromDictionary(data);
        return RenderAsync(templateBody, context, cancellationToken);
    }

    /// <inheritdoc/>
    public async Task<string> RenderAsync(
        string templateBody,
        AppTemplateContext context,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(templateBody);
        ArgumentNullException.ThrowIfNull(context);

        // Validate template body is not empty
        if (string.IsNullOrWhiteSpace(templateBody))
        {
            return string.Empty;
        }

        // Combine the provided CancellationToken with the render timeout
        var timeout = context.Timeout > TimeSpan.Zero ? context.Timeout : DefaultTimeout;
        using var timeoutCts = new CancellationTokenSource(timeout);
        using var linkedCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, timeoutCts.Token);

        // Parse the template — Scriban validates syntax here
        var template = ParseTemplate(templateBody);

        // Build the Scriban script context with security sandbox
        var scribanContext = BuildScribanContext(context, linkedCts.Token);

        // Render with error capture
        string result;
        try
        {
            result = await template.RenderAsync(scribanContext);
        }
        catch (OperationCanceledException ex) when (timeoutCts.IsCancellationRequested && !cancellationToken.IsCancellationRequested)
        {
            // Timeout expired (not a caller-initiated cancel)
            _logger.LogWarning(ex, "Template render timed out after {Timeout}ms", timeout.TotalMilliseconds);
            throw new TemplateRenderException(
                $"Template rendering timed out after {timeout.TotalSeconds} seconds.",
                ex);
        }
        catch (OperationCanceledException)
        {
            // Caller cancelled — rethrow as-is
            throw;
        }
        catch (Exception ex) when (ex.GetType().FullName == "Scriban.Syntax.ScriptAbortException")
        {
            // Scriban throws ScriptAbortException when CancellationToken is triggered
            if (timeoutCts.IsCancellationRequested && !cancellationToken.IsCancellationRequested)
            {
                _logger.LogWarning(ex, "Template render timed out after {Timeout}ms", timeout.TotalMilliseconds);
                throw new TemplateRenderException(
                    $"Template rendering timed out after {timeout.TotalSeconds} seconds.",
                    ex);
            }
            // Caller-initiated cancellation
            throw new OperationCanceledException("Template rendering was cancelled.", ex, cancellationToken);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Template render failed during evaluation");
            throw new TemplateRenderException(
                $"Template rendering failed: {ex.Message}",
                ex);
        }

        return result;
    }

    // ----------------------------------------------------------------
    // Private helpers
    // ----------------------------------------------------------------

    /// <summary>
    /// Parses the template body and throws TemplateRenderException for syntax errors.
    /// </summary>
    private Template ParseTemplate(string templateBody)
    {
        var template = Template.Parse(templateBody);

        if (template.HasErrors)
        {
            var errors = string.Join("; ", template.Messages.Select(m => m.ToString()));
            _logger.LogWarning("Template parse failed: {Errors}", errors);
            throw new TemplateRenderException(
                $"Template syntax error: {errors}",
                template.Messages.FirstOrDefault()?.Span.ToString());
        }

        return template;
    }

    /// <summary>
    /// Builds a sandboxed Scriban script context populated with the provided data.
    ///
    /// Security sandbox:
    /// - LoopLimit: prevents infinite loops (max 10,000 iterations)
    /// - RecursionLimit: prevents stack overflow (max 64 levels)
    /// - CancellationToken: enforces render timeout
    /// - StrictVariables = false: missing vars render as empty string (designer-friendly)
    /// - No custom member access: only ScriptObject values are accessible
    /// </summary>
    private static ScribanRenderContext BuildScribanContext(AppTemplateContext context, CancellationToken cancellationToken)
    {
        var scribanContext = new ScribanRenderContext
        {
            // Strict mode off: missing variables render as empty string (designer-friendly)
            StrictVariables = false,

            // Safety limits to prevent denial-of-service via malformed templates
            LoopLimit = 10_000,
            ObjectRecursionLimit = 64,

            // Enforce timeout via CancellationToken
            CancellationToken = cancellationToken
        };

        // Build the root script object from context data
        var scriptObject = BuildScriptObject(context);
        scribanContext.PushGlobal(scriptObject);

        return scribanContext;
    }

    /// <summary>
    /// Builds a Scriban ScriptObject from the TemplateContext data dictionary.
    /// All string values are HTML-encoded when HtmlEncodeValues is true (BR-001).
    /// </summary>
    private static ScriptObject BuildScriptObject(AppTemplateContext context)
    {
        var scriptObject = new ScriptObject();

        foreach (var kvp in context.Data)
        {
            var key = NormalizeKey(kvp.Key);
            var value = ConvertValue(kvp.Value, context.HtmlEncodeValues);
            scriptObject.SetValue(key, value, readOnly: true);
        }

        return scriptObject;
    }

    /// <summary>
    /// Normalizes a data key to Scriban-compatible lowercase.
    /// Scriban uses lowercase keys by convention.
    /// </summary>
    private static string NormalizeKey(string key)
    {
        return key.ToLowerInvariant();
    }

    /// <summary>
    /// Converts a .NET value to a Scriban-safe value.
    /// Strings are HTML-encoded when htmlEncode is true (BR-001).
    /// Collections (arrays, lists) are converted to Scriban arrays.
    /// Dictionaries become nested ScriptObjects.
    /// </summary>
    private static object? ConvertValue(object? value, bool htmlEncode)
    {
        return value switch
        {
            null => null,
            string s => htmlEncode ? System.Net.WebUtility.HtmlEncode(s) : s,
            IDictionary<string, object?> dict => ConvertDictionary(dict, htmlEncode),
            System.Collections.IEnumerable enumerable when value is not string => ConvertEnumerable(enumerable, htmlEncode),
            _ => value
        };
    }

    private static ScriptObject ConvertDictionary(IDictionary<string, object?> dict, bool htmlEncode)
    {
        var obj = new ScriptObject();
        foreach (var kvp in dict)
        {
            obj.SetValue(NormalizeKey(kvp.Key), ConvertValue(kvp.Value, htmlEncode), readOnly: true);
        }
        return obj;
    }

    private static ScriptArray ConvertEnumerable(System.Collections.IEnumerable enumerable, bool htmlEncode)
    {
        var array = new ScriptArray();
        foreach (var item in enumerable)
        {
            array.Add(ConvertValue(item, htmlEncode));
        }
        return array;
    }
}
