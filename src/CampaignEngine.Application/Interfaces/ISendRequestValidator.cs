using CampaignEngine.Application.DTOs.Send;
using CampaignEngine.Domain.Entities;

namespace CampaignEngine.Application.Interfaces;

/// <summary>
/// Validates a SendRequest against business rules before dispatch:
/// - Template must exist and be Published
/// - Channel must match the template channel
/// - All required placeholder keys must be present in the data dictionary
/// - Recipient must have a valid address for the specified channel
/// </summary>
public interface ISendRequestValidator
{
    /// <summary>
    /// Validates the send request and the resolved template.
    /// </summary>
    /// <param name="request">The incoming send request.</param>
    /// <param name="template">The template resolved from TemplateId.</param>
    /// <returns>
    /// A list of validation error messages. Empty list means the request is valid.
    /// </returns>
    IReadOnlyList<string> Validate(SendRequest request, Template template);
}
