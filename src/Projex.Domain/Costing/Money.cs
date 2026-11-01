namespace Projex.Domain.Costing;

/// <summary>Shared money/quantity rules (SRS FR-COST-007: decimal only, fixed scale, no floating point).</summary>
public static class Money
{
    public const int Scale = 4;
    public static decimal Round(decimal value) => Math.Round(value, Scale, MidpointRounding.ToEven);
    public static decimal Extend(decimal quantity, decimal unitRate) => Round(quantity * unitRate);

    /// <summary>Format check only (three upper-case letters). Validation against the ISO 4217 list is a separate, open item.</summary>
    public static string NormalizeCurrency(string code)
    {
        var c = code?.Trim().ToUpperInvariant() ?? string.Empty;
        if (c.Length != 3 || !c.All(ch => ch is >= 'A' and <= 'Z')) throw new ArgumentException("Currency must be a three-letter code.", nameof(code));
        return c;
    }
}
