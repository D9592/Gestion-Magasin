using Microsoft.Data.Sqlite;
using StoreManager.App.Data;
using StoreManager.App.Forms;
using StoreManager.App.Models;
using StoreManager.App.Services;
using Xunit;

namespace StoreManager.Tests;

public sealed class StockAndSalesTests : IDisposable
{
    private readonly string _directory=Path.Combine(Path.GetTempPath(),"NOVA-stock-sales-tests",Guid.NewGuid().ToString("N"));
    private readonly SqliteConnectionFactory _database;
    private readonly AuthenticationService _auth;
    private readonly UserManagementService _users;
    private readonly CatalogManagementService _catalog;
    private readonly StockManagementService _stock;
    private readonly SalesService _sales;
    private readonly UserSession _admin;
    private readonly UserSession _vendor;

    public StockAndSalesTests()
    {
        Directory.CreateDirectory(_directory);_database=new SqliteConnectionFactory(Path.Combine(_directory,"test.sqlite"));_database.Initialize();
        var hasher=new PasswordHasher();var users=new UserRepository(_database);_auth=new AuthenticationService(users,hasher);_users=new UserManagementService(users,hasher);var catalogRepository=new CatalogRepository(_database);_catalog=new CatalogManagementService(catalogRepository);_stock=new StockManagementService(new StockRepository(_database),_catalog);_sales=new SalesService(new SalesRepository(_database,new StoreSettingsRepository(_database)),_catalog);
        _auth.CreateInitialAdministrator("Administrateur test","admin.stock","MotDePasseAdmin!2026");_admin=_auth.Login("admin.stock","MotDePasseAdmin!2026");_users.Create(_admin,"Vendeur test","vendor.stock",UserRole.Vendeur,"MotDePasseVendeur!2026");_vendor=_auth.Login("vendor.stock","MotDePasseVendeur!2026");
    }

    [Fact]
    public void InitialReplenishmentAndCountCorrectionAreRecordedAsSignedMovements()
    {
        var product=CreateProduct("REF-INV",1000);_stock.Initialize(_admin,product.ProduitId,10);_stock.Replenish(_admin,product.ProduitId,5,"BL-001");
        Assert.Equal(-3,_stock.Correct(_admin,product.ProduitId,12,"Comptage physique"));Assert.Equal(12,_stock.CurrentBalance(product.ProduitId));
        var movements=_stock.Movements(_admin,product.ProduitId);Assert.Equal(new[]{10,5,-3},movements.Select(x=>x.SignedQuantity));Assert.Equal(12,movements[^1].BalanceAfter);
        Assert.Throws<InvalidOperationException>(()=>_stock.Initialize(_admin,product.ProduitId,10));Assert.Throws<ArgumentOutOfRangeException>(()=>_stock.Replenish(_admin,product.ProduitId,0,"BL"));
        Assert.Throws<UnauthorizedAccessException>(()=>_stock.Replenish(_vendor,product.ProduitId,1,"BL-002"));
    }

    [Fact]
    public void SaleSnapshotsPricesAndPaymentAndUsesIdempotentValidationToken()
    {
        var product=CreateProduct("REF-SALE",250);_stock.Initialize(_admin,product.ProduitId,8);var token=Guid.NewGuid().ToString("N");var draft=new SaleLineDraft(product.ProduitId,3,250,product.Reference,product.Nom);
        var first=_sales.Validate(_vendor,[draft],PaymentMode.Especes,1000,token);var again=_sales.Validate(_vendor,[draft],PaymentMode.Especes,1000,token);
        Assert.False(first.AlreadyCommitted);Assert.True(again.AlreadyCommitted);Assert.Equal(first.Sale.SaleId,again.Sale.SaleId);Assert.Equal(5,_stock.CurrentBalance(product.ProduitId));Assert.Equal(250,first.Sale.Total/3);
        var detail=_sales.Detail(_vendor,first.Sale.SaleId);Assert.Equal("REF-SALE",Assert.Single(detail.Lines).Reference);Assert.Equal(750,detail.Summary.Total);Assert.Equal(1000,detail.Summary.AmountReceived);Assert.Equal(250,detail.Summary.Change);
        _users.Create(_admin,"Autre vendeur","vendor.other",UserRole.Vendeur,"Vendeur!Autre2026");var other=_auth.Login("vendor.other","Vendeur!Autre2026");Assert.Throws<UnauthorizedAccessException>(()=>_sales.Detail(other,first.Sale.SaleId));
    }

    [Fact]
    public void PriceChangeRequiresASecondConfirmationAndFailedSaleDoesNotConsumeStock()
    {
        var product=CreateProduct("REF-PRICE",400);_stock.Initialize(_admin,product.ProduitId,4);var draft=new SaleLineDraft(product.ProduitId,2,400,product.Reference,product.Nom);var token=Guid.NewGuid().ToString("N");
        _catalog.SaveProduct(_admin,product.ProduitId,product.Reference,product.Nom,null,500,0,true);
        var changed=Assert.Throws<SalePriceChangedException>(()=>_sales.Validate(_vendor,[draft],PaymentMode.MobileMoney,1000,token));Assert.Equal(1000,changed.CurrentTotal);Assert.Equal(4,_stock.CurrentBalance(product.ProduitId));
        var accepted=draft with{ExpectedUnitPrice=500};var sale=_sales.Validate(_vendor,[accepted],PaymentMode.MobileMoney,1000,token);Assert.Equal(1000,sale.Sale.Total);Assert.Equal(2,_stock.CurrentBalance(product.ProduitId));
    }

