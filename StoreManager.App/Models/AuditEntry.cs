namespace StoreManager.App.Models;

internal sealed record AuditEntry(long Id, long? AuthorId, string Author, DateTime TimestampUtc, string Action, string Entity, string? EntityId, string? Details);
