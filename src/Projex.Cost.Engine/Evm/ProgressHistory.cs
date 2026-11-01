namespace Projex.Cost.Engine.Evm;

/// <summary>Resolves an activity's percent complete as of a data date from its append-only update history (SRS FR-EVM-002).</summary>
public static class ProgressHistory
{
    /// <summary>
    /// The latest update whose data date is on or before <paramref name="asOf"/>; ties on data date go to the most recently recorded one.
    /// Returns null when no update existed yet by that date.
    /// </summary>
    public static decimal? PercentAsOf(IEnumerable<(DateOnly DataDate, DateTimeOffset RecordedAt, decimal Percent)> updates, DateOnly asOf)
    {
        (DateOnly DataDate, DateTimeOffset RecordedAt, decimal Percent)? best = null;
        foreach (var u in updates)
        {
            if (u.DataDate > asOf) continue;
            if (best is null || u.DataDate > best.Value.DataDate || (u.DataDate == best.Value.DataDate && u.RecordedAt > best.Value.RecordedAt)) best = u;
        }
        return best?.Percent;
    }
}
