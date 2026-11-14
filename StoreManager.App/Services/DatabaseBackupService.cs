using Microsoft.Data.Sqlite;
using StoreManager.App.Data;
using StoreManager.App.Models;

namespace StoreManager.App.Services;

internal sealed class DatabaseBackupService(SqliteConnectionFactory database)
{
    private static readonly SemaphoreSlim OperationGate = new(1, 1);
    private const int CurrentSchema = 3;
    private string BackupDirectory => Path.Combine(Path.GetDirectoryName(database.DatabasePath)!, "Backups");

    public string CreateManual(UserSession actor, string destination)
    {
        UserManagementService.RequireAdmin(actor);
        return CreateBackup(destination, actor.UserId, "SauvegardeManuelle");
    }

    public string CreateMaintenanceBackup()
    {
        Directory.CreateDirectory(BackupDirectory);
        return CreateBackup(Path.Combine(BackupDirectory, $"pre_mise_a_jour_{DateTime.UtcNow:yyyyMMdd_HHmmss_fff}.sqlite"), null, "SauvegardeAvantMiseAJour");
    }

    public string CreateSafetyBackup(UserSession actor)
    {
        UserManagementService.RequireAdmin(actor);
        Directory.CreateDirectory(BackupDirectory);
        return CreateBackup(Path.Combine(BackupDirectory, $"securite_{DateTime.UtcNow:yyyyMMdd_HHmmss_fff}.sqlite"), actor.UserId, "SauvegardeSecurite");
    }

    public bool CreateAutomaticDailyBackup(TimeZoneInfo timeZone)
    {
        var businessDate = TimeZoneInfo.ConvertTimeFromUtc(DateTime.UtcNow, timeZone).ToString("yyyy-MM-dd", System.Globalization.CultureInfo.InvariantCulture);
        using (var c = database.Create()) using (var q = c.CreateCommand())
        {
            q.CommandText = "SELECT Valeur FROM Parametre WHERE Cle='DerniereSauvegardeAutomatiqueReussie';";
            if (string.Equals(q.ExecuteScalar() as string, businessDate, StringComparison.Ordinal)) return false;
        }
        Directory.CreateDirectory(BackupDirectory);
        var target = Path.Combine(BackupDirectory, $"automatique_{DateTime.UtcNow:yyyyMMdd_HHmmss_fff}.sqlite");
        CreateBackup(target, null, "SauvegardeAutomatique");
        using (var c = database.Create()) using (var t = c.BeginTransaction())
        {
            using var q = c.CreateCommand(); q.Transaction = t; q.CommandText = "INSERT INTO Parametre(Cle,Valeur,ModifieLeUtc) VALUES('DerniereSauvegardeAutomatiqueReussie',$date,$utc) ON CONFLICT(Cle) DO UPDATE SET Valeur=excluded.Valeur,ModifieLeUtc=excluded.ModifieLeUtc;";
            q.Parameters.AddWithValue("$date", businessDate); q.Parameters.AddWithValue("$utc", DateTime.UtcNow.ToString("O")); q.ExecuteNonQuery(); t.Commit();
        }
        PruneAutomaticBackups();
        return true;
    }

    public string Restore(UserSession actor, string source)
    {
        UserManagementService.RequireAdmin(actor);
        if (string.IsNullOrWhiteSpace(source) || !File.Exists(source)) throw new FileNotFoundException("Le fichier de sauvegarde est introuvable.", source);
        if (string.Equals(Path.GetFullPath(source), database.DatabasePath, StringComparison.OrdinalIgnoreCase)) throw new ArgumentException("La base active ne peut pas être utilisée comme fichier de restauration.", nameof(source));
        OperationGate.Wait();
        string? staged = null; string? rollback = null; var databaseMoved = false;
        try
        {
            VerifyDatabase(source);
            Audit(actor.UserId, "RestaurationDemandee", Path.GetFileName(source), "Sauvegarde vérifiée avant restauration.");
            Directory.CreateDirectory(BackupDirectory);
            var safety = CreateBackupCore(Path.Combine(BackupDirectory, $"securite_{DateTime.UtcNow:yyyyMMdd_HHmmss_fff}.sqlite"), actor.UserId, "SauvegardeSecurite");
            using (var c = database.Create()) using (var q = c.CreateCommand()) { q.CommandText = "PRAGMA wal_checkpoint(TRUNCATE);"; q.ExecuteNonQuery(); }
            SqliteConnection.ClearAllPools();
            var active = database.DatabasePath; var directory = Path.GetDirectoryName(active)!;
            staged = Path.Combine(directory, $".restore_{Guid.NewGuid():N}.sqlite"); rollback = Path.Combine(directory, $".rollback_{Guid.NewGuid():N}.sqlite");
            File.Copy(source, staged, false); VerifyDatabase(staged);
            File.Move(active, rollback); databaseMoved = true;
            try
            {
                File.Move(staged, active); staged = null;
                SqliteConnection.ClearAllPools(); database.Initialize(); VerifyDatabase(active);
                Audit(null, "RestaurationReussie", Path.GetFileName(source), $"Base restaurée par {actor.Name} (identifiant de session {actor.UserId}); sauvegarde de sécurité: {Path.GetFileName(safety)}.");
                File.Delete(rollback); rollback = null;
                return safety;
            }
            catch
            {
                SqliteConnection.ClearAllPools();
                if (File.Exists(active)) File.Delete(active);
                if (databaseMoved && rollback is not null && File.Exists(rollback)) { File.Move(rollback, active); rollback = null; }
                database.Initialize(); throw;
            }
        }
        catch (Exception ex)
        {
            try { Audit(actor.UserId, "EchecRestauration", Path.GetFileName(source), $"Restauration annulée; état précédent conservé. Type={ex.GetType().Name}."); } catch { /* Ne jamais masquer l'erreur principale. */ }
            throw;
        }
        finally
        {
            if (staged is not null && File.Exists(staged)) File.Delete(staged);
            if (rollback is not null && File.Exists(rollback) && File.Exists(database.DatabasePath)) File.Delete(rollback);
            OperationGate.Release();
        }
    }

