namespace Projex.Scheduling.Engine.Calendars;

/// <summary>Places CPM offsets on a real calendar using working minutes and the calendar time zone.</summary>
public sealed class WorkingCalendarEngine
{
    private const int SearchLimitDays = 36500;

    /// <summary>Maps a CPM start offset and moves boundary timestamps forward to the next valid working instant.</summary>
    public DateTimeOffset AddWorkingMinutesForStart(WorkingCalendarDefinition calendar, DateOnly anchorDate, decimal workingMinutes)
    {
        var placed = AddWorkingMinutes(calendar, anchorDate, workingMinutes);
        var local = TimeZoneInfo.ConvertTime(placed, calendar.TimeZone).DateTime;
        return ToOffset(calendar, FindForward(calendar, local));
    }

    public DateTimeOffset AddWorkingMinutes(WorkingCalendarDefinition calendar, DateOnly anchorDate, decimal workingMinutes)
    {
        ArgumentNullException.ThrowIfNull(calendar);
        if (workingMinutes == 0m)
            return ToOffset(calendar, FindForward(calendar, anchorDate.ToDateTime(TimeOnly.MinValue)));
        var anchor = anchorDate.ToDateTime(TimeOnly.MinValue);
        var cursor = workingMinutes > 0 ? FindForward(calendar, anchor) : FindBackward(calendar, anchor);
        var remaining = Math.Abs(workingMinutes);

        for (var day = 0; day < SearchLimitDays; day++)
        {
            var date = DateOnly.FromDateTime(cursor);
            var intervals = calendar.GetIntervals(date);
            if (workingMinutes > 0)
            {
                var interval = intervals.FirstOrDefault(x => cursor >= date.ToDateTime(x.Start) && cursor < date.ToDateTime(x.End));
                if (interval is null) { cursor = FindForward(calendar, date.AddDays(1).ToDateTime(TimeOnly.MinValue)); continue; }
                var end = date.ToDateTime(interval.End);
                var available = (decimal)(end - cursor).TotalMinutes;
                if (remaining <= available) return ToOffset(calendar, cursor.AddMinutes((double)remaining));
                remaining -= available;
                var nextInterval = intervals.FirstOrDefault(x => x.Start > interval.End);
                cursor = nextInterval is not null
                    ? date.ToDateTime(nextInterval.Start)
                    : FindForward(calendar, date.AddDays(1).ToDateTime(TimeOnly.MinValue));
            }
            else
            {
                var interval = intervals.LastOrDefault(x => cursor > date.ToDateTime(x.Start) && cursor <= date.ToDateTime(x.End));
                if (interval is null) { cursor = FindBackward(calendar, date.AddDays(-1).ToDateTime(TimeOnly.MaxValue)); continue; }
                var start = date.ToDateTime(interval.Start);
                var available = (decimal)(cursor - start).TotalMinutes;
                if (remaining <= available) return ToOffset(calendar, cursor.AddMinutes(-(double)remaining));
                remaining -= available;
                var previousInterval = intervals.LastOrDefault(x => x.End < interval.Start);
                cursor = previousInterval is not null
                    ? date.ToDateTime(previousInterval.End)
                    : FindBackward(calendar, date.AddDays(-1).ToDateTime(TimeOnly.MaxValue));
            }
        }
        throw new InvalidOperationException("Calendar calculation exceeded the 100-year search limit. Check the working week and exceptions.");
    }

    private static DateTime FindForward(WorkingCalendarDefinition calendar, DateTime candidate)
    {
        var local = DateTime.SpecifyKind(candidate, DateTimeKind.Unspecified);
        for (var i = 0; i < SearchLimitDays; i++, local = local.Date.AddDays(1))
        {
            var date = DateOnly.FromDateTime(local);
            foreach (var interval in calendar.GetIntervals(date))
            {
                var start = date.ToDateTime(interval.Start);
                var end = date.ToDateTime(interval.End);
                if (local < end) return local > start ? local : start;
            }
        }
        throw new InvalidOperationException("No working time found within 100 years after the requested date.");
    }

    private static DateTime FindBackward(WorkingCalendarDefinition calendar, DateTime candidate)
    {
        var local = DateTime.SpecifyKind(candidate, DateTimeKind.Unspecified);
        for (var i = 0; i < SearchLimitDays; i++, local = local.Date.AddTicks(-1))
        {
            var date = DateOnly.FromDateTime(local);
            foreach (var interval in calendar.GetIntervals(date).Reverse())
            {
                var start = date.ToDateTime(interval.Start);
                var end = date.ToDateTime(interval.End);
                if (local > start) return local < end ? local : end;
            }
        }
        throw new InvalidOperationException("No working time found within 100 years before the requested date.");
    }

    private static DateTimeOffset ToOffset(WorkingCalendarDefinition calendar, DateTime local)
    {
        local = DateTime.SpecifyKind(local, DateTimeKind.Unspecified);
        if (calendar.TimeZone.IsInvalidTime(local))
            throw new InvalidOperationException($"Calendar time {local:O} falls in a daylight-saving gap in {calendar.TimeZoneId}.");
        var offset = calendar.TimeZone.GetUtcOffset(local);
        return new DateTimeOffset(local, offset);
    }
}
