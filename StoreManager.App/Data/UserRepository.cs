using Microsoft.Data.Sqlite;
using StoreManager.App.Models;

namespace StoreManager.App.Data;

internal sealed class UserRepository(SqliteConnectionFactory database)
{
    public bool AnyUsers()
    {
        using var c = database.Create(); using var q = c.CreateCommand(); q.CommandText = "SELECT EXISTS(SELECT 1 FROM Utilisateur);"; return Convert.ToInt64(q.ExecuteScalar()) != 0;
    }

    public (long Id, string Name, string Username, UserRole Role)? Authenticate(string username, string password, Services.PasswordHasher hasher)
    {
        using var c = database.Create(); using var q = c.CreateCommand();
        q.CommandText = "SELECT UtilisateurId,Nom,Identifiant,MotDePasseHash,Sel,ParametresHachage,Role,Actif FROM Utilisateur WHERE Identifiant=$u COLLATE NOCASE;";
        q.Parameters.AddWithValue("$u", username.Trim());
        using var r = q.ExecuteReader();
        if (!r.Read()) return null;
        var active = r.GetInt32(7) == 1; var hash = (byte[])r[3]; var salt = (byte[])r[4]; var parameters = r.GetString(5);
        if (!active || !hasher.Verify(password, hash, salt, parameters)) return null;
        return (r.GetInt64(0), r.GetString(1), r.GetString(2), Enum.Parse<UserRole>(r.GetString(6)));
    }

    public long Create(string name, string username, UserRole role, bool active, string password, long? actingUserId, bool firstAdmin, Services.PasswordHasher hasher)
    {
        var derived = hasher.Hash(password);
        using var c = database.Create(); using var t = c.BeginTransaction();
        using var q = c.CreateCommand(); q.Transaction = t;
        q.CommandText = "INSERT INTO Utilisateur(Nom,Identifiant,MotDePasseHash,Sel,ParametresHachage,Role,Actif) VALUES($name,$username,$hash,$salt,$parameters,$role,$active); SELECT last_insert_rowid();";
        q.Parameters.AddWithValue("$name", name.Trim()); q.Parameters.AddWithValue("$username", username.Trim());
        q.Parameters.Add("$hash", SqliteType.Blob).Value = derived.Hash; q.Parameters.Add("$salt", SqliteType.Blob).Value = derived.Salt;
        q.Parameters.AddWithValue("$parameters", derived.Parameters); q.Parameters.AddWithValue("$role", role.ToString()); q.Parameters.AddWithValue("$active", active ? 1 : 0);
        var id = Convert.ToInt64(q.ExecuteScalar());
        WriteAudit(c, t, actingUserId, firstAdmin ? "PremierAdministrateurCree" : "UtilisateurCree", "Utilisateur", id.ToString(), $"Role={role};Actif={active}"); t.Commit(); return id;
    }

    public IReadOnlyList<UserSummary> List(string? search)
    {
        using var c = database.Create(); using var q = c.CreateCommand();
        q.CommandText = "SELECT UtilisateurId,Nom,Identifiant,Role,Actif FROM Utilisateur WHERE $search='' OR Nom LIKE $like OR Identifiant LIKE $like ORDER BY Nom COLLATE NOCASE;";
        q.Parameters.AddWithValue("$search", search?.Trim() ?? ""); q.Parameters.AddWithValue("$like", $"%{search?.Trim()}%");
        using var r = q.ExecuteReader(); var items = new List<UserSummary>(); while (r.Read()) items.Add(new(r.GetInt64(0), r.GetString(1), r.GetString(2), Enum.Parse<UserRole>(r.GetString(3)), r.GetInt32(4) == 1)); return items;
    }

