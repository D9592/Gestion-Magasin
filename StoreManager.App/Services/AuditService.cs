using StoreManager.App.Data;
using StoreManager.App.Models;

namespace StoreManager.App.Services;

internal sealed class AuditService(AuditRepository repository)
{
    public IReadOnlyList<AuditEntry> List(UserSession actor, string? search = null, int limit = 500)
    { UserManagementService.RequireAdmin(actor); return repository.List(search, limit); }
}
