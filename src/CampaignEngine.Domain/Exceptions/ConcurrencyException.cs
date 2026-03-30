namespace CampaignEngine.Domain.Exceptions;

/// <summary>
/// Exception thrown when an optimistic concurrency conflict is detected during a save.
/// Maps to HTTP 409 Conflict in the Global Exception Middleware.
/// </summary>
public class ConcurrencyException : DomainException
{
    public ConcurrencyException(string message) : base(message)
    {
    }

    public ConcurrencyException(string message, Exception innerException)
        : base(message, innerException)
    {
    }
}
