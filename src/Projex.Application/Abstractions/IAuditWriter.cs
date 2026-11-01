namespace Projex.Application.Abstractions;

/// <summary>
/// Stages an audit record in the current unit of work. It is persisted by the caller's SaveChanges,
/// so the business change and its audit record commit or fail together. Never pass secrets in before/after.
/// </summary>
public interface IAuditWriter
{
    void Record(string action, string entityType, Guid? entityId, object? before = null, object? after = null, Guid? actorUserId = null);
}
