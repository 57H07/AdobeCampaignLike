using CampaignEngine.Application.Models;
using CampaignEngine.Infrastructure.Campaigns;

namespace CampaignEngine.Infrastructure.Tests.Campaigns;

/// <summary>
/// Unit tests for <see cref="CampaignStepSchedulingService"/>.
/// Covers TASK-024-07: multi-step scheduling logic.
///
/// Business rules tested:
///   - Step 1 is scheduled at campaignStart + Step1.DelayDays
///   - Each subsequent step is scheduled relative to the previous step's scheduled date
///   - DelayDays = 0 means same date as the base date
///   - Steps are sorted by StepOrder before calculation
///   - Empty step list returns empty result
/// </summary>
public class CampaignStepSchedulingServiceTests
{
    private readonly CampaignStepSchedulingService _sut = new();

    private static readonly DateTime BaseDate = new DateTime(2026, 6, 1, 0, 0, 0, DateTimeKind.Utc);

    // -----------------------------------------------------------------------
    // Empty / edge-case inputs
    // -----------------------------------------------------------------------

    [Fact(DisplayName = "Empty step list returns empty result")]
    public void CalculateStepDates_EmptyList_ReturnsEmpty()
    {
        var result = _sut.CalculateStepDates(BaseDate, []);

        result.Should().BeEmpty();
    }

    [Fact(DisplayName = "Null steps argument throws ArgumentNullException")]
    public void CalculateStepDates_NullSteps_ThrowsArgumentNullException()
    {
        var act = () => _sut.CalculateStepDates(BaseDate, null!);

        act.Should().Throw<ArgumentNullException>();
    }

    // -----------------------------------------------------------------------
    // Single-step campaigns
    // -----------------------------------------------------------------------

    [Fact(DisplayName = "Single step with delay 0 is scheduled on campaign start date")]
    public void CalculateStepDates_SingleStep_DelayZero_ScheduledAtCampaignStart()
    {
        var steps = new[] { new StepScheduleInput(1, 0) };

        var result = _sut.CalculateStepDates(BaseDate, steps);

        result.Should().HaveCount(1);
        result[0].StepOrder.Should().Be(1);
        result[0].ScheduledAt.Should().Be(BaseDate);
    }

    [Fact(DisplayName = "Single step with delay 5 is scheduled at campaignStart + 5 days")]
    public void CalculateStepDates_SingleStep_DelayFive_ScheduledAtStartPlusFive()
    {
        var steps = new[] { new StepScheduleInput(1, 5) };

        var result = _sut.CalculateStepDates(BaseDate, steps);

        result.Should().HaveCount(1);
        result[0].ScheduledAt.Should().Be(BaseDate.AddDays(5));
    }

    // -----------------------------------------------------------------------
    // Multi-step campaigns: sequential delay calculation
    // -----------------------------------------------------------------------

    [Fact(DisplayName = "Step 2 delay is relative to step 1 scheduled date, not campaign start")]
    public void CalculateStepDates_TwoSteps_Step2RelativeToStep1()
    {
        // Step 1: Day 0 (immediate), Step 2: Day +15 relative to step 1
        var steps = new[]
        {
            new StepScheduleInput(1, 0),
            new StepScheduleInput(2, 15)
        };

        var result = _sut.CalculateStepDates(BaseDate, steps);

        result.Should().HaveCount(2);
        result[0].ScheduledAt.Should().Be(BaseDate);                  // 2026-06-01
        result[1].ScheduledAt.Should().Be(BaseDate.AddDays(15));      // 2026-06-16
    }

    [Fact(DisplayName = "Three-step sequence: Email Day 0, Email reminder Day 15, SMS Day 20")]
    public void CalculateStepDates_ThreeStepSequence_ComputesCorrectDates()
    {
        // Business rule example from the US specification:
        // Step 1 (Email, Day 0) -> Step 2 (Email reminder, Day +15) -> Step 3 (SMS, Day +20)
        var steps = new[]
        {
            new StepScheduleInput(1, 0),
            new StepScheduleInput(2, 15),
            new StepScheduleInput(3, 20)
        };

        var result = _sut.CalculateStepDates(BaseDate, steps);

        result.Should().HaveCount(3);
        result[0].ScheduledAt.Should().Be(BaseDate);               // June 1
        result[1].ScheduledAt.Should().Be(BaseDate.AddDays(15));   // June 16
        result[2].ScheduledAt.Should().Be(BaseDate.AddDays(35));   // July 6 (15 + 20)
    }

