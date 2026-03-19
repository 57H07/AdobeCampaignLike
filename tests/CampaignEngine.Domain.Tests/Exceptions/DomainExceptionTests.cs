using CampaignEngine.Domain.Exceptions;

namespace CampaignEngine.Domain.Tests.Exceptions;

public class DomainExceptionTests
{
    [Fact]
    public void DomainException_ShouldPreserveMessage()
    {
        const string message = "Business rule violated";
        var exception = new DomainException(message);

        exception.Message.Should().Be(message);
    }

    [Fact]
    public void NotFoundException_ShouldIncludeEntityNameAndKey()
    {
        var exception = new NotFoundException("Template", Guid.NewGuid());

        exception.Message.Should().Contain("Template");
        exception.Should().BeAssignableTo<DomainException>();
    }

    [Fact]
    public void ValidationException_WithErrors_ShouldExposeErrorDictionary()
    {
        var errors = new Dictionary<string, string[]>
        {
            { "Name", new[] { "Name is required" } },
            { "Channel", new[] { "Invalid channel type" } }
        };

        var exception = new ValidationException(errors);

        exception.Errors.Should().HaveCount(2);
        exception.Errors["Name"].Should().Contain("Name is required");
    }
}
