using System.Text;
using Microsoft.Data.Sqlite;
using StoreManager.App.Data;
using StoreManager.App.Helpers;
using StoreManager.App.Models;
using StoreManager.App.Services;
using Xunit;

namespace StoreManager.Tests;

public sealed class ReportingTests : IDisposable
{
    private readonly string _directory=Path.Combine(Path.GetTempPath(),"NOVA-report-tests",Guid.NewGuid().ToString("N"));
    private readonly SqliteConnectionFactory _database;private readonly AuthenticationService _auth;private readonly CatalogManagementService _catalog;private readonly UserManagementService _users;private readonly StockManagementService _stock;private readonly SalesService _sales;private readonly ReportingService _reports;private readonly UserSession _admin;private readonly UserSession _vendor;
    public ReportingTests()
    {
        Directory.CreateDirectory(_directory);_database=new SqliteConnectionFactory(Path.Combine(_directory,"reports.sqlite"));_database.Initialize();var hash=new PasswordHasher();var users=new UserRepository(_database);_auth=new AuthenticationService(users,hash);_users=new UserManagementService(users,hash);var settings=new StoreSettingsRepository(_database);_catalog=new CatalogManagementService(new CatalogRepository(_database));_stock=new StockManagementService(new StockRepository(_database),_catalog);_sales=new SalesService(new SalesRepository(_database,settings),_catalog);_reports=new ReportingService(new ReportingRepository(_database),settings);
        _auth.CreateInitialAdministrator("Administrateur rapport","admin.report","MotDePasseAdmin!2026");_admin=_auth.Login("admin.report","MotDePasseAdmin!2026");_users.Create(_admin,"Vendeur rapport","vendor.report",UserRole.Vendeur,"MotDePasseVendeur!2026");_vendor=_auth.Login("vendor.report","MotDePasseVendeur!2026");
        using var c=_database.Create();using var q=c.CreateCommand();q.CommandText="INSERT INTO Parametre(Cle,Valeur,ModifieLeUtc) VALUES('FuseauHoraire','UTC',$now);";q.Parameters.AddWithValue("$now",DateTime.UtcNow.ToString("O"));q.ExecuteNonQuery();
    }

    [Fact]
    public void DashboardScopesSalesByRoleExcludesCancelledSalesAndListsOnlyActiveAlerts()
    {
        var sold=CreateProduct("RPT-SOLD",100,0);var low=CreateProduct("RPT-LOW",50,3);var outOfStock=CreateProduct("RPT-OUT",25,0);var archived=CreateProduct("RPT-ARCH",10,2);
        _stock.Initialize(_admin,sold.ProduitId,8);_stock.Initialize(_admin,low.ProduitId,2);_stock.Initialize(_admin,outOfStock.ProduitId,0);_stock.Initialize(_admin,archived.ProduitId,0);_catalog.SaveProduct(_admin,archived.ProduitId,archived.Reference,archived.Nom,null,10,2,false);
        var vendorSale=_sales.Validate(_vendor,[new(sold.ProduitId,2,100,sold.Reference,sold.Nom)],PaymentMode.Especes,250,Guid.NewGuid().ToString("N")).Sale;
        _sales.Validate(_admin,[new(sold.ProduitId,1,100,sold.Reference,sold.Nom)],PaymentMode.Carte,100,Guid.NewGuid().ToString("N"));
        var vendorBefore=_reports.Dashboard(_vendor);Assert.Equal(200,vendorBefore.Statistics.SalesTotal);Assert.Equal(1,vendorBefore.Statistics.SaleCount);Assert.Equal(2,vendorBefore.Statistics.UnitsSold);
        var adminBefore=_reports.Dashboard(_admin);Assert.Equal(300,adminBefore.Statistics.SalesTotal);Assert.Equal(2,adminBefore.Statistics.SaleCount);Assert.Equal(3,adminBefore.Statistics.UnitsSold);Assert.Equal(1,adminBefore.Statistics.OutOfStockCount);Assert.Equal(1,adminBefore.Statistics.LowStockCount);
        Assert.Equal(7,adminBefore.SalesTrend.Count);Assert.Contains(adminBefore.TopProducts,p=>p.Reference==sold.Reference&&p.Quantity==3);Assert.Single(adminBefore.PaymentModes,p=>p.PaymentMode=="Carte");
        Assert.Equal(2,adminBefore.Alerts.Count);Assert.DoesNotContain(adminBefore.Alerts,p=>p.ProductId==archived.ProduitId);
        _sales.Cancel(_admin,vendorSale.SaleId,"Test annulation historique");var vendorAfter=_reports.Dashboard(_vendor);Assert.Equal(0,vendorAfter.Statistics.SalesTotal);Assert.Equal(0,vendorAfter.Statistics.UnitsSold);Assert.All(vendorAfter.SalesTrend,p=>Assert.Equal(0,p.Amount));
        var adminAfter=_reports.Dashboard(_admin);Assert.Equal(100,adminAfter.Statistics.SalesTotal);Assert.Equal(1,adminAfter.Statistics.SaleCount);Assert.Equal(1,adminAfter.Statistics.UnitsSold);
    }

