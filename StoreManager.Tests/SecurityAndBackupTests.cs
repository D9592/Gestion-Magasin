using Microsoft.Data.Sqlite;
using StoreManager.App.Data;
using StoreManager.App.Models;
using StoreManager.App.Services;
using Xunit;

namespace StoreManager.Tests;

public sealed class SecurityAndBackupTests : IDisposable
{
    private readonly string _directory=Path.Combine(Path.GetTempPath(),"NOVA-security-tests",Guid.NewGuid().ToString("N"));
    private readonly SqliteConnectionFactory _database;private readonly AuthenticationService _auth;private readonly UserManagementService _users;private readonly CatalogManagementService _catalog;private readonly StockManagementService _stock;private readonly SalesService _sales;private readonly AuditService _audit;private readonly DatabaseBackupService _backups;private readonly UserSession _admin;private readonly UserSession _vendor;
    public SecurityAndBackupTests()
    {
        Directory.CreateDirectory(_directory);_database=new SqliteConnectionFactory(Path.Combine(_directory,"store.sqlite"));_database.Initialize();var repo=new UserRepository(_database);var hasher=new PasswordHasher();_auth=new AuthenticationService(repo,hasher);_users=new UserManagementService(repo,hasher);_catalog=new CatalogManagementService(new CatalogRepository(_database));_stock=new StockManagementService(new StockRepository(_database),_catalog);var settings=new StoreSettingsRepository(_database);_sales=new SalesService(new SalesRepository(_database,settings),_catalog);_audit=new AuditService(new AuditRepository(_database));_backups=new DatabaseBackupService(_database);_auth.CreateInitialAdministrator("Administrateur test","admin.security","MotDePasseAdmin!2026");_admin=_auth.Login("admin.security","MotDePasseAdmin!2026");_users.Create(_admin,"Vendeur test","vendor.security",UserRole.Vendeur,"MotDePasseVendeur!2026");_vendor=_auth.Login("vendor.security","MotDePasseVendeur!2026");
    }

    [Fact]
    public void AuditIsAdminOnlyImmutableAndContainsBeforeAfterWithoutSecrets()
    {
        _catalog.SaveCategory(_admin,null,"Boissons",true);var category=Assert.Single(_catalog.Categories(_admin,"Boissons"));_catalog.SaveCategory(_admin,category.CategoryId,"Boissons froides",false);
        var user=Assert.Single(_users.List(_admin,"vendor.security"));_users.Update(_admin,user.UserId,user.Name,user.Username,user.Role,false);_auth.Logout(_admin);
        Assert.Throws<UnauthorizedAccessException>(()=>_audit.List(_vendor));var entries=_audit.List(_admin);Assert.Contains(entries,e=>e.Action=="Deconnexion");var update=Assert.Single(entries,e=>e.Action=="CategorieModifiee");Assert.Contains("Boissons",update.Details);Assert.Contains("Boissons froides",update.Details);
        Assert.DoesNotContain(entries,e=>e.Details?.Contains("MotDePasseAdmin!2026",StringComparison.Ordinal)==true);
        using var c=_database.Create();using var updateQuery=c.CreateCommand();updateQuery.CommandText="UPDATE JournalAudit SET Details='altéré' WHERE JournalAuditId=(SELECT MAX(JournalAuditId) FROM JournalAudit);";Assert.Throws<SqliteException>(()=>updateQuery.ExecuteNonQuery());
        using var deleteQuery=c.CreateCommand();deleteQuery.CommandText="DELETE FROM JournalAudit;";Assert.Throws<SqliteException>(()=>deleteQuery.ExecuteNonQuery());
    }

