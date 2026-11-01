using Projex.Scheduling.Engine.Calendars;
using Xunit;

namespace Projex.Domain.Tests;

public sealed class WorkingCalendarEngineTests
{
    private const string StandardWeek = """
    {
      "monday": [{"start":"08:00","end":"12:00"},{"start":"13:00","end":"17:00"}],
      "tuesday": [{"start":"08:00","end":"12:00"},{"start":"13:00","end":"17:00"}],
      "wednesday": [{"start":"08:00","end":"12:00"},{"start":"13:00","end":"17:00"}],
      "thursday": [{"start":"08:00","end":"12:00"},{"start":"13:00","end":"17:00"}],
      "friday": [{"start":"08:00","end":"12:00"},{"start":"13:00","end":"17:00"}]
    }
    """;

    [Fact]
    public void AddsMinutesAcrossSplitShiftWithoutLosingLunchPeriod()
    {
        var calendar = WorkingCalendarDefinition.Parse("UTC", StandardWeek);
        var engine = new WorkingCalendarEngine();

        var result = engine.AddWorkingMinutes(calendar, new DateOnly(2026, 11, 2), 480m);

        Assert.Equal(new DateTimeOffset(2026, 11, 2, 17, 0, 0, TimeSpan.Zero), result);
    }

    [Fact]
    public void RollsRemainingMinutesIntoNextWorkingDay()
    {
        var calendar = WorkingCalendarDefinition.Parse("UTC", StandardWeek);
        var engine = new WorkingCalendarEngine();

        var result = engine.AddWorkingMinutes(calendar, new DateOnly(2026, 11, 2), 540m);

        Assert.Equal(new DateTimeOffset(2026, 11, 3, 9, 0, 0, TimeSpan.Zero), result);
    }

    [Fact]
    public void StartBoundaryMovesPastLunchButFinishBoundaryStaysAtShiftEnd()
    {
        var calendar = WorkingCalendarDefinition.Parse("UTC", StandardWeek);
        var engine = new WorkingCalendarEngine();
        var date = new DateOnly(2026, 11, 2);

        var finish = engine.AddWorkingMinutes(calendar, date, 240m);
        var nextStart = engine.AddWorkingMinutesForStart(calendar, date, 240m);

        Assert.Equal(new DateTimeOffset(2026, 11, 2, 12, 0, 0, TimeSpan.Zero), finish);
        Assert.Equal(new DateTimeOffset(2026, 11, 2, 13, 0, 0, TimeSpan.Zero), nextStart);
    }

    [Fact]
    public void CalendarExceptionCanTurnWorkingDayIntoHoliday()
    {
        var holiday = new CalendarExceptionDay(new DateOnly(2026, 11, 2), false, null, null);
        var calendar = WorkingCalendarDefinition.Parse("UTC", StandardWeek, [holiday]);
        var engine = new WorkingCalendarEngine();

        var result = engine.AddWorkingMinutes(calendar, new DateOnly(2026, 11, 2), 1m);

        Assert.Equal(new DateTimeOffset(2026, 11, 3, 8, 1, 0, TimeSpan.Zero), result);
    }

    [Fact]
    public void SupportsNegativeLeadMinutesAcrossPreviousWorkingDay()
    {
        var calendar = WorkingCalendarDefinition.Parse("UTC", StandardWeek);
        var engine = new WorkingCalendarEngine();

        var result = engine.AddWorkingMinutes(calendar, new DateOnly(2026, 11, 2), -60m);

        Assert.Equal(new DateTimeOffset(2026, 10, 30, 16, 0, 0, TimeSpan.Zero), result);
    }

    [Fact]
    public void RejectsMalformedCalendarJsonAsArgumentError()
    {
        Assert.Throws<ArgumentException>(() => WorkingCalendarDefinition.Parse("UTC", "{not-json}"));
    }

    [Fact]
    public void RejectsOverlappingIntervals()
    {
        const string invalid = "{\"monday\":[{\"start\":\"08:00\",\"end\":\"12:00\"},{\"start\":\"11:00\",\"end\":\"17:00\"}]}";
        Assert.Throws<ArgumentException>(() => WorkingCalendarDefinition.Parse("UTC", invalid));
    }
}
