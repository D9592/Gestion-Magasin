using Microsoft.Data.Sqlite;
using StoreManager.App.Data;
using StoreManager.App.Models;
using StoreManager.App.Services;
using Xunit;

namespace StoreManager.Tests;

public sealed class AuthenticationAndCatalogTests : IDisposable
{
    private readonly string _directory=Path.Combine(Path.GetTempPath(),"NOVA-tests",Guid.NewGuid().ToString("N"));
    private readonly SqliteConnectionFactory _database;
    private readonly PasswordHasher _hasher=new();
    private readonly UserRepository _users;
    private readonly CatalogRepository _catalogRepository;
    private readonly AuthenticationService _authentication;
    private readonly UserManagementService _userService;
    private readonly CatalogManagementService _catalog;
    private UserSession _admin=null!;

    public AuthenticationAndCatalogTests()
    {
        Directory.CreateDirectory(_directory);
        _database=new SqliteConnectionFactory(Path.Combine(_directory,"test.sqlite"));_database.Initialize();
        _users=new UserRepository(_database);_catalogRepository=new CatalogRepository(_database);
        _authentication=new AuthenticationService(_users,_hasher);_userService=new UserManagementService(_users,_hasher);_catalog=new CatalogManagementService(_catalogRepository);
        _authentication.CreateInitialAdministrator("Administrateur Démo","admin.test","MotDePasseSecurise!2026");
        _admin=_authentication.Login("ADMIN.TEST","MotDePasseSecurise!2026");
    }

    [Fact]
    public void PasswordsAreSaltedAndNeverStoredAsPlainText()
    {
        using var connection=_database.Create();using var command=connection.CreateCommand();command.CommandText="SELECT MotDePasseHash,Sel,ParametresHachage FROM Utilisateur WHERE Identifiant=$id;";command.Parameters.AddWithValue("$id","admin.test");using var reader=command.ExecuteReader();Assert.True(reader.Read());
        var hash=(byte[])reader[0];var salt=(byte[])reader[1];var parameters=reader.GetString(2);
        Assert.NotEqual(System.Text.Encoding.UTF8.GetBytes("MotDePasseSecurise!2026"),hash);Assert.Equal(16,salt.Length);Assert.Contains("PBKDF2-SHA256",parameters);Assert.True(_hasher.Verify("MotDePasseSecurise!2026",hash,salt,parameters));
    }

    [Fact]
    public void EachUserReceivesADifferentSaltAndDuplicateUsernamesAreRejected()
    {
        _userService.Create(_admin,"Vendeur","vendeur.unique",UserRole.Vendeur,"MotDePasseVendeur!2026");
        Assert.Contains("déjà utilisé",Assert.Throws<InvalidOperationException>(()=>_userService.Create(_admin,"Autre","VENDEUR.UNIQUE",UserRole.Vendeur,"AutreMotDePasse!2026")).Message,StringComparison.OrdinalIgnoreCase);
        using var connection=_database.Create();using var command=connection.CreateCommand();command.CommandText="SELECT Sel FROM Utilisateur ORDER BY UtilisateurId;";using var reader=command.ExecuteReader();Assert.True(reader.Read());var first=(byte[])reader[0];Assert.True(reader.Read());var second=(byte[])reader[0];Assert.NotEqual(first,second);
    }

    [Fact]
    public void LoginRejectsWrongCredentialsAndRateLimitsRepeatedFailures()
    {
        Assert.Throws<InvalidOperationException>(()=>_authentication.Login("admin.test","mot de passe incorrect"));
        for(var i=0;i<4;i++)Assert.Throws<InvalidOperationException>(()=>_authentication.Login("admin.test","incorrect"));
        var locked=Assert.Throws<InvalidOperationException>(()=>_authentication.Login("admin.test","MotDePasseSecurise!2026"));
        Assert.Contains("Trop de tentatives",locked.Message);
    }

    [Fact]
    public void VendorCanReadButCannotManageProductsOrCategories()
    {
        _userService.Create(_admin,"Vendeur Démo","vendeur.test",UserRole.Vendeur,"MotDePasseVendeur!2026");
        var vendor=_authentication.Login("vendeur.test","MotDePasseVendeur!2026");
        Assert.Equal(UserRole.Vendeur,vendor.Role);
        _catalog.SaveCategory(_admin,null,"Épicerie",true);
        Assert.Single(_catalog.Categories(vendor,"Épi"));
        Assert.Throws<UnauthorizedAccessException>(()=>_catalog.SaveCategory(vendor,null,"Interdit",true));
        Assert.Throws<UnauthorizedAccessException>(()=>_catalog.SaveProduct(vendor,null,"REF-1","Produit test",null,1500,2,true));
        Assert.Throws<UnauthorizedAccessException>(()=>_userService.List(vendor,null));
        var vendorRecord=Assert.Single(_userService.List(_admin,"vendeur.test"));_userService.Update(_admin,vendorRecord.UserId,vendorRecord.Name,vendorRecord.Username,vendorRecord.Role,false);
        Assert.Throws<InvalidOperationException>(()=>_authentication.Login("vendeur.test","MotDePasseVendeur!2026"));
    }

