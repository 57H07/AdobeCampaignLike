using CampaignEngine.Application.DTOs.Campaigns;
using CampaignEngine.Domain.Enums;
using CampaignEngine.Domain.Exceptions;
using CampaignEngine.Infrastructure.Campaigns;

namespace CampaignEngine.Infrastructure.Tests.Campaigns;

/// <summary>
/// Unit tests for <see cref="CampaignStepValidationService"/>.
/// Covers TASK-024-08: step filter application and multi-step validation rules.
///
/// Business rules tested:
///   1. At least one step is required (empty list is rejected)
///   2. Maximum 10 steps per campaign
///   3. StepOrder values must be unique within the campaign
///   4. StepOrder must be positive (1-based)
///   5. DelayDays must be non-negative
///   6. StepFilter field is optional (null is accepted)
///   7. Valid step configurations pass without exception
/// </summary>
public class CampaignStepValidationServiceTests
{
    private readonly CampaignStepValidationService _sut = new();

    private static readonly Guid AnyTemplateId = Guid.NewGuid();

    // -----------------------------------------------------------------------
    // Helper factory
    // -----------------------------------------------------------------------

    private static CreateCampaignStepRequest MakeStep(
        int stepOrder,
        int delayDays = 0,
        string? stepFilter = null,
        ChannelType channel = ChannelType.Email)
        => new()
        {
            StepOrder = stepOrder,
            Channel = channel,
            TemplateId = AnyTemplateId,
            DelayDays = delayDays,
            StepFilter = stepFilter
        };

    // -----------------------------------------------------------------------
    // Null guard
    // -----------------------------------------------------------------------

    [Fact(DisplayName = "Null steps argument throws ArgumentNullException")]
    public void Validate_NullSteps_ThrowsArgumentNullException()
    {
        var act = () => _sut.Validate(null!);

        act.Should().Throw<ArgumentNullException>();
    }

    // -----------------------------------------------------------------------
    // Rule 1: at least one step required
    // -----------------------------------------------------------------------

    [Fact(DisplayName = "Empty step list throws ValidationException")]
    public void Validate_EmptyList_ThrowsValidationException()
    {
        var act = () => _sut.Validate([]);

        act.Should().Throw<ValidationException>()
            .Which.Errors.Should().ContainKey("steps");
    }

    // -----------------------------------------------------------------------
    // Rule 2: maximum 10 steps
    // -----------------------------------------------------------------------

    [Fact(DisplayName = "Exactly 10 steps is accepted without exception")]
    public void Validate_TenSteps_Passes()
    {
        var steps = Enumerable.Range(1, 10)
            .Select(i => MakeStep(i))
            .ToList();

        var act = () => _sut.Validate(steps);

        act.Should().NotThrow();
    }

    [Fact(DisplayName = "11 steps throws ValidationException about maximum")]
    public void Validate_ElevenSteps_ThrowsValidationException()
    {
        var steps = Enumerable.Range(1, 11)
            .Select(i => MakeStep(i))
            .ToList();

        var act = () => _sut.Validate(steps);

        act.Should().Throw<ValidationException>()
            .Which.Errors["steps"].Should().Contain(e => e.Contains("10"));
    }

    // -----------------------------------------------------------------------
    // Rule 3: StepOrder uniqueness
    // -----------------------------------------------------------------------

    [Fact(DisplayName = "Duplicate step orders throw ValidationException with duplicate info")]
    public void Validate_DuplicateStepOrders_ThrowsValidationException()
    {
        var steps = new[]
        {
            MakeStep(1),
            MakeStep(1),  // duplicate order 1
            MakeStep(2)
        };

        var act = () => _sut.Validate(steps);

        act.Should().Throw<ValidationException>()
            .Which.Errors["steps"].Should().Contain(e => e.Contains("1"));
    }

    [Fact(DisplayName = "Multiple duplicate step orders are all reported")]
    public void Validate_MultipleDuplicates_AllReported()
    {
        var steps = new[]
        {
            MakeStep(2),
            MakeStep(2),
            MakeStep(3),
            MakeStep(3)
        };

        var act = () => _sut.Validate(steps);

        // Should mention both duplicate orders 2 and 3 in the error
        act.Should().Throw<ValidationException>()
            .Which.Errors["steps"].Should().Contain(e => e.Contains("2") && e.Contains("3"));
    }

    // -----------------------------------------------------------------------
    // Rule 4: StepOrder must be positive (1-based)
    // -----------------------------------------------------------------------

    [Fact(DisplayName = "Step with order 0 throws ValidationException")]
    public void Validate_StepOrderZero_ThrowsValidationException()
    {
        var steps = new[] { MakeStep(0) };

        var act = () => _sut.Validate(steps);

        act.Should().Throw<ValidationException>()
            .Which.Errors["steps"].Should().Contain(e => e.Contains("positive") || e.Contains("1-based") || e.Contains("0"));
    }

    [Fact(DisplayName = "Step with negative order throws ValidationException")]
    public void Validate_NegativeStepOrder_ThrowsValidationException()
    {
        var steps = new[] { MakeStep(-1) };

        var act = () => _sut.Validate(steps);

        act.Should().Throw<ValidationException>()
            .Which.Errors["steps"].Should().Contain(e => e.Contains("positive") || e.Contains("-1"));
    }

