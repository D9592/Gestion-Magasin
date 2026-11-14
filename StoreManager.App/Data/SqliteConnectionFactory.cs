using Microsoft.Data.Sqlite;
using System.Reflection;
using System.Security.AccessControl;
using System.Security.Principal;

namespace StoreManager.App.Data;

internal sealed class SqliteConnectionFactory
{
    private readonly string _connectionString;
    private readonly string? _legacyDatabasePath;
    public string DatabasePath { get; }
    public string? PreservedLegacyDatabasePath { get; }

    public SqliteConnectionFactory(string? databasePath = null)
    {
        var useDefaultPath = databasePath is null;
        var localAppData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        if (useDefaultPath) _legacyDatabasePath = Path.Combine(localAppData, "NOVA", "GestionMagasin", "magasin.sqlite");
        databasePath ??= Path.Combine(localAppData, "GestionMagasin", "magasin.sqlite");
        var directory = Path.GetDirectoryName(databasePath) ?? throw new ArgumentException("Chemin de base invalide.", nameof(databasePath));
        Directory.CreateDirectory(directory);
        if (useDefaultPath && OperatingSystem.IsWindows()) SecureDataDirectory(directory);
        DatabasePath = Path.GetFullPath(databasePath);
        var possibleLegacy = Path.Combine(directory, "gestion-magasin.db");
        if (!string.Equals(possibleLegacy, DatabasePath, StringComparison.OrdinalIgnoreCase) && File.Exists(possibleLegacy) && !File.Exists(DatabasePath)) PreservedLegacyDatabasePath = possibleLegacy;
        _connectionString = new SqliteConnectionStringBuilder { DataSource = DatabasePath, ForeignKeys = true, Pooling = true, Mode = SqliteOpenMode.ReadWriteCreate }.ToString();
    }

    private static void SecureDataDirectory(string path)
    {
        var currentUser = WindowsIdentity.GetCurrent().User ?? throw new InvalidOperationException("Le compte Windows courant ne possède pas de SID.");
        var acl = new DirectoryInfo(path).GetAccessControl(AccessControlSections.Access);
        var ownerRule = new FileSystemAccessRule(currentUser, FileSystemRights.FullControl, InheritanceFlags.ContainerInherit | InheritanceFlags.ObjectInherit, PropagationFlags.None, AccessControlType.Allow);
        var hasOwnerRule = acl.GetAccessRules(true, true, typeof(SecurityIdentifier)).Cast<FileSystemAccessRule>()
            .Any(rule => rule.IdentityReference == currentUser && rule.AccessControlType == AccessControlType.Allow && (rule.FileSystemRights & FileSystemRights.FullControl) == FileSystemRights.FullControl && rule.InheritanceFlags == ownerRule.InheritanceFlags);
        if (!hasOwnerRule) acl.AddAccessRule(ownerRule);
        new DirectoryInfo(path).SetAccessControl(acl);
    }

    public SqliteConnection Create()
    {
        var connection = new SqliteConnection(_connectionString);
        connection.Open();
        using var command = connection.CreateCommand();
        command.CommandText = "PRAGMA foreign_keys=ON; PRAGMA busy_timeout=5000;";
        command.ExecuteNonQuery();
        return connection;
    }

    public void Initialize()
    {
        MigrateLegacyDatabaseIfNeeded();
        using var connection = Create();
        using (var setup = connection.CreateCommand())
        {
            setup.CommandText = "PRAGMA journal_mode=WAL; CREATE TABLE IF NOT EXISTS VersionSchema (Numero INTEGER PRIMARY KEY, DateApplicationUtc TEXT NOT NULL, Description TEXT NOT NULL);";
            setup.ExecuteNonQuery();
        }

        var resources = Assembly.GetExecutingAssembly().GetManifestResourceNames()
            .Where(name => name.Contains("Data.Migrations.", StringComparison.Ordinal) && name.EndsWith(".sql", StringComparison.Ordinal))
            .OrderBy(name => name, StringComparer.Ordinal).ToArray();
        foreach (var resource in resources)
        {
            var file = resource[(resource.LastIndexOf("Migrations.", StringComparison.Ordinal) + "Migrations.".Length)..];
            file = Path.GetFileNameWithoutExtension(file);
            if (!int.TryParse(file.Split('_')[0], out var version)) continue;
            using var check = connection.CreateCommand();
            check.CommandText = "SELECT EXISTS(SELECT 1 FROM VersionSchema WHERE Numero=$version);";
            check.Parameters.AddWithValue("$version", version);
            if (Convert.ToInt64(check.ExecuteScalar()) != 0) continue;

            using var stream = Assembly.GetExecutingAssembly().GetManifestResourceStream(resource) ?? throw new InvalidOperationException($"Migration introuvable: {resource}");
            using var reader = new StreamReader(stream);
            var sql = reader.ReadToEnd();
            using var transaction = connection.BeginTransaction();
            using var migration = connection.CreateCommand();
            migration.Transaction = transaction;
            migration.CommandText = sql;
            migration.ExecuteNonQuery();
            using var record = connection.CreateCommand();
            record.Transaction = transaction;
            record.CommandText = "INSERT INTO VersionSchema(Numero,DateApplicationUtc,Description) VALUES($v,$date,$description);";
            record.Parameters.AddWithValue("$v", version);
            record.Parameters.AddWithValue("$date", DateTime.UtcNow.ToString("O"));
            record.Parameters.AddWithValue("$description", file);
            record.ExecuteNonQuery();
            transaction.Commit();
        }
        VerifyIntegrity(connection);
    }

    private static void VerifyIntegrity(SqliteConnection connection)
    {
        using (var quick = connection.CreateCommand())
        {
            quick.CommandText = "PRAGMA quick_check;";
            if (!string.Equals(Convert.ToString(quick.ExecuteScalar()), "ok", StringComparison.Ordinal)) throw new InvalidDataException("La base SQLite a échoué au contrôle d'intégrité.");
        }
        using var foreign = connection.CreateCommand(); foreign.CommandText = "PRAGMA foreign_key_check;";
        using var reader = foreign.ExecuteReader();
        if (reader.Read()) throw new InvalidDataException("La base SQLite contient une relation étrangère invalide.");
    }

    private void MigrateLegacyDatabaseIfNeeded()
    {
        if (_legacyDatabasePath is null || File.Exists(DatabasePath) || !File.Exists(_legacyDatabasePath)) return;
        var temporary = Path.Combine(Path.GetDirectoryName(DatabasePath)!, $".migration_{Guid.NewGuid():N}.sqlite");
        try
        {
            using (var source = new SqliteConnection(new SqliteConnectionStringBuilder { DataSource = _legacyDatabasePath, Mode = SqliteOpenMode.ReadWrite, Pooling = false, ForeignKeys = true }.ToString()))
            using (var target = new SqliteConnection(new SqliteConnectionStringBuilder { DataSource = temporary, Mode = SqliteOpenMode.ReadWriteCreate, Pooling = false, ForeignKeys = true }.ToString()))
            {
                source.Open(); target.Open(); source.BackupDatabase(target);
                using var check = target.CreateCommand(); check.CommandText = "PRAGMA quick_check;";
                if (!string.Equals(Convert.ToString(check.ExecuteScalar()), "ok", StringComparison.Ordinal)) throw new InvalidDataException("La base héritée est incohérente; le fichier d'origine est conservé.");
            }
            SqliteConnection.ClearAllPools(); File.Move(temporary, DatabasePath);
        }
        finally { if (File.Exists(temporary)) File.Delete(temporary); }
    }
}