    [Fact]
    public void AdminCanCreateAndUpdateCategoryAndProductAndReferencesStayUnique()
    {
        _catalog.SaveCategory(_admin,null,"Épicerie",true);
        Assert.Contains("déjà",Assert.Throws<InvalidOperationException>(()=>_catalog.SaveCategory(_admin,null,"épicerie",true)).Message,StringComparison.OrdinalIgnoreCase);
        var category=Assert.Single(_catalog.Categories(_admin,"Épi"));
        _catalog.SaveCategory(_admin,category.CategoryId,"Épicerie sèche",true);
        category=Assert.Single(_catalog.Categories(_admin,"sèche"));
        _catalog.SaveProduct(_admin,null,"REF-UNIQUE","Riz",category.CategoryId,2000,5,true);
        var product=Assert.Single(_catalog.Products(_admin,"REF-UNIQUE"));
        Assert.Equal(2000,product.PrixVente);Assert.Equal(category.CategoryId,product.CategorieId);
        _catalog.SaveProduct(_admin,product.ProduitId,"REF-UNIQUE","Riz parfumé",category.CategoryId,2300,4,false);
        product=Assert.Single(_catalog.Products(_admin,"Riz parfumé"));Assert.False(product.Actif);Assert.Equal(2300,product.PrixVente);
        Assert.Contains("déjà utilisée",Assert.Throws<InvalidOperationException>(()=>_catalog.SaveProduct(_admin,null,"REF-UNIQUE","Autre",null,500,0,true)).Message);
    }

    [Fact]
    public void ProductAvailabilityIsReadFromSignedStockMovements()
    {
        _catalog.SaveProduct(_admin,null,"REF-STOCK","Savon",null,500,5,true);
        var product=Assert.Single(_catalog.Products(_admin,"REF-STOCK"));Assert.Equal(0,product.QuantiteStock);Assert.Equal("En rupture",product.StatutStock);
        using(var connection=_database.Create())using(var command=connection.CreateCommand())
        {
            command.CommandText="INSERT INTO MouvementStock(ProduitId,AuteurId,DateHeureUtc,Type,QuantiteSignee,Motif) VALUES($product,$author,$date,'Initial',5,'Test de stock');";
            command.Parameters.AddWithValue("$product",product.ProduitId);command.Parameters.AddWithValue("$author",_admin.UserId);command.Parameters.AddWithValue("$date",DateTime.UtcNow.ToString("O"));command.ExecuteNonQuery();
        }
        product=Assert.Single(_catalog.Products(_admin,"REF-STOCK"));Assert.Equal(5,product.QuantiteStock);Assert.Equal("Stock faible",product.StatutStock);
        using(var connection=_database.Create())using(var command=connection.CreateCommand())
        {
            command.CommandText="INSERT INTO MouvementStock(ProduitId,AuteurId,DateHeureUtc,Type,QuantiteSignee,Motif) VALUES($product,$author,$date,'Reapprovisionnement',3,'Test de stock');";
            command.Parameters.AddWithValue("$product",product.ProduitId);command.Parameters.AddWithValue("$author",_admin.UserId);command.Parameters.AddWithValue("$date",DateTime.UtcNow.ToString("O"));command.ExecuteNonQuery();
        }
        product=Assert.Single(_catalog.Products(_admin,"REF-STOCK"));Assert.Equal(8,product.QuantiteStock);Assert.Equal("Disponible",product.StatutStock);
        Assert.Single(_catalog.Products(_admin,"REF-STOCK",availability:"Disponible"));
    }

    [Fact]
    public void ProductListsCanBePagedWithoutHidingLaterItems()
    {
        _catalog.SaveProduct(_admin,null,"REF-PAGE-A","Article A",null,100,0,true);
        _catalog.SaveProduct(_admin,null,"REF-PAGE-B","Article B",null,200,0,true);
        var first=_catalog.ProductsPage(_admin,"REF-PAGE",null,null,null,0,1);Assert.Single(first.Items);Assert.True(first.HasMore);
        var second=_catalog.ProductsPage(_admin,"REF-PAGE",null,null,null,1,1);Assert.Single(second.Items);Assert.False(second.HasMore);Assert.NotEqual(first.Items[0].ProduitId,second.Items[0].ProduitId);
    }

    [Fact]
    public void LastAdministratorCannotBeDeactivatedOrDemoted()
    {
        var admin=Assert.Single(_userService.List(_admin,null));
        var otherAdministratorSession=new UserSession(-1,"Autre administrateur","test-admin",UserRole.Administrateur);
        var error=Assert.Throws<InvalidOperationException>(()=>_userService.Update(otherAdministratorSession,admin.UserId,admin.Name,admin.Username,UserRole.Vendeur,true));
        Assert.Contains("dernier administrateur",error.Message);
    }

    [Fact]
    public void PasswordResetInvalidatesTheOldPasswordAndPreservesData()
    {
        _userService.Create(_admin,"Vendeur","vendeur.reset",UserRole.Vendeur,"MotDePasseInitial!2026");
        var vendor=Assert.Single(_userService.List(_admin,"vendeur.reset"));
        _userService.ResetPassword(_admin,vendor.UserId,"MotDePasseNouveau!2026");
        Assert.Throws<InvalidOperationException>(()=>_authentication.Login("vendeur.reset","MotDePasseInitial!2026"));
        Assert.Equal(UserRole.Vendeur,_authentication.Login("vendeur.reset","MotDePasseNouveau!2026").Role);
        var reopened=new SqliteConnectionFactory(Path.Combine(_directory,"test.sqlite"));reopened.Initialize();Assert.True(new UserRepository(reopened).AnyUsers());
    }

    public void Dispose(){try{SqliteConnection.ClearAllPools();Directory.Delete(_directory,true);}catch(IOException){}}
}