    [Fact]
    public void BackupAndRestorePreserveCatalogMovementsSalesAndIntegrity()
    {
        _catalog.SaveCategory(_admin,null,"Épicerie",true);var category=Assert.Single(_catalog.Categories(_admin,"Épicerie"));_catalog.SaveProduct(_admin,null,"SEC-001","Riz",category.CategoryId,300,2,true);var product=Assert.Single(_catalog.Products(_admin,"SEC-001"));_stock.Initialize(_admin,product.ProduitId,9);_stock.Replenish(_admin,product.ProduitId,4,"BL-99");var sale=_sales.Validate(_vendor,[new SaleLineDraft(product.ProduitId,2,300,product.Reference,product.Nom)],PaymentMode.Especes,1000,Guid.NewGuid().ToString("N")).Sale;
        var backup=Path.Combine(_directory,"manual.sqlite");Assert.Equal(backup,_backups.CreateManual(_admin,backup));Assert.True(File.Exists(backup));DatabaseBackupService.VerifyDatabase(backup);
        _catalog.SaveProduct(_admin,product.ProduitId,"SEC-001","Riz modifié",category.CategoryId,500,2,true);_stock.Replenish(_admin,product.ProduitId,2,"Après sauvegarde");
        var safety=_backups.Restore(_admin,backup);Assert.True(File.Exists(safety));var restored=Assert.Single(_catalog.Products(_admin,"SEC-001"));Assert.Equal("Riz",restored.Nom);Assert.Equal(11,_stock.CurrentBalance(product.ProduitId));Assert.Equal("Validee",_sales.Detail(_admin,sale.SaleId).Summary.Status);DatabaseBackupService.VerifyDatabase(_database.DatabasePath);
    }

    [Fact]
    public void InvalidMissingAndVendorBackupOperationsAreRejectedWithoutChangingActiveData()
    {
        var before=_audit.List(_admin).Count;var invalid=Path.Combine(_directory,"not-a-db.sqlite");File.WriteAllText(invalid,"not sqlite");Assert.Throws<InvalidDataException>(()=>_backups.Restore(_admin,invalid));Assert.Throws<FileNotFoundException>(()=>_backups.Restore(_admin,Path.Combine(_directory,"missing.sqlite")));
        Assert.Throws<UnauthorizedAccessException>(()=>_backups.CreateManual(_vendor,Path.Combine(_directory,"vendor.sqlite")));Assert.Throws<UnauthorizedAccessException>(()=>_backups.Restore(_vendor,invalid));Assert.True(_users.List(_admin,null).Count>0);Assert.True(_audit.List(_admin).Count>=before);
    }

    [Fact]
    public void ReadOnlyBackupDestinationFailurePreservesExistingFileAndActiveDatabase()
    {
        var protectedTarget=Path.Combine(_directory,"readonly.sqlite");File.WriteAllText(protectedTarget,"copie à préserver");File.SetAttributes(protectedTarget,FileAttributes.ReadOnly);
        try
        {
            Assert.Throws<IOException>(()=>_backups.CreateManual(_admin,protectedTarget));Assert.Equal("copie à préserver",File.ReadAllText(protectedTarget));DatabaseBackupService.VerifyDatabase(_database.DatabasePath);
        }
        finally { File.SetAttributes(protectedTarget,FileAttributes.Normal); }
    }

    [Fact]
    public void DatabaseFailureDuringSaleValidationRollsBackSaleAndStock()
    {
        _catalog.SaveProduct(_admin,null,"FAIL-DB","Produit transaction",null,2000,1,true);var product=Assert.Single(_catalog.Products(_admin,"FAIL-DB"));_stock.Initialize(_admin,product.ProduitId,10);
        using(var c=_database.Create())using(var q=c.CreateCommand()){q.CommandText="CREATE TRIGGER Test_FailSale BEFORE INSERT ON Vente BEGIN SELECT RAISE(ABORT,'SIMULATED_DB_FAILURE'); END;";q.ExecuteNonQuery();}
        try{Assert.Throws<SqliteException>(()=>_sales.Validate(_vendor,[new SaleLineDraft(product.ProduitId,3,2000,product.Reference,product.Nom)],PaymentMode.Especes,6000,Guid.NewGuid().ToString("N")));}
        finally{using var c=_database.Create();using var q=c.CreateCommand();q.CommandText="DROP TRIGGER IF EXISTS Test_FailSale;";q.ExecuteNonQuery();}
        Assert.Equal(10,_stock.CurrentBalance(product.ProduitId));Assert.Empty(_sales.History(_admin,"",null,null,null,null,0).Items);
    }

    [Fact]
    public void AcceptanceT01AndT03CreateInitialStockAndCommitExpectedSale()
    {
        _catalog.SaveProduct(_admin,null,"T01-T03","Farine",null,2000,5,true);var product=Assert.Single(_catalog.Products(_admin,"T01-T03"));_stock.Initialize(_admin,product.ProduitId,10);Assert.Equal(10,_stock.CurrentBalance(product.ProduitId));Assert.Single(_stock.Movements(_admin,product.ProduitId),m=>m.Type=="Initial");
        var sale=_sales.Validate(_vendor,[new SaleLineDraft(product.ProduitId,3,2000,product.Reference,product.Nom)],PaymentMode.Especes,6000,Guid.NewGuid().ToString("N")).Sale;Assert.Equal(6000,sale.Total);Assert.Equal(7,_stock.CurrentBalance(product.ProduitId));
    }