    [Fact]
    public void DailyPeriodSalesAndStockReportsHonorInclusiveDatesAndCurrentCancellationStatus()
    {
        var product=CreateProduct("RPT-PERIOD",400,1);_stock.Initialize(_admin,product.ProduitId,5);var sale=_sales.Validate(_vendor,[new(product.ProduitId,2,400,product.Reference,product.Nom)],PaymentMode.MobileMoney,800,Guid.NewGuid().ToString("N")).Sale;var adminSale=_sales.Validate(_admin,[new(product.ProduitId,1,400,product.Reference,product.Nom)],PaymentMode.Carte,400,Guid.NewGuid().ToString("N")).Sale;var today=DateTime.UtcNow.ToString("yyyy-MM-dd");var yesterday=DateTime.UtcNow.AddDays(-1).ToString("yyyy-MM-dd");
        var daily=_reports.ProductSales(_admin,today,today,"RPT-PERIOD");var line=Assert.Single(daily.Items);Assert.Equal(3,line.Quantity);Assert.Equal(1200,line.Amount);
        Assert.Empty(_reports.ProductSales(_admin,yesterday,yesterday,null).Items);Assert.Throws<ArgumentException>(()=>_reports.ProductSales(_admin,today,yesterday,null));
        var sales=_reports.Sales(_admin,today,today,sale.Number,null,null);var row=Assert.Single(sales.Items);Assert.Equal("Validee",row.Status);Assert.Equal(800,row.Amount);Assert.Equal(adminSale.SaleId,Assert.Single(_reports.Sales(_admin,today,today,null,_admin.UserId,"Validee").Items).SaleId);
        _sales.Cancel(_admin,sale.SaleId,"Correction de test");var recalculated=Assert.Single(_reports.ProductSales(_admin,today,today,"RPT-PERIOD").Items);Assert.Equal(1,recalculated.Quantity);Assert.Equal(400,recalculated.Amount);Assert.Equal("Annulee",Assert.Single(_reports.Sales(_admin,today,today,sale.Number,null,null).Items).Status);
        var allSales=_reports.AllSales(_admin,today,today,null,null,null);Assert.Equal(2,allSales.Count);Assert.Single(_reports.AllSales(_admin,today,today,null,null,"Validee"));Assert.Single(_reports.AllSales(_admin,today,today,null,null,"Annulee"));using var export=new MemoryStream();CsvReportExporter.WriteSales(export,allSales);var salesCsv=new UTF8Encoding(true).GetString(export.ToArray());Assert.Contains(sale.Number,salesCsv);Assert.Contains("Annulee",salesCsv);Assert.Contains("800",salesCsv);
        Assert.Throws<UnauthorizedAccessException>(()=>_reports.Sales(_vendor,today,today,null,null,null));Assert.Throws<UnauthorizedAccessException>(()=>_reports.Stocks(_vendor,null,null));
        var stock=Assert.Single(_reports.Stocks(_admin,"RPT-PERIOD",null).Items);Assert.Equal(4,stock.Quantity);Assert.Equal("Disponible",stock.Status);
    }

    [Fact]
    public void CsvUsesUtf8BomExcelDelimiterExactIntegersAndFormulaEscaping()
    {
        using var stream=new MemoryStream();CsvReportExporter.WriteStocks(stream,[new StockReportRow(1,"REF;1"," \t=HYPERLINK(\"https://example.invalid\")",12,3,"Stock faible")]);var bytes=stream.ToArray();Assert.Equal(new byte[]{0xEF,0xBB,0xBF},bytes.Take(3));var csv=new UTF8Encoding(true).GetString(bytes);
        Assert.Contains(";",csv);Assert.Contains("' \t=HYPERLINK",csv);Assert.Contains("\"12\"",csv);Assert.Contains("\"Stock faible\"",csv);Assert.Equal("'=1+1",CsvReportExporter.ProtectFormula("=1+1"));Assert.Equal("Texte sûr",CsvReportExporter.ProtectFormula("Texte sûr"));
    }

    private Produit CreateProduct(string reference,long price,int threshold){_catalog.SaveProduct(_admin,null,reference,$"Produit {reference}",null,price,threshold,true);return Assert.Single(_catalog.Products(_admin,reference));}
    public void Dispose(){try{SqliteConnection.ClearAllPools();Directory.Delete(_directory,true);}catch(IOException){}}
}
