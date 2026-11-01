using Projex.Domain.Common;

namespace Projex.Domain.Platform;

public enum SubscriptionStatus { Trial, Active, PastDue, Suspended, Cancelled }

/// <summary>A tenant's licence: plan, seat limit and module entitlements (ERD platform.subscriptions).</summary>
public sealed class Subscription : Entity
{
    private Subscription() { }
    public Subscription(Guid tenantId, string planCode, SubscriptionStatus status, DateTimeOffset startsAt, DateTimeOffset? endsAt, int seatLimit, string? moduleEntitlementsJson = null)
    {
        if (tenantId == Guid.Empty) throw new ArgumentException("Tenant is required.", nameof(tenantId));
        TenantId = tenantId;
        Change(planCode, status, startsAt, endsAt, seatLimit, moduleEntitlementsJson);
    }

    public Guid TenantId { get; private set; }
    public string PlanCode { get; private set; } = string.Empty;
    public SubscriptionStatus Status { get; private set; }
    public DateTimeOffset StartsAt { get; private set; }
    public DateTimeOffset? EndsAt { get; private set; }
    public int SeatLimit { get; private set; }
    /// <summary>Controlled feature flags as a JSON object, e.g. {"costing":true}.</summary>
    public string ModuleEntitlements { get; private set; } = "{}";

    public void Change(string planCode, SubscriptionStatus status, DateTimeOffset startsAt, DateTimeOffset? endsAt, int seatLimit, string? moduleEntitlementsJson)
    {
        if (string.IsNullOrWhiteSpace(planCode) || planCode.Trim().Length > 50) throw new ArgumentException("A plan code of 1-50 characters is required.", nameof(planCode));
        if (seatLimit <= 0) throw new ArgumentOutOfRangeException(nameof(seatLimit), "The seat limit must be positive.");
        if (endsAt is not null && endsAt <= startsAt) throw new ArgumentException("The end date must be after the start date.", nameof(endsAt));
        var entitlements = string.IsNullOrWhiteSpace(moduleEntitlementsJson) ? "{}" : moduleEntitlementsJson.Trim();
        try { using var doc = System.Text.Json.JsonDocument.Parse(entitlements); if (doc.RootElement.ValueKind != System.Text.Json.JsonValueKind.Object) throw new ArgumentException("Module entitlements must be a JSON object."); }
        catch (System.Text.Json.JsonException) { throw new ArgumentException("Module entitlements must be valid JSON."); }
        PlanCode = planCode.Trim(); Status = status; StartsAt = startsAt; EndsAt = endsAt; SeatLimit = seatLimit; ModuleEntitlements = entitlements;
        Touch();
    }

    /// <summary>True when new users may be added: the subscription is live and not past its end date.</summary>
    public bool AllowsNewUsers(DateTimeOffset now) =>
        (Status is SubscriptionStatus.Trial or SubscriptionStatus.Active or SubscriptionStatus.PastDue) && (EndsAt is null || EndsAt > now);
}
