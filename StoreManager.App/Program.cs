using StoreManager.App.Forms;
using StoreManager.App.Helpers;
using StoreManager.App.Data;
using StoreManager.App.Services;

namespace StoreManager.App;

internal static class Program
{
    [STAThread]
    private static void Main(string[] args)
    {
        ApplicationConfiguration.Initialize();
        GlobalExceptionHandler.Register();
        using var mutex=new Mutex(true,@"Local\NOVA_GestionMagasin",out var isNew);
        if(!isNew){MessageBox.Show("NOVA est déjà ouvert sur ce poste.","NOVA",MessageBoxButtons.OK,MessageBoxIcon.Information);return;}
        string? debugDatabasePath=null;
#if DEBUG
        var debugDirectory=Environment.GetEnvironmentVariable("NOVA_DEBUG_DATA_DIRECTORY");
        if(!string.IsNullOrWhiteSpace(debugDirectory))debugDatabasePath=Path.Combine(debugDirectory,"magasin.sqlite");
#endif
        var dataDirectoryIndex=Array.IndexOf(args,"--data-directory");
        if(dataDirectoryIndex>=0)
        {
            if(dataDirectoryIndex+1>=args.Length||string.IsNullOrWhiteSpace(args[dataDirectoryIndex+1]))throw new ArgumentException("Le chemin suivant --data-directory est obligatoire.");
            debugDatabasePath=Path.Combine(Path.GetFullPath(args[dataDirectoryIndex+1]),"magasin.sqlite");
        }
        var database=new SqliteConnectionFactory(debugDatabasePath);
        database.Initialize();
        if(database.PreservedLegacyDatabasePath is not null)
            MessageBox.Show($"Une base existante a été repérée :\n{database.PreservedLegacyDatabasePath}\n\nSon format n’est pas importé automatiquement. Le fichier est conservé intact; GestionMagasin utilise sa base magasin.sqlite. Demandez une migration dédiée avant de poursuivre si ces données doivent être reprises.","Base existante conservée",MessageBoxButtons.OK,MessageBoxIcon.Warning);
        var backups=new DatabaseBackupService(database);
        if(args.Contains("--backup-before-update",StringComparer.Ordinal))
        {
            try { backups.CreateMaintenanceBackup(); }
            catch (Exception) { MessageBox.Show("La sauvegarde avant mise à jour a échoué. L’installation est interrompue et les données existantes sont conservées.","Mise à jour",MessageBoxButtons.OK,MessageBoxIcon.Error); Environment.ExitCode=1; }
            return;
        }
        var passwordHasher=new PasswordHasher();
        var userRepository=new UserRepository(database);
        var catalogRepository=new CatalogRepository(database);
        var authentication=new AuthenticationService(userRepository,passwordHasher);
        var users=new UserManagementService(userRepository,passwordHasher);
        var catalog=new CatalogManagementService(catalogRepository);
        var stockRepository=new StockRepository(database);
        var stock=new StockManagementService(stockRepository,catalog);
        var settings=new StoreSettingsRepository(database);
        var salesRepository=new SalesRepository(database,settings);
        var sales=new SalesService(salesRepository,catalog);
        var reporting=new ReportingService(new ReportingRepository(database),settings);
        var audit=new AuditService(new AuditRepository(database));
        try { backups.CreateAutomaticDailyBackup(settings.TimeZone()); }
        catch (Exception) { MessageBox.Show("La sauvegarde automatique du jour a échoué. Les sauvegardes existantes sont conservées. Vérifiez les droits d’accès au dossier de données et relancez une sauvegarde depuis l’administration.","Sauvegarde",MessageBoxButtons.OK,MessageBoxIcon.Warning); }
        if(authentication.NeedsInitialAdministrator)
        {
            using var setup=new InitialAdministratorForm(authentication);
            if(setup.ShowDialog()!=DialogResult.OK)return;
        }
        while(true)
        {
            using var login=new LoginForm(authentication);
            if(login.ShowDialog()!=DialogResult.OK||login.Session is null)return;
            using var main=new MainForm(login.Session,users,catalog,stock,sales,reporting,authentication,audit,backups);
            if(main.ShowDialog()!=DialogResult.Retry)return;
        }
    }
}
