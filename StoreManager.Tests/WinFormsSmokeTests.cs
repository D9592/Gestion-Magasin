using StoreManager.App.Data;
using StoreManager.App.Forms;
using StoreManager.App.Services;
using System.Drawing;
using System.Windows.Forms;
using Xunit;

namespace StoreManager.Tests;

public sealed class WinFormsSmokeTests
{
    [Fact]
    public void AuthenticationAndAdministrationFormsCanBeOpenedOnStaThread()
    {
        Exception? failure=null;
        var thread=new Thread(()=>
        {
            var directory=Path.Combine(Path.GetTempPath(),"NOVA-ui-tests",Guid.NewGuid().ToString("N"));
            try
            {
                Application.SetHighDpiMode(HighDpiMode.PerMonitorV2);Application.EnableVisualStyles();Application.SetCompatibleTextRenderingDefault(false);
                Directory.CreateDirectory(directory);var database=new SqliteConnectionFactory(Path.Combine(directory,"ui.sqlite"));database.Initialize();
                var hash=new PasswordHasher();var usersRepo=new UserRepository(database);var catalogRepo=new CatalogRepository(database);var auth=new AuthenticationService(usersRepo,hash);var users=new UserManagementService(usersRepo,hash);var catalog=new CatalogManagementService(catalogRepo);var stock=new StockManagementService(new StockRepository(database),catalog);var settings=new StoreSettingsRepository(database);var sales=new SalesService(new SalesRepository(database,settings),catalog);var reporting=new ReportingService(new ReportingRepository(database),settings);var audit=new AuditService(new AuditRepository(database));var backups=new DatabaseBackupService(database);
                auth.CreateInitialAdministrator("Administrateur UI","admin.ui","MotDePasseUI!2026");var session=auth.Login("admin.ui","MotDePasseUI!2026");
                using var firstRun=new InitialAdministratorForm(auth);Open(firstRun);
                using var login=new LoginForm(auth);Open(login);
                using var main=new MainForm(session,users,catalog,stock,sales,reporting,auth,audit,backups);Open(main);
                using var administration=new AdministrationForm(session,users,catalog,audit,backups);Open(administration,main);
                using var auditForm=new AuditLogForm(session,audit);Open(auditForm,main);
                using var backupForm=new BackupManagementForm(session,backups);Open(backupForm,main);
                using var userList=new UserManagementForm(session,users);Open(userList,main);
                using var categories=new CategoryManagementForm(session,catalog);Open(categories,main);
                using var products=new ProductManagementForm(session,catalog);Open(products,main);
                using var userEdit=new UserEditDialog(null);Open(userEdit,main);
                using var categoryEdit=new CategoryEditDialog(null);Open(categoryEdit,main);
                using var productEdit=new ProductEditDialog(catalog.Categories(session),null);Open(productEdit,main);
                using var newSale=new NewSaleForm(session,sales);Open(newSale,main);
                using var stockForm=new StockManagementForm(session,stock,catalog);Open(stockForm,main);
                using var salesHistory=new SalesHistoryForm(session,sales,users);Open(salesHistory,main);
                using var reports=new ReportsForm(session,reporting,users);Open(reports,main);
                foreach(var size in new[]{new Size(1366,768),new Size(1600,900),new Size(1920,1080)}){reports.Size=size;reports.PerformLayout();Application.DoEvents();Assert.True(reports.ClientSize.Width>=1080);var root=reports.Controls.OfType<TableLayoutPanel>().Single();Assert.True(root.GetControlFromPosition(0,2)?.Visible);}
                catalog.SaveProduct(session,null,"SMOKE-TICKET","Produit ticket",null,500,2,true);var ticketProduct=System.Linq.Enumerable.Single(catalog.Products(session,"SMOKE-TICKET"));stock.Initialize(session,ticketProduct.ProduitId,3);using var stockDialog=new StockOperationDialog(session,ticketProduct,StockOperation.Replenish,stock);Open(stockDialog,main);
                var sale=sales.Validate(session,[new StoreManager.App.Models.SaleLineDraft(ticketProduct.ProduitId,1,500,ticketProduct.Reference,ticketProduct.Nom)],StoreManager.App.Models.PaymentMode.Especes,500,Guid.NewGuid().ToString("N")).Sale;using var ticket=new TicketDialog(sales.Detail(session,sale.SaleId));Open(ticket,main);
                using var resetPassword=new PasswordResetDialog();Open(resetPassword,main);
                resetPassword.Close();ticket.Close();stockDialog.Close();reports.Close();salesHistory.Close();stockForm.Close();newSale.Close();productEdit.Close();categoryEdit.Close();userEdit.Close();products.Close();categories.Close();userList.Close();backupForm.Close();auditForm.Close();administration.Close();main.Close();login.Close();firstRun.Close();
                Application.DoEvents();
            }
            catch(Exception ex){failure=ex;}
            finally{try{Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();Directory.Delete(directory,true);}catch(Exception){}}
        });
        thread.SetApartmentState(ApartmentState.STA);thread.Start();thread.Join();
        if(failure is not null)throw new InvalidOperationException("Échec du smoke test WinForms.",failure);
    }

    private static void Open(Form form,IWin32Window? owner=null)
    {
        if(owner is null)form.Show();else form.Show(owner);
        Application.DoEvents();
        Assert.True(form.IsHandleCreated,$"Handle absent pour {form.GetType().Name}.");
        Assert.True(form.Visible,$"Formulaire non visible : {form.GetType().Name}.");
    }
}
