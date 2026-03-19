using System.Diagnostics;
using CampaignEngine.Infrastructure.Rendering;
using Microsoft.Extensions.Logging.Abstractions;

namespace CampaignEngine.Infrastructure.Tests.Rendering;

/// <summary>
/// Performance benchmark tests for ScribanTemplateRenderer.
/// Verifies the acceptance criterion: 1000 renders/sec minimum throughput.
///
/// These are functional performance checks (not BenchmarkDotNet micro-benchmarks)
/// using Stopwatch timing to give fast CI feedback.
///
/// Target: 1000 renders in under 1 second on any modern CI machine.
/// Note: First-run JIT warmup is excluded from measurement.
/// </summary>
public class ScribanTemplateRendererBenchmarkTests
{
    private readonly ScribanTemplateRenderer _renderer;

    // Simple template representative of real-world scalar substitution
    private const string SimpleTemplate = "Dear {{ first_name }} {{ last_name }}, your account {{ account_id }} has a balance of {{ balance }}.";

    // Moderately complex template with a loop
    private const string LoopTemplate = """
        <ul>
        {{ for item in items }}
          <li>{{ item }}</li>
        {{ end }}
        </ul>
        """;

    public ScribanTemplateRendererBenchmarkTests()
    {
        _renderer = new ScribanTemplateRenderer(NullLogger<ScribanTemplateRenderer>.Instance);
    }

    // ----------------------------------------------------------------
    // Throughput: 1000 renders/sec (acceptance criterion AC-005)
    // ----------------------------------------------------------------

    [Fact]
    public async Task RenderAsync_1000Renders_CompletesUnder1Second()
    {
        const int iterations = 1000;
        var data = new Dictionary<string, object?>
        {
            ["first_name"] = "Alice",
            ["last_name"] = "Dupont",
            ["account_id"] = "ACC-001",
            ["balance"] = 1234.56m
        };

        // Warmup: one call to trigger JIT compilation
        await _renderer.RenderAsync(SimpleTemplate, data);

        // Measure 1000 sequential renders
        var sw = Stopwatch.StartNew();
        for (var i = 0; i < iterations; i++)
        {
            await _renderer.RenderAsync(SimpleTemplate, data);
        }
        sw.Stop();

        // Assert: 1000 renders must complete within 1000ms
        sw.ElapsedMilliseconds.Should().BeLessThan(1000,
            $"Expected 1000 renders/sec but {iterations} renders took {sw.ElapsedMilliseconds}ms. " +
            $"Throughput: {iterations * 1000.0 / sw.ElapsedMilliseconds:F0} renders/sec.");
    }

    [Fact]
    public async Task RenderAsync_1000ParallelRenders_AllSucceedWithinTimeout()
    {
        const int parallelism = 1000;
        var data = new Dictionary<string, object?>
        {
            ["first_name"] = "Bob",
            ["last_name"] = "Martin",
            ["account_id"] = "ACC-002",
            ["balance"] = 9999.99m
        };

        // Warmup
        await _renderer.RenderAsync(SimpleTemplate, data);

        var sw = Stopwatch.StartNew();
        var tasks = Enumerable.Range(0, parallelism)
            .Select(_ => _renderer.RenderAsync(SimpleTemplate, data))
            .ToList();

        var results = await Task.WhenAll(tasks);
        sw.Stop();

        // All renders should produce consistent results
        results.Should().HaveCount(parallelism);
        results.Should().OnlyContain(r => r.Contains("Bob") && r.Contains("Martin"));

        // Parallel execution should be faster than sequential due to async
        sw.ElapsedMilliseconds.Should().BeLessThan(5000,
            $"1000 parallel renders took {sw.ElapsedMilliseconds}ms — expected < 5000ms");
    }

    [Fact]
    public async Task RenderAsync_SingleRender_CompletesWithin50ms()
    {
        var data = new Dictionary<string, object?>
        {
            ["first_name"] = "Carol",
            ["last_name"] = "Smith",
            ["account_id"] = "ACC-003",
            ["balance"] = 500m
        };

        // Warmup
        await _renderer.RenderAsync(SimpleTemplate, data);

        // Measure a single render p95 target
        var timings = new List<long>(10);
        for (var i = 0; i < 10; i++)
        {
            var sw = Stopwatch.StartNew();
            await _renderer.RenderAsync(SimpleTemplate, data);
            sw.Stop();
            timings.Add(sw.ElapsedMilliseconds);
        }

        var p95 = timings.OrderBy(t => t).Skip((int)(timings.Count * 0.95)).FirstOrDefault();
        p95.Should().BeLessThan(50,
            $"p95 single render time was {p95}ms — expected < 50ms. " +
            "Individual renders are far below the 500ms API target.");
    }

    [Fact]
    public async Task RenderAsync_LoopTemplate_200Renders_CompletesUnder1Second()
    {
        const int iterations = 200;
        var data = new Dictionary<string, object?>
        {
            ["items"] = Enumerable.Range(1, 10).Select(i => $"Item {i}").ToList<object?>()
        };

        // Warmup
        await _renderer.RenderAsync(LoopTemplate, data);

        var sw = Stopwatch.StartNew();
        for (var i = 0; i < iterations; i++)
        {
            await _renderer.RenderAsync(LoopTemplate, data);
        }
        sw.Stop();

        sw.ElapsedMilliseconds.Should().BeLessThan(1000,
            $"{iterations} loop template renders took {sw.ElapsedMilliseconds}ms. " +
            $"Throughput: {iterations * 1000.0 / sw.ElapsedMilliseconds:F0} renders/sec.");
    }

    [Fact]
    public async Task RenderAsync_ReportsThroughput_InOutputForObservability()
    {
        const int iterations = 1000;
        var data = new Dictionary<string, object?> { ["name"] = "World" };

        // Warmup
        await _renderer.RenderAsync("Hello {{ name }}!", data);

        var sw = Stopwatch.StartNew();
        for (var i = 0; i < iterations; i++)
        {
            await _renderer.RenderAsync("Hello {{ name }}!", data);
        }
        sw.Stop();

        var rendersPerSecond = iterations * 1000.0 / sw.ElapsedMilliseconds;

        // Log for observability (visible in test output)
        Console.WriteLine($"[PERF] ScribanTemplateRenderer: {rendersPerSecond:F0} renders/sec ({sw.ElapsedMilliseconds}ms for {iterations} renders)");

        // Sanity check: must exceed 1000 renders/sec
        rendersPerSecond.Should().BeGreaterThan(1000,
            $"Throughput {rendersPerSecond:F0} renders/sec must exceed the 1000/sec target. " +
            $"Elapsed: {sw.ElapsedMilliseconds}ms for {iterations} renders.");
    }
}
