namespace CampaignEngine.Domain.Exceptions;

/// <summary>
/// Thrown when an SMS send operation fails.
/// TASK-020-01: SMS error categorization (transient vs permanent).
///
/// Transient errors (retriable): network timeout, provider rate limit, temporary server error.
/// Permanent errors (not retriable): invalid phone number, account suspended, message too long.
/// </summary>
public class SmsDispatchException : DomainException
{
    /// <summary>
    /// Whether the failure is transient and should be retried.
    /// Transient: network error, provider timeout, rate limit (429).
    /// Permanent: invalid phone number (400), account disabled (403), bad request.
    /// </summary>
    public bool IsTransient { get; }

    /// <summary>Optional HTTP status code from the provider API response (e.g. 400, 429, 500).</summary>
    public int? HttpStatusCode { get; }

    /// <summary>Optional provider-specific error code or message body.</summary>
    public string? ProviderErrorCode { get; }

    public SmsDispatchException(string message, bool isTransient, int? httpStatusCode = null, string? providerErrorCode = null)
        : base(message)
    {
        IsTransient = isTransient;
        HttpStatusCode = httpStatusCode;
        ProviderErrorCode = providerErrorCode;
    }

    public SmsDispatchException(string message, bool isTransient, Exception innerException,
        int? httpStatusCode = null, string? providerErrorCode = null)
        : base(message, innerException)
    {
        IsTransient = isTransient;
        HttpStatusCode = httpStatusCode;
        ProviderErrorCode = providerErrorCode;
    }
}

/// <summary>
/// Thrown when a phone number fails E.164 format validation.
/// This is a permanent failure — retrying will not help.
/// TASK-020-03: Phone number validation.
/// </summary>
public class InvalidPhoneNumberException : DomainException
{
    /// <summary>The phone number that failed validation.</summary>
    public string PhoneNumber { get; }

    public InvalidPhoneNumberException(string phoneNumber)
        : base($"Phone number '{phoneNumber}' is not in E.164 format. Expected format: +[country code][number] (e.g. +12025551234).")
    {
        PhoneNumber = phoneNumber;
    }
}