    public void Update(long id, string name, string username, UserRole role, bool active, long actorId)
    {
        using var c = database.Create(); using var t = c.BeginTransaction();
        using var beforeQuery = c.CreateCommand(); beforeQuery.Transaction = t; beforeQuery.CommandText = "SELECT Nom,Identifiant,Role,Actif FROM Utilisateur WHERE UtilisateurId=$id;"; beforeQuery.Parameters.AddWithValue("$id", id);
        using var beforeReader = beforeQuery.ExecuteReader(); if (!beforeReader.Read()) throw new InvalidOperationException("Utilisateur introuvable.");
        var before = new { Nom = beforeReader.GetString(0), Identifiant = beforeReader.GetString(1), Role = beforeReader.GetString(2), Actif = beforeReader.GetInt32(3) == 1 }; beforeReader.Close();
        using var q = c.CreateCommand(); q.Transaction = t;
        q.CommandText = "UPDATE Utilisateur SET Nom=$name,Identifiant=$username,Role=$role,Actif=$active WHERE UtilisateurId=$id;";
        q.Parameters.AddWithValue("$name", name.Trim()); q.Parameters.AddWithValue("$username", username.Trim()); q.Parameters.AddWithValue("$role", role.ToString()); q.Parameters.AddWithValue("$active", active ? 1 : 0); q.Parameters.AddWithValue("$id", id);
        if (q.ExecuteNonQuery() != 1) throw new InvalidOperationException("Utilisateur introuvable.");
        var after = new { Nom = name.Trim(), Identifiant = username.Trim(), Role = role.ToString(), Actif = active };
        WriteAudit(c, t, actorId, "UtilisateurModifie", "Utilisateur", id.ToString(), System.Text.Json.JsonSerializer.Serialize(new { Avant = before, Apres = after })); t.Commit();
    }

    public void ResetPassword(long id, string password, long actorId, Services.PasswordHasher hasher)
    {
        var derived = hasher.Hash(password); using var c = database.Create(); using var t = c.BeginTransaction(); using var q = c.CreateCommand(); q.Transaction = t;
        q.CommandText = "UPDATE Utilisateur SET MotDePasseHash=$hash,Sel=$salt,ParametresHachage=$parameters WHERE UtilisateurId=$id;";
        q.Parameters.Add("$hash", SqliteType.Blob).Value = derived.Hash; q.Parameters.Add("$salt", SqliteType.Blob).Value = derived.Salt; q.Parameters.AddWithValue("$parameters", derived.Parameters); q.Parameters.AddWithValue("$id", id);
        if (q.ExecuteNonQuery() != 1) throw new InvalidOperationException("Utilisateur introuvable.");
        WriteAudit(c, t, actorId, "MotDePasseReinitialise", "Utilisateur", id.ToString(), "Réinitialisation administrateur."); t.Commit();
    }

    public int CountActiveAdministrators(long? excludingId = null)
    {
        using var c = database.Create(); using var q = c.CreateCommand(); q.CommandText = "SELECT COUNT(*) FROM Utilisateur WHERE Role='Administrateur' AND Actif=1 AND ($id IS NULL OR UtilisateurId<>$id);"; q.Parameters.AddWithValue("$id", excludingId is null ? DBNull.Value : excludingId.Value); return Convert.ToInt32(q.ExecuteScalar());
    }

    public void LogAuthentication(long? userId, string username, bool succeeded)
    {
        using var c=database.Create(); using var t=c.BeginTransaction();
        WriteAudit(c,t,userId,succeeded?"ConnexionReussie":"EchecConnexion","Utilisateur",username,"Événement d'authentification; aucun secret enregistré."); t.Commit();
    }

    public void LogLogout(long userId) { using var c=database.Create(); using var t=c.BeginTransaction(); WriteAudit(c,t,userId,"Deconnexion","Utilisateur",userId.ToString(),"Fin de session."); t.Commit(); }

    internal static void WriteAudit(SqliteConnection c, SqliteTransaction t, long? actorId, string action, string entity, string? entityId, string? details)
    {
        using var q = c.CreateCommand(); q.Transaction = t; q.CommandText = "INSERT INTO JournalAudit(AuteurId,DateHeureUtc,Action,Entite,EntiteId,Details) VALUES($actor,$date,$action,$entity,$id,$details);";
        q.Parameters.AddWithValue("$actor", actorId is null ? DBNull.Value : actorId.Value); q.Parameters.AddWithValue("$date", DateTime.UtcNow.ToString("O")); q.Parameters.AddWithValue("$action", action); q.Parameters.AddWithValue("$entity", entity); q.Parameters.AddWithValue("$id", entityId is null ? DBNull.Value : entityId); q.Parameters.AddWithValue("$details", details is null ? DBNull.Value : details); q.ExecuteNonQuery();
    }
}