    [Theory]
    [InlineData("MobileMoney")]
    [InlineData("Carte")]
    public void MultiProductNonCashSalesRecordFullPaymentAndTicketSnapshots(string paymentName)
    {
        var payment=Enum.Parse<PaymentMode>(paymentName);
        var first=CreateProduct("REF-MULTI-A",200);var second=CreateProduct("REF-MULTI-B",350);_stock.Initialize(_admin,first.ProduitId,4);_stock.Initialize(_admin,second.ProduitId,5);
        var drafts=new[]{new SaleLineDraft(first.ProduitId,2,200,first.Reference,first.Nom),new SaleLineDraft(second.ProduitId,1,350,second.Reference,second.Nom)};var sale=_sales.Validate(_vendor,drafts,payment,750,Guid.NewGuid().ToString("N")).Sale;
        Assert.Equal(750,sale.Total);Assert.Equal(2,_sales.Detail(_vendor,sale.SaleId).Lines.Count);Assert.Equal(2,_stock.CurrentBalance(first.ProduitId));Assert.Equal(4,_stock.CurrentBalance(second.ProduitId));
        var detail=_sales.Detail(_vendor,sale.SaleId);var lines=TicketPrintDocument.TicketLines(detail);Assert.Contains("NOVA",lines[0]);Assert.Contains(sale.Number,string.Join("\n",lines));Assert.Contains("750",string.Join("\n",lines));
    }

    [Fact]
    public void InsufficientStockInvalidPaymentAndVendorCancelCannotPartiallyWrite()
    {
        var product=CreateProduct("REF-FAIL",100);var empty=CreateProduct("REF-EMPTY",200);_stock.Initialize(_admin,product.ProduitId,1);_stock.Initialize(_admin,empty.ProduitId,0);var token=Guid.NewGuid().ToString("N");var validFirst=new SaleLineDraft(product.ProduitId,1,100,product.Reference,product.Nom);var unavailableSecond=new SaleLineDraft(empty.ProduitId,1,200,empty.Reference,empty.Nom);
        Assert.Throws<InsufficientStockException>(()=>_sales.Validate(_vendor,[validFirst,unavailableSecond],PaymentMode.Especes,300,token));Assert.Equal(1,_stock.CurrentBalance(product.ProduitId));Assert.Empty(_sales.History(_admin,"",null,null,null,null,0).Items);
        using(var connection=_database.Create())using(var command=connection.CreateCommand()){command.CommandText="INSERT INTO MouvementStock(ProduitId,AuteurId,DateHeureUtc,Type,QuantiteSignee,Motif) VALUES($product,$author,$date,'Vente',-2,'Bypass attempt');";command.Parameters.AddWithValue("$product",product.ProduitId);command.Parameters.AddWithValue("$author",_admin.UserId);command.Parameters.AddWithValue("$date",DateTime.UtcNow.ToString("O"));Assert.Throws<SqliteException>(()=>command.ExecuteNonQuery());}
        Assert.Empty(_sales.History(_admin,"",null,null,null,null,0).Items);
        Assert.Throws<InvalidOperationException>(()=>_sales.Validate(_vendor,[validFirst],PaymentMode.Carte,101,token));Assert.Equal(1,_stock.CurrentBalance(product.ProduitId));
    }

    [Fact]
    public void AdministratorCancellationRestoresStockExactlyOnceAndPreservesAuditIntegrity()
    {
        var product=CreateProduct("REF-CANCEL",150);_stock.Initialize(_admin,product.ProduitId,7);var draft=new SaleLineDraft(product.ProduitId,2,150,product.Reference,product.Nom);var sale=_sales.Validate(_vendor,[draft],PaymentMode.Carte,300,Guid.NewGuid().ToString("N")).Sale;
        Assert.Throws<UnauthorizedAccessException>(()=>_sales.Cancel(_vendor,sale.SaleId,"Test"));_sales.Cancel(_admin,sale.SaleId,"Erreur de saisie");Assert.Equal(7,_stock.CurrentBalance(product.ProduitId));Assert.Throws<InvalidOperationException>(()=>_sales.Cancel(_admin,sale.SaleId,"Double annulation"));
        Assert.Equal("Annulee",_sales.Detail(_admin,sale.SaleId).Summary.Status);Assert.Single(_stock.Movements(_admin,product.ProduitId),movement=>movement.Type=="AnnulationVente");
        using var connection=_database.Create();using var command=connection.CreateCommand();command.CommandText="PRAGMA quick_check;";Assert.Equal("ok",command.ExecuteScalar());
        using var foreignKeys=connection.CreateCommand();foreignKeys.CommandText="PRAGMA foreign_key_check;";using var reader=foreignKeys.ExecuteReader();Assert.False(reader.Read());
    }

    private Produit CreateProduct(string reference,long price){_catalog.SaveProduct(_admin,null,reference,$"Produit {reference}",null,price,2,true);return Assert.Single(_catalog.Products(_admin,reference));}
    public void Dispose(){try{SqliteConnection.ClearAllPools();Directory.Delete(_directory,true);}catch(IOException){}}
}
