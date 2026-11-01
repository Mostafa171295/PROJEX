using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;

namespace Projex.Desktop;

/// <summary>Read-only adapter for the authenticated PROJEX Gantt API contract.</summary>
public sealed class GanttApiClient : IDisposable
{
    private readonly HttpClient _http;
    private static readonly JsonSerializerOptions JsonOptions = new() { PropertyNameCaseInsensitive = true };

    public GanttApiClient(Uri baseAddress, string bearerToken)
    {
        if (!baseAddress.IsAbsoluteUri || (baseAddress.Scheme != Uri.UriSchemeHttps && baseAddress.Host != "localhost" && baseAddress.Host != "127.0.0.1"))
            throw new ArgumentException("Use HTTPS for remote API connections. HTTP is permitted only for localhost.", nameof(baseAddress));
        if (string.IsNullOrWhiteSpace(bearerToken)) throw new ArgumentException("A bearer access token is required.", nameof(bearerToken));
        _http = new HttpClient { BaseAddress = new Uri(baseAddress.ToString().TrimEnd('/') + "/") };
        _http.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", bearerToken.Trim());
        _http.Timeout = TimeSpan.FromSeconds(30);
    }

    public async Task<GanttScheduleDto> GetScheduleAsync(Guid projectId, Guid? baselineId = null, CancellationToken cancellationToken = default)
    {
        if (projectId == Guid.Empty) throw new ArgumentException("A valid project ID is required.", nameof(projectId));
        var path = $"api/v1/projects/{projectId}/planning/schedule/gantt";
        if (baselineId.HasValue) path += $"?baselineId={baselineId.Value}";
        using var response = await _http.GetAsync(path, cancellationToken);
        if (!response.IsSuccessStatusCode)
        {
            var detail = await response.Content.ReadAsStringAsync(cancellationToken);
            var message = response.StatusCode switch
            {
                HttpStatusCode.Unauthorized => "Authentication failed. Check the access token.",
                HttpStatusCode.Forbidden => "Access denied. The account cannot view planning data for this project.",
                HttpStatusCode.NotFound => "Project or requested baseline was not found.",
                _ => $"API request failed ({(int)response.StatusCode}): {detail}"
            };
            throw new HttpRequestException(message, null, response.StatusCode);
        }
        return await response.Content.ReadFromJsonAsync<GanttScheduleDto>(JsonOptions, cancellationToken)
            ?? throw new InvalidDataException("The Gantt API returned an empty response.");
    }

    public async Task<GanttDurationUpdateResult> UpdateDurationAsync(Guid projectId, Guid activityId, int durationMinutes, long expectedRowVersion, CancellationToken cancellationToken = default)
    {
        if (projectId == Guid.Empty || activityId == Guid.Empty) throw new ArgumentException("Project and activity IDs are required.");
        if (durationMinutes < 0) throw new ArgumentOutOfRangeException(nameof(durationMinutes));
        var path = $"api/v1/projects/{projectId}/planning/schedule/activities/{activityId}";
        using var response = await _http.PatchAsJsonAsync(path, new { durationMinutes, expectedRowVersion }, cancellationToken);
        if (!response.IsSuccessStatusCode)
        {
            var detail = await response.Content.ReadAsStringAsync(cancellationToken);
            var message = response.StatusCode switch
            {
                HttpStatusCode.Unauthorized => "Authentication expired. Reconnect with a valid access token.",
                HttpStatusCode.Forbidden => "You do not have permission to edit this project's schedule.",
                HttpStatusCode.Conflict => "This activity changed on the server. Reload the schedule before editing again.",
                HttpStatusCode.NotFound => "The activity no longer exists in this project.",
                HttpStatusCode.BadRequest => $"The API rejected this duration: {detail}",
                _ => $"Duration update failed ({(int)response.StatusCode}): {detail}"
            };
            throw new HttpRequestException(message, null, response.StatusCode);
        }
        return await response.Content.ReadFromJsonAsync<GanttDurationUpdateResult>(JsonOptions, cancellationToken)
            ?? throw new InvalidDataException("The duration update returned an empty response.");
    }

    public void Dispose() => _http.Dispose();
}

public sealed class GanttScheduleDto
{
    public Guid ProjectId { get; set; }
    public string ScheduleStatus { get; set; } = "Unknown";
    public bool RequiresRecalculation { get; set; }
    public bool HasCalculatedDates { get; set; }
    public GanttBaselineDto? Baseline { get; set; }
    public List<GanttActivityDto> Activities { get; set; } = [];
    public List<GanttRelationshipDto> Relationships { get; set; } = [];
}
public sealed class GanttBaselineDto { public Guid Id { get; set; } public string Name { get; set; } = ""; public int Version { get; set; } public string Status { get; set; } = ""; }
public sealed class GanttActivityDto
{
    public Guid Id { get; set; }
    public string ActivityCode { get; set; } = "";
    public string Name { get; set; } = "";
    public string WbsCode { get; set; } = "";
    public string WbsName { get; set; } = "";
    public int DurationMinutes { get; set; }
    public DateTimeOffset? PlannedStart { get; set; }
    public DateTimeOffset? PlannedFinish { get; set; }
    public decimal PercentComplete { get; set; }
    public string Status { get; set; } = "";
    public bool IsCritical { get; set; }
    public decimal TotalFloatMinutes { get; set; }
    public long RowVersion { get; set; }
    public DateTimeOffset? BaselineStart { get; set; }
    public DateTimeOffset? BaselineFinish { get; set; }
    public decimal? StartVarianceCalendarDays { get; set; }
    public decimal? FinishVarianceCalendarDays { get; set; }
}
public sealed class GanttRelationshipDto
{
    public Guid PredecessorActivityId { get; set; }
    public Guid SuccessorActivityId { get; set; }
    public string Type { get; set; } = "FS";
    public int LagMinutes { get; set; }
}

public sealed class GanttDurationUpdateResult
{
    public Guid Id { get; set; }
    public int DurationMinutes { get; set; }
    public long RowVersion { get; set; }
    public string ScheduleStatus { get; set; } = "Stale";
    public bool RequiresRecalculation { get; set; } = true;
}