    [Fact(DisplayName = "All steps with delay 0 are scheduled on the same date as campaign start")]
    public void CalculateStepDates_AllStepsDelayZero_AllOnSameDate()
    {
        var steps = new[]
        {
            new StepScheduleInput(1, 0),
            new StepScheduleInput(2, 0),
            new StepScheduleInput(3, 0),
        };

        var result = _sut.CalculateStepDates(BaseDate, steps);

        result.Should().HaveCount(3);
        result.Should().AllSatisfy(e => e.ScheduledAt.Should().Be(BaseDate));
    }

    [Fact(DisplayName = "Step 1 with delay > 0 shifts all subsequent steps accordingly")]
    public void CalculateStepDates_Step1WithDelay_ShiftsAll()
    {
        // Step 1 starts on Day 5 from campaign start, Step 2 is +10 from step 1
        var steps = new[]
        {
            new StepScheduleInput(1, 5),
            new StepScheduleInput(2, 10)
        };

        var result = _sut.CalculateStepDates(BaseDate, steps);

        result[0].ScheduledAt.Should().Be(BaseDate.AddDays(5));   // Day 5
        result[1].ScheduledAt.Should().Be(BaseDate.AddDays(15));  // Day 5 + 10 = Day 15
    }

    // -----------------------------------------------------------------------
    // Step ordering: out-of-order input is sorted before calculation
    // -----------------------------------------------------------------------

    [Fact(DisplayName = "Steps provided out of order are sorted by StepOrder before calculating")]
    public void CalculateStepDates_OutOfOrderInput_SortedCorrectly()
    {
        // Deliberately reverse the order to verify sort behaviour
        var steps = new[]
        {
            new StepScheduleInput(3, 5),  // Step 3: +5 days from step 2
            new StepScheduleInput(1, 0),  // Step 1: immediate
            new StepScheduleInput(2, 10)  // Step 2: +10 days from step 1
        };

        var result = _sut.CalculateStepDates(BaseDate, steps);

        // After sorting: Step 1 (Day 0), Step 2 (Day 10), Step 3 (Day 15)
        result.Should().HaveCount(3);
        result[0].StepOrder.Should().Be(1);
        result[0].ScheduledAt.Should().Be(BaseDate);              // Day 0
        result[1].StepOrder.Should().Be(2);
        result[1].ScheduledAt.Should().Be(BaseDate.AddDays(10)); // Day 10
        result[2].StepOrder.Should().Be(3);
        result[2].ScheduledAt.Should().Be(BaseDate.AddDays(15)); // Day 15
    }

    // -----------------------------------------------------------------------
    // Maximum steps: 10-step campaign
    // -----------------------------------------------------------------------

    [Fact(DisplayName = "Ten-step campaign with uniform 7-day delay produces correct cumulative dates")]
    public void CalculateStepDates_TenSteps_CumulativeDatesCorrect()
    {
        // Each step has a 7-day delay; step N is scheduled at campaignStart + (N-1)*7 days
        var steps = Enumerable.Range(1, 10)
            .Select(i => new StepScheduleInput(i, 7))
            .ToList();

        // Override step 1 to have delay 0 (from campaign start, no initial delay)
        steps[0] = new StepScheduleInput(1, 0);

        var result = _sut.CalculateStepDates(BaseDate, steps);

        result.Should().HaveCount(10);
        for (int i = 0; i < 10; i++)
        {
            var expectedDays = i * 7; // Step 1 = Day 0, Step 2 = Day 7, ..., Step 10 = Day 63
            result[i].StepOrder.Should().Be(i + 1);
            result[i].ScheduledAt.Should().Be(BaseDate.AddDays(expectedDays));
        }
    }

    // -----------------------------------------------------------------------
    // Result ordering
    // -----------------------------------------------------------------------

    [Fact(DisplayName = "Result list is returned in ascending StepOrder order")]
    public void CalculateStepDates_ResultIsOrderedByStepOrder()
    {
        var steps = new[]
        {
            new StepScheduleInput(2, 10),
            new StepScheduleInput(4, 5),
            new StepScheduleInput(1, 0),
            new StepScheduleInput(3, 7)
        };

        var result = _sut.CalculateStepDates(BaseDate, steps);

        var orders = result.Select(r => r.StepOrder).ToList();
        orders.Should().BeInAscendingOrder();
    }

    // -----------------------------------------------------------------------
    // Large delay values
    // -----------------------------------------------------------------------

    [Fact(DisplayName = "Step with delay 365 days is scheduled one year after previous step")]
    public void CalculateStepDates_LargeDelay_ScheduledCorrectly()
    {
        var steps = new[]
        {
            new StepScheduleInput(1, 0),
            new StepScheduleInput(2, 365)
        };

        var result = _sut.CalculateStepDates(BaseDate, steps);

        result[1].ScheduledAt.Should().Be(BaseDate.AddDays(365));
    }
}
