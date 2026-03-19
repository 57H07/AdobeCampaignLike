using CampaignEngine.Application.DTOs.Send;
using CampaignEngine.Application.Interfaces;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace CampaignEngine.Web.Controllers;

/// <summary>
/// Generic Send API — POST /api/send.
/// Provides a simple, synchronous endpoint for integration consumers to trigger
/// single transactional messages without interacting with the Campaign Orchestrator.
/// </summary>
/// <remarks>
/// Authentication is handled by the global fallback policy (authenticated user required).
/// No specific role is required — any authenticated consumer can call this endpoint.
/// </remarks>
[ApiController]
[Route("api/send")]
[Produces("application/json")]
public class SendController : ControllerBase
{
    private readonly ISingleSendService _sendService;

    public SendController(ISingleSendService sendService)
    {
        _sendService = sendService;
    }

    /// <summary>
    /// Sends a single transactional message immediately.
    /// </summary>
    /// <remarks>
    /// Resolves the specified template, substitutes all placeholders with the
    /// provided data dictionary, and dispatches the message through the appropriate
    /// channel (Email, SMS, or Letter).
    ///
    /// Business rules:
    /// - The template must be in Published status.
    /// - All placeholder keys declared in the template manifest must be present in <c>data</c>.
    /// - For Email channel, <c>recipient.email</c> is required and must be a valid email address.
    /// - For SMS channel, <c>recipient.phoneNumber</c> is required and must be in E.164 format.
    /// - Response time target is &lt; 500ms at p95.
    ///
    /// The response includes a <c>trackingId</c> (GUID) that can be used to query the
    /// send log for status and error details.
    /// </remarks>
    /// <param name="request">The send request containing template, channel, data, and recipient.</param>
    /// <param name="cancellationToken">Optional cancellation token.</param>
    /// <returns>
    /// 200 OK with <see cref="SendResponse"/> on success or dispatch failure (check <c>success</c> field).
    /// 400 Bad Request if validation fails (missing data, wrong channel, unpublished template, etc.).
    /// 404 Not Found if the template does not exist.
    /// </returns>
    [HttpPost]
    [AllowAnonymous]
    [ProducesResponseType(typeof(SendResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ValidationProblemDetails), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<ActionResult<SendResponse>> Send(
        [FromBody] SendRequest request,
        CancellationToken cancellationToken = default)
    {
        // Model binding validation (Required attributes on DTO) is checked first by ASP.NET Core.
        // Domain validation (template status, placeholder completeness, recipient format)
        // is performed inside ISingleSendService and surfaced as domain exceptions caught by
        // GlobalExceptionMiddleware (ValidationException → 400, NotFoundException → 404).

        var response = await _sendService.SendAsync(request, cancellationToken);
        return Ok(response);
    }
}
