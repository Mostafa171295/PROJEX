using System.Globalization;
using System.Text.Json;

namespace Projex.Scheduling.Engine.Calendars;

public sealed record WorkingInterval(TimeOnly Start, TimeOnly End);
public sealed record CalendarExceptionDay(DateOnly Date, bool IsWorkingDay, TimeOnly? Start, TimeOnly? End);

/// <summary>
/// Calendar JSON contract: { "monday": [{"start":"08:00","end":"12:00"}, ...], ... }.
/// Day keys are English day names; omitted days are non-working days.
/// </summary>
public sealed class WorkingCalendarDefinition
{
    private readonly IReadOnlyDictionary<DayOfWeek, IReadOnlyList<WorkingInterval>> _week;
    private readonly IReadOnlyDictionary<DateOnly, CalendarExceptionDay> _exceptions;

    private WorkingCalendarDefinition(string timeZoneId, TimeZoneInfo timeZone,
        IReadOnlyDictionary<DayOfWeek, IReadOnlyList<WorkingInterval>> week,
        IReadOnlyDictionary<DateOnly, CalendarExceptionDay> exceptions)
    {
        TimeZoneId = timeZoneId;
        TimeZone = timeZone;
        _week = week;
        _exceptions = exceptions;
    }

    public string TimeZoneId { get; }
    public TimeZoneInfo TimeZone { get; }

    public static WorkingCalendarDefinition Parse(string timeZoneId, string workingWeekJson,
        IEnumerable<CalendarExceptionDay>? exceptions = null)
    {
        if (string.IsNullOrWhiteSpace(timeZoneId)) throw new ArgumentException("Calendar time zone is required.", nameof(timeZoneId));
        TimeZoneInfo timeZone;
        try { timeZone = TimeZoneInfo.FindSystemTimeZoneById(timeZoneId); }
        catch (Exception ex) when (ex is TimeZoneNotFoundException or InvalidTimeZoneException or ArgumentException)
        { throw new ArgumentException($"Unknown or invalid calendar time zone '{timeZoneId}'.", nameof(timeZoneId), ex); }
        using var document = ParseDocument(string.IsNullOrWhiteSpace(workingWeekJson) ? "{}" : workingWeekJson);
        if (document.RootElement.ValueKind != JsonValueKind.Object)
            throw new ArgumentException("Working week JSON must be an object.", nameof(workingWeekJson));

        var week = new Dictionary<DayOfWeek, IReadOnlyList<WorkingInterval>>();
        foreach (var property in document.RootElement.EnumerateObject())
        {
            if (!Enum.TryParse<DayOfWeek>(property.Name, true, out var day))
                throw new ArgumentException($"Unknown calendar day '{property.Name}'. Use English day names.", nameof(workingWeekJson));
            if (property.Value.ValueKind != JsonValueKind.Array)
                throw new ArgumentException($"Calendar day '{property.Name}' must contain an array of intervals.", nameof(workingWeekJson));

            var intervals = new List<WorkingInterval>();
            foreach (var item in property.Value.EnumerateArray())
            {
                if (item.ValueKind != JsonValueKind.Object || !item.TryGetProperty("start", out var startElement) || !item.TryGetProperty("end", out var endElement))
                    throw new ArgumentException($"Each interval for '{property.Name}' requires start and end (HH:mm).", nameof(workingWeekJson));
                if (startElement.ValueKind != JsonValueKind.String || endElement.ValueKind != JsonValueKind.String ||
                    !TimeOnly.TryParse(startElement.GetString(), CultureInfo.InvariantCulture, DateTimeStyles.None, out var start) ||
                    !TimeOnly.TryParse(endElement.GetString(), CultureInfo.InvariantCulture, DateTimeStyles.None, out var end) || end <= start)
                    throw new ArgumentException($"Invalid working interval for '{property.Name}'. End must be after start.", nameof(workingWeekJson));
                intervals.Add(new WorkingInterval(start, end));
            }
            intervals.Sort((a, b) => a.Start.CompareTo(b.Start));
            for (var i = 1; i < intervals.Count; i++)
                if (intervals[i].Start < intervals[i - 1].End)
                    throw new ArgumentException($"Working intervals overlap on '{property.Name}'.", nameof(workingWeekJson));
            if (!week.TryAdd(day, intervals))
                throw new ArgumentException($"Calendar day '{property.Name}' is defined more than once.", nameof(workingWeekJson));
        }

        var exceptionMap = new Dictionary<DateOnly, CalendarExceptionDay>();
        foreach (var exception in exceptions ?? [])
        {
            if (exception.IsWorkingDay && exception.Start.HasValue != exception.End.HasValue)
                throw new ArgumentException($"Calendar exception {exception.Date} must specify both start and end or neither.");
            if (exception.Start.HasValue && exception.End <= exception.Start)
                throw new ArgumentException($"Calendar exception {exception.Date} has an invalid time range.");
            if (!exceptionMap.TryAdd(exception.Date, exception))
                throw new ArgumentException($"Duplicate calendar exception for {exception.Date}.");
        }
        return new WorkingCalendarDefinition(timeZoneId, timeZone, week, exceptionMap);
    }

    private static JsonDocument ParseDocument(string json)
    {
        try { return JsonDocument.Parse(json); }
        catch (JsonException ex) { throw new ArgumentException("Working week JSON must be valid JSON.", "workingWeekJson", ex); }
    }

    public IReadOnlyList<WorkingInterval> GetIntervals(DateOnly date)
    {
        if (_exceptions.TryGetValue(date, out var exception))
        {
            if (!exception.IsWorkingDay) return [];
            if (exception.Start.HasValue) return [new WorkingInterval(exception.Start.Value, exception.End!.Value)];
            return _week.TryGetValue(date.DayOfWeek, out var exceptionIntervals) ? exceptionIntervals : [];
        }
        return _week.TryGetValue(date.DayOfWeek, out var intervals) ? intervals : [];
    }
}