    [Fact(DisplayName = "Step with order 1 (minimum valid) passes")]
    public void Validate_StepOrderOne_Passes()
    {
        var steps = new[] { MakeStep(1) };

        var act = () => _sut.Validate(steps);

        act.Should().NotThrow();
    }

    // -----------------------------------------------------------------------
    // Rule 5: DelayDays must be non-negative
    // -----------------------------------------------------------------------

    [Fact(DisplayName = "Negative DelayDays throws ValidationException")]
    public void Validate_NegativeDelayDays_ThrowsValidationException()
    {
        var steps = new[] { MakeStep(1, delayDays: -1) };

        var act = () => _sut.Validate(steps);

        act.Should().Throw<ValidationException>()
            .Which.Errors["steps"].Should().Contain(e => e.Contains("negative") || e.Contains("delay"));
    }

    [Fact(DisplayName = "DelayDays = 0 (immediate) is accepted")]
    public void Validate_DelayDaysZero_Passes()
    {
        var steps = new[] { MakeStep(1, delayDays: 0) };

        var act = () => _sut.Validate(steps);

        act.Should().NotThrow();
    }

    [Fact(DisplayName = "Large positive DelayDays is accepted")]
    public void Validate_LargePositiveDelayDays_Passes()
    {
        var steps = new[] { MakeStep(1, delayDays: 365) };

        var act = () => _sut.Validate(steps);

        act.Should().NotThrow();
    }

    [Fact(DisplayName = "Multiple steps with negative delays report the offending step orders")]
    public void Validate_MultipleNegativeDelays_ReportsStepOrders()
    {
        var steps = new[]
        {
            MakeStep(1, delayDays: 0),
            MakeStep(2, delayDays: -5),
            MakeStep(3, delayDays: -2)
        };

        var act = () => _sut.Validate(steps);

        act.Should().Throw<ValidationException>()
            .Which.Errors["steps"].Should().Contain(e => e.Contains("2") || e.Contains("3"));
    }

    // -----------------------------------------------------------------------
    // Rule 6: StepFilter is optional (null and valid JSON are both accepted by validation)
    // -----------------------------------------------------------------------

    [Fact(DisplayName = "Step with null StepFilter passes validation")]
    public void Validate_StepFilterNull_Passes()
    {
        var steps = new[] { MakeStep(1, stepFilter: null) };

        var act = () => _sut.Validate(steps);

        act.Should().NotThrow();
    }

    [Fact(DisplayName = "Step with non-null StepFilter JSON passes validation")]
    public void Validate_StepFilterNotNull_Passes()
    {
        // Validation service does not parse the JSON content; it only enforces structural rules
        var jsonFilter = """[{"type":"leaf","fieldName":"Status","operator":1,"value":"active"}]""";
        var steps = new[] { MakeStep(1, stepFilter: jsonFilter) };

        var act = () => _sut.Validate(steps);

        act.Should().NotThrow();
    }

    // -----------------------------------------------------------------------
    // Rule 7: Valid multi-step configurations pass
    // -----------------------------------------------------------------------

    [Theory(DisplayName = "Single valid step with any channel passes")]
    [InlineData(ChannelType.Email)]
    [InlineData(ChannelType.Letter)]
    [InlineData(ChannelType.Sms)]
    public void Validate_SingleStepAnyChannel_Passes(ChannelType channel)
    {
        var steps = new[] { MakeStep(1, channel: channel) };

        var act = () => _sut.Validate(steps);

        act.Should().NotThrow();
    }

    [Fact(DisplayName = "Three-step valid campaign (Email day 0, Email day 15, SMS day 20) passes")]
    public void Validate_ThreeStepExampleFromSpec_Passes()
    {
        var steps = new[]
        {
            MakeStep(1, delayDays: 0, channel: ChannelType.Email),
            MakeStep(2, delayDays: 15, channel: ChannelType.Email),
            MakeStep(3, delayDays: 20, channel: ChannelType.Sms)
        };

        var act = () => _sut.Validate(steps);

        act.Should().NotThrow();
    }

    [Fact(DisplayName = "Mixed-channel multi-step campaign passes validation")]
    public void Validate_MixedChannelMultiStep_Passes()
    {
        var steps = new[]
        {
            MakeStep(1, delayDays: 0, channel: ChannelType.Email),
            MakeStep(2, delayDays: 3, channel: ChannelType.Letter),
            MakeStep(3, delayDays: 7, channel: ChannelType.Sms),
            MakeStep(4, delayDays: 14, channel: ChannelType.Email)
        };

        var act = () => _sut.Validate(steps);

        act.Should().NotThrow();
    }

    // -----------------------------------------------------------------------
    // Step filter: AND-combination semantics (documented in StepFilter field)
    // -----------------------------------------------------------------------

    [Fact(DisplayName = "Step with step filter and base campaign filter can coexist (filter is a plain string)")]
    public void Validate_StepWithStepFilterCoexistsWithCampaignFilter_Passes()
    {
        // The validation service does not evaluate whether the step filter AND-combines
        // correctly with a campaign filter — that is a runtime concern. We only confirm
        // that the step filter string is accepted as-is during validation.
        var stepFilter = """[{"type":"leaf","fieldName":"Responded","operator":2,"value":"true"}]""";
        var steps = new[]
        {
            MakeStep(1, delayDays: 0),
            MakeStep(2, delayDays: 15, stepFilter: stepFilter)  // non-respondents only
        };

        var act = () => _sut.Validate(steps);

        act.Should().NotThrow();
    }
}
