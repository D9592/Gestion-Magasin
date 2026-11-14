using StoreManager.App.Models;

namespace StoreManager.App.Data;

internal sealed class AuditRepository(SqliteConnectionFactory database)
{
    public IReadOnlyList<AuditEntry> List(string? search, int limit = 500)
    {
        if (limit is < 1 or > 2000) throw new ArgumentOutOfRangeException(nameof(limit));
        using var c = database.Create(); using var q = c.CreateCommand();
        q.CommandText = "SELECT a.JournalAuditId,a.AuteurId,COALESCE(u.Nom,'Système'),a.DateHeureUtc,a.Action,a.Entite,a.EntiteId,a.Details FROM JournalAudit a LEFT JOIN Utilisateur u ON u.UtilisateurId=a.AuteurId WHERE $search='' OR a.Action LIKE $like OR a.Entite LIKE $like OR a.EntiteId LIKE $like OR a.Details LIKE $like OR u.Nom LIKE $like ORDER BY a.JournalAuditId DESC LIMIT $limit;";
        q.Parameters.AddWithValue("$search", search?.Trim() ?? ""); q.Parameters.AddWithValue("$like", $"%{search?.Trim()}%"); q.Parameters.AddWithValue("$limit", limit);
        using var r = q.ExecuteReader(); var entries = new List<AuditEntry>();
        while (r.Read()) entries.Add(new(r.GetInt64(0), r.IsDBNull(1) ? null : r.GetInt64(1), r.GetString(2), DateTime.Parse(r.GetString(3), null, System.Globalization.DateTimeStyles.RoundtripKind), r.GetString(4), r.GetString(5), r.IsDBNull(6) ? null : r.GetString(6), r.IsDBNull(7) ? null : r.GetString(7)));
        return entries;
    }
}
