namespace CampaignEngine.Domain.Common;

/// <summary>
/// Value object representing a reference from a parent template to a sub-template.
/// Captures the sub-template name as it appears in the placeholder syntax {{> name}}.
/// </summary>
public sealed class TemplateReference : IEquatable<TemplateReference>
{
    /// <summary>
    /// The sub-template name as declared in the placeholder syntax {{> name}}.
    /// </summary>
    public string Name { get; }

    /// <summary>
    /// The unique ID of the referenced sub-template, resolved by name at runtime.
    /// May be null if the sub-template cannot be resolved (e.g., it has been deleted).
    /// </summary>
    public Guid? ResolvedTemplateId { get; }

    public TemplateReference(string name, Guid? resolvedTemplateId = null)
    {
        if (string.IsNullOrWhiteSpace(name))
            throw new ArgumentException("Sub-template name cannot be null or whitespace.", nameof(name));

        Name = name.Trim();
        ResolvedTemplateId = resolvedTemplateId;
    }

    public bool Equals(TemplateReference? other)
    {
        if (other is null) return false;
        return string.Equals(Name, other.Name, StringComparison.OrdinalIgnoreCase);
    }

    public override bool Equals(object? obj) => obj is TemplateReference other && Equals(other);

    public override int GetHashCode() => StringComparer.OrdinalIgnoreCase.GetHashCode(Name);

    public override string ToString() => $"{{{{> {Name}}}}}";
}
