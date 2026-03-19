namespace CampaignEngine.Domain.Exceptions;

/// <summary>
/// Exception thrown when domain validation rules are violated.
/// </summary>
public class ValidationException : DomainException
{
    public IReadOnlyDictionary<string, string[]> Errors { get; }

    public ValidationException(string message) : base(message)
    {
        Errors = new Dictionary<string, string[]>();
    }

    public ValidationException(IDictionary<string, string[]> errors)
        : base("One or more validation errors occurred.")
    {
        Errors = errors.AsReadOnly();
    }
}
