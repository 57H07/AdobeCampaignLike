using CampaignEngine.Application.DependencyInjection;
using CampaignEngine.Application.Interfaces;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace CampaignEngine.Web.Controllers;

/// <summary>
/// Admin REST endpoint for rate limiting metrics and configuration inspection.
///
/// US-022 TASK-022-05: Rate limit metrics exposed for monitoring.
///
/// Endpoints:
///   GET  /api/admin/rate-limit-metrics         — current metrics snapshot for all channels
///   POST /api/admin/rate-limit-metrics/reset    — reset all counters (admin only)
///
/// Authentication: X-Api-Key header (Admin role required).
/// No Prometheus/AppMetrics infrastructure needed — lightweight JSON endpoint
/// suitable for Windows Server + IIS deployment.
/// </summary>
[ApiController]
[Route("api/admin/rate-limit-metrics")]
[Authorize(Policy = AuthorizationPolicies.RequireAdmin)]
[Produces("application/json")]
public class RateLimitMetricsController : ControllerBase
{
    private readonly IRateLimitMetricsService _metricsService;
    private readonly IChannelRateLimiterRegistry _registry;

    public RateLimitMetricsController(
        IRateLimitMetricsService metricsService,
        IChannelRateLimiterRegistry registry)
    {
        _metricsService = metricsService;
        _registry = registry;
    }

    /// <summary>
    /// Returns the current rate limiting metrics snapshot for all channels.
    ///
    /// Fields:
    ///   - channel: Email | Sms | Letter
    ///   - configuredRatePerSecond: 0 = unlimited
    ///   - tokensAcquired: total sends since last reset
    ///   - throttleWaitCount: times a sender had to wait for a token
    ///   - totalWaitDuration: cumulative time spent waiting
    ///   - averageWaitDuration: average wait per throttle event
    ///   - rateLimitExceededCount: times a send was rejected due to timeout
    ///   - currentSendRatePerSecond: approximate current rate
    ///   - windowStartUtc: metrics window start time
    ///   - availableTokens: current bucket level (-1 = unlimited)
    ///   - waitingCount: senders currently waiting for a token
    /// </summary>
    [HttpGet]
    [ProducesResponseType(typeof(RateLimitMetricsResponse), StatusCodes.Status200OK)]
    public IActionResult GetMetrics()
    {
        var snapshot = _metricsService.GetSnapshot();
        var limiters = _registry.GetAllLimiters()
            .ToDictionary(l => l.Channel, l => l);

        var response = new RateLimitMetricsResponse
        {
            GeneratedAtUtc = DateTime.UtcNow,
            Channels = snapshot.Select(m => new ChannelMetricsDto
            {
                Channel = m.Channel.ToString(),
                ConfiguredRatePerSecond = m.ConfiguredRatePerSecond,
                IsThrottled = m.ConfiguredRatePerSecond > 0,
                TokensAcquired = m.TokensAcquired,
                ThrottleWaitCount = m.ThrottleWaitCount,
                TotalWaitMs = (long)m.TotalWaitDuration.TotalMilliseconds,
                AverageWaitMs = (long)m.AverageWaitDuration.TotalMilliseconds,
                RateLimitExceededCount = m.RateLimitExceededCount,
                CurrentSendRatePerSecond = m.CurrentSendRatePerSecond,
                WindowStartUtc = m.WindowStartUtc,
                AvailableTokens = m.AvailableTokens,
                WaitingCount = m.WaitingCount
            }).ToList()
        };

        return Ok(response);
    }

    /// <summary>
    /// Resets all rate limit metrics counters and the tracking window.
    /// </summary>
    [HttpPost("reset")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    public IActionResult Reset()
    {
        _metricsService.Reset();
        return NoContent();
    }
}

/// <summary>Response DTO for rate limit metrics.</summary>
public sealed class RateLimitMetricsResponse
{
    public DateTime GeneratedAtUtc { get; set; }
    public List<ChannelMetricsDto> Channels { get; set; } = [];
}

/// <summary>Per-channel metrics DTO.</summary>
public sealed class ChannelMetricsDto
{
    public string Channel { get; set; } = string.Empty;
    public int ConfiguredRatePerSecond { get; set; }
    public bool IsThrottled { get; set; }
    public long TokensAcquired { get; set; }
    public long ThrottleWaitCount { get; set; }
    public long TotalWaitMs { get; set; }
    public long AverageWaitMs { get; set; }
    public long RateLimitExceededCount { get; set; }
    public double CurrentSendRatePerSecond { get; set; }
    public DateTime WindowStartUtc { get; set; }
    public double AvailableTokens { get; set; }
    public int WaitingCount { get; set; }
}