    public static void VerifyDatabase(string path)
    {
        if (!File.Exists(path) || new FileInfo(path).Length == 0) throw new InvalidDataException("Le fichier de sauvegarde est absent ou vide.");
        try
        {
            var cs = new SqliteConnectionStringBuilder { DataSource = path, Mode = SqliteOpenMode.ReadOnly, Pooling = false }.ToString();
            using var c = new SqliteConnection(cs); c.Open();
            ScalarMustEqual(c, "PRAGMA quick_check;", "ok", "L'intégrité SQLite de la sauvegarde est invalide.");
            using (var q = c.CreateCommand()) { q.CommandText = "PRAGMA foreign_key_check;"; using var r = q.ExecuteReader(); if (r.Read()) throw new InvalidDataException("La sauvegarde contient une relation invalide."); }
            ScalarMustEqual(c, "SELECT CASE WHEN COALESCE(MAX(Numero),0)>=$v THEN 'ok' ELSE 'schema' END FROM VersionSchema;", "ok", "La version du schéma de cette sauvegarde n'est pas prise en charge.", CurrentSchema);
            ScalarMustEqual(c, "SELECT CASE WHEN EXISTS(SELECT 1 FROM VueStockCourant WHERE QuantiteStock<0) THEN 'ko' WHEN EXISTS(SELECT 1 FROM Vente v WHERE v.Total<>(SELECT COALESCE(SUM(l.SousTotal),0) FROM LigneVente l WHERE l.VenteId=v.VenteId)) THEN 'ko' WHEN EXISTS(SELECT 1 FROM Vente v WHERE (v.ModePaiement='Especes' AND (v.MontantRecu<v.Total OR v.MonnaieRendue<>v.MontantRecu-v.Total)) OR (v.ModePaiement<>'Especes' AND (v.MontantRecu<>v.Total OR v.MonnaieRendue<>0))) THEN 'ko' WHEN EXISTS(SELECT 1 FROM Vente v WHERE (v.Statut='Annulee')<>(EXISTS(SELECT 1 FROM AnnulationVente a WHERE a.VenteId=v.VenteId))) THEN 'ko' ELSE 'ok' END;", "ok", "La sauvegarde contient des données métier incohérentes.");
        }
        catch (SqliteException ex) { throw new InvalidDataException("Le fichier sélectionné n’est pas une sauvegarde SQLite valide.", ex); }
    }

    private string CreateBackup(string destination, long? actorId, string action)
    {
        OperationGate.Wait();
        try { return CreateBackupCore(destination, actorId, action); }
        finally { OperationGate.Release(); }
    }

    private string CreateBackupCore(string destination, long? actorId, string action)
    {
        string? temporary = null;
        try
        {
            var full = Path.GetFullPath(destination); if (string.Equals(full, database.DatabasePath, StringComparison.OrdinalIgnoreCase)) throw new ArgumentException("La sauvegarde ne peut pas remplacer la base principale.");
            Directory.CreateDirectory(Path.GetDirectoryName(full)!); temporary = full + ".tmp_" + Guid.NewGuid().ToString("N");
            Audit(actorId, action + "Demarree", Path.GetFileName(full), "Copie SQLite cohérente en cours.");
            using (var source = database.Create()) using (var target = new SqliteConnection(new SqliteConnectionStringBuilder { DataSource = temporary, Mode = SqliteOpenMode.ReadWriteCreate, Pooling = false }.ToString()))
            { target.Open(); source.BackupDatabase(target); }
            VerifyDatabase(temporary); File.Move(temporary, full, true); temporary = null;
            Audit(actorId, action, Path.GetFileName(full), "Sauvegarde créée et vérifiée."); return full;
        }
        catch (Exception ex)
        {
            try { Audit(actorId, action + "Echec", Path.GetFileName(destination), $"Type={ex.GetType().Name}; sauvegarde précédente préservée."); } catch { }
            throw new IOException("La sauvegarde a échoué. La base principale et les sauvegardes existantes sont préservées.", ex);
        }
        finally { if (temporary is not null && File.Exists(temporary)) File.Delete(temporary); }
    }

    private void Audit(long? actorId, string action, string entityId, string details)
    { using var c = database.Create(); using var t = c.BeginTransaction(); UserRepository.WriteAudit(c, t, actorId, action, "BaseDeDonnees", entityId, details); t.Commit(); }

    private void PruneAutomaticBackups()
    {
        var files = Directory.EnumerateFiles(BackupDirectory, "automatique_*.sqlite").Select(p => new FileInfo(p)).OrderByDescending(f => f.LastWriteTimeUtc).ToArray();
        foreach (var old in files.Skip(7)) try { old.Delete(); } catch (IOException) { } catch (UnauthorizedAccessException) { }
    }

    private static void ScalarMustEqual(SqliteConnection connection, string sql, string expected, string message, int parameter = 0)
    { using var q = connection.CreateCommand(); q.CommandText = sql; if (parameter != 0) q.Parameters.AddWithValue("$v", parameter); if (!string.Equals(Convert.ToString(q.ExecuteScalar(), System.Globalization.CultureInfo.InvariantCulture), expected, StringComparison.Ordinal)) throw new InvalidDataException(message); }
}
