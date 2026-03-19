using CampaignEngine.Application.DTOs.Send;

namespace CampaignEngine.Application.Interfaces;

/// <summary>
/// Orchestrates the full lifecycle of a single transactional send:
/// template resolution, validation, rendering, dispatch, and response construction.
/// Synchronous (inline) — no background queue involvement.
/// </summary>
public interface ISingleSendService
{
    /// <summary>
    /// Processes a single send request synchronously:
    /// 1. Resolves and validates the template.
    /// 2. Validates the request against business rules.
    /// 3. Renders the template with the provided data.
    /// 4. Dispatches via the appropriate channel dispatcher.
    /// 5. Returns a response with tracking ID and send status.
    /// </summary>
    /// <param name="request">The send request.</param>
    /// <param name="cancellationToken">Optional cancellation token.</param>
    /// <returns>A <see cref="SendResponse"/> with tracking ID and send status.</returns>
    Task<SendResponse> SendAsync(SendRequest request, CancellationToken cancellationToken = default);
}