    [Fact]
    public void AcceptanceT05AndT06RejectEmptyStockThenApplyThresholdStatus()
    {
        _catalog.SaveProduct(_admin,null,"T05-T06","Huile",null,1000,5,true);var product=Assert.Single(_catalog.Products(_admin,"T05-T06"));_stock.Initialize(_admin,product.ProduitId,7);var line=new SaleLineDraft(product.ProduitId,7,1000,product.Reference,product.Nom);_sales.Validate(_vendor,[line],PaymentMode.MobileMoney,7000,Guid.NewGuid().ToString("N"));var empty=Assert.Single(_catalog.Products(_admin,"T05-T06"));Assert.Equal(0,empty.QuantiteStock);Assert.Equal("En rupture",empty.StatutStock);Assert.Throws<InsufficientStockException>(()=>_sales.Validate(_vendor,[line with{Quantity=1}],PaymentMode.MobileMoney,1000,Guid.NewGuid().ToString("N")));
        _stock.Replenish(_admin,product.ProduitId,5,"T06 réception");var low=Assert.Single(_catalog.Products(_admin,"T05-T06"));Assert.Equal(5,low.QuantiteStock);Assert.Equal("Stock faible",low.StatutStock);
    }

    [Fact]
    public void AcceptanceT10KeepsSoldNameAndPriceSnapshotsAfterCatalogChange()
    {
        _catalog.SaveProduct(_admin,null,"T10-OLD","Café classique",null,2000,1,true);var product=Assert.Single(_catalog.Products(_admin,"T10-OLD"));_stock.Initialize(_admin,product.ProduitId,5);var first=_sales.Validate(_vendor,[new SaleLineDraft(product.ProduitId,1,2000,product.Reference,product.Nom)],PaymentMode.Especes,2000,Guid.NewGuid().ToString("N")).Sale;
        _catalog.SaveProduct(_admin,product.ProduitId,"T10-OLD","Café nouvelle récolte",null,2500,1,true);var oldLine=Assert.Single(_sales.Detail(_admin,first.SaleId).Lines);Assert.Equal("Café classique",oldLine.Name);Assert.Equal(2000,oldLine.UnitPrice);var updated=Assert.Single(_catalog.Products(_admin,"T10-OLD"));var next=_sales.Validate(_vendor,[new SaleLineDraft(product.ProduitId,1,updated.PrixVente,updated.Reference,updated.Nom)],PaymentMode.Especes,2500,Guid.NewGuid().ToString("N")).Sale;Assert.Equal(2500,next.Total);
    }

    [Fact]
    public void AutomaticBackupRunsOncePerBusinessDateAndKeepsVerifiedFile()
    {
        Assert.True(_backups.CreateAutomaticDailyBackup(TimeZoneInfo.Utc));Assert.False(_backups.CreateAutomaticDailyBackup(TimeZoneInfo.Utc));var files=Directory.GetFiles(Path.Combine(_directory,"Backups"),"automatique_*.sqlite");Assert.Single(files);DatabaseBackupService.VerifyDatabase(files[0]);
    }

    [Fact]
    public void AutomaticRetentionKeepsTheSevenNewestSuccessfulBackups()
    {
        for(var i=0;i<8;i++)
        {
            using(var c=_database.Create())using(var q=c.CreateCommand()){q.CommandText="INSERT INTO Parametre(Cle,Valeur,ModifieLeUtc) VALUES('DerniereSauvegardeAutomatiqueReussie','2000-01-01',$utc) ON CONFLICT(Cle) DO UPDATE SET Valeur='2000-01-01',ModifieLeUtc=excluded.ModifieLeUtc;";q.Parameters.AddWithValue("$utc",DateTime.UtcNow.ToString("O"));q.ExecuteNonQuery();}
            Assert.True(_backups.CreateAutomaticDailyBackup(TimeZoneInfo.Utc));
        }
        var files=Directory.GetFiles(Path.Combine(_directory,"Backups"),"automatique_*.sqlite").Select(File.GetLastWriteTimeUtc).OrderByDescending(x=>x).ToArray();Assert.Equal(7,files.Length);Assert.True(files[0]>files[^1]);
    }

    public void Dispose(){try{SqliteConnection.ClearAllPools();Directory.Delete(_directory,true);}catch(IOException){}}
}
