using StoreManager.App.Controls;
using StoreManager.App.Models;
using StoreManager.App.Services;

namespace StoreManager.App.Forms;

public sealed class MainForm : Form
{
    private readonly UserSession _session;
    private readonly UserManagementService _users;
    private readonly CatalogManagementService _catalog;
    private readonly StockManagementService _stock;
    private readonly SalesService _sales;
    private readonly ReportingService _reporting;
    private readonly AuthenticationService _authentication; private readonly AuditService _audit; private readonly DatabaseBackupService _backups;
    private readonly Panel _workspace = new();
    private readonly Label _pageTitle = Ui.Label("Vue d'ensemble", 20, true);
    private bool _logoutRecorded; private bool _restoringDatabase;

    internal MainForm(UserSession session, UserManagementService users, CatalogManagementService catalog, StockManagementService stock, SalesService sales, ReportingService reporting, AuthenticationService authentication, AuditService audit, DatabaseBackupService backups)
    {
        _session=session;_users=users;_catalog=catalog;_stock=stock;_sales=sales;_reporting=reporting;_authentication=authentication;_audit=audit;_backups=backups;
        Text = "NOVA — Gestion de magasin"; MinimumSize = new Size(1366, 768); Size = new Size(1440, 900);
        BackColor = Ui.Canvas; Font = new Font("Segoe UI", 9); StartPosition = FormStartPosition.CenterScreen;AutoScaleMode=AutoScaleMode.Font;
        BuildShell(); ShowDashboard(); FormClosing+=(_,_)=>{if(!_restoringDatabase)RecordLogout();};
    }

    private void BuildShell()
    {
        // Use a single layout container instead of overlapping docked controls. WinForms
        // resolves Dock positions in z-order, which caused the header and sidebar to
        // cover each other on some Windows DPI/display configurations.
        var shell = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 2,
            RowCount = 3,
            Margin = Padding.Empty,
            Padding = Padding.Empty,
            BackColor = Ui.Canvas
        };
        shell.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 238));
        shell.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        shell.RowStyles.Add(new RowStyle(SizeType.Absolute, 70));
        shell.RowStyles.Add(new RowStyle(SizeType.Absolute, 52));
        shell.RowStyles.Add(new RowStyle(SizeType.Percent, 100));

        var side = new Panel { Dock = DockStyle.Left, Width = 238, BackColor = Color.FromArgb(17, 31, 58), Padding = new Padding(18, 20, 14, 16) };
        var brand = new Label { Text = "◈  NOVA\n     GESTION", ForeColor = Color.White, Font = new Font("Segoe UI", 15, FontStyle.Bold), Height = 62, Dock = DockStyle.Top };
        side.Controls.Add(brand);
        var menu = new FlowLayoutPanel { Dock = DockStyle.Top, AutoSize = true, FlowDirection = FlowDirection.TopDown, WrapContents = false, Padding = new Padding(0, 18, 0, 0) };
        side.Controls.Add(menu);
        AddNav(menu, "▦   Tableau de bord", ShowDashboard);
        AddNav(menu, "□   Produits", ShowProducts);
        AddNav(menu, "▤   Gestion des stocks", ShowStock);
        AddNav(menu, "＋   Nouvelle vente", ShowSale);
        AddNav(menu, "◷   Historique", ShowSalesHistory);
        if(_session.Role==UserRole.Administrateur)
        {
            AddNav(menu, "▥   Rapports", ShowReports);
            AddNav(menu, "⚙   Administration", ShowAdministration);
        }
        var storeName=_reporting.StoreName();var lower = new Label { Text = $"ESPACE DE TRAVAIL\n{storeName}", ForeColor = Color.FromArgb(174, 188, 211), Dock = DockStyle.Bottom, Height = 52, Font = new Font("Segoe UI", 9) };
        side.Controls.Add(lower);

        var top = new Panel { Dock = DockStyle.Top, Height = 70, BackColor = Color.White, Padding = new Padding(28, 15, 28, 10) };
        top.Controls.Add(new Label { Text = storeName, Dock = DockStyle.Left, AutoSize = false, Width = 350, TextAlign = ContentAlignment.MiddleLeft, Font = new Font("Segoe UI", 10, FontStyle.Bold), ForeColor = Ui.Ink });
        var logout=Ui.Button($"{_session.Name} · Déconnexion");logout.Dock=DockStyle.Right;logout.Click+=(_,_)=>{RecordLogout();DialogResult=DialogResult.Retry;Close();};top.Controls.Add(logout);
        _workspace.Dock = DockStyle.Fill; _workspace.AutoScroll = true; _workspace.Padding = new Padding(30, 24, 16, 18); _workspace.BackColor = Ui.Canvas;
        var head = new Panel { Dock = DockStyle.Top, Height = 52, BackColor = Color.White, Padding = new Padding(28, 14, 20, 10) };
        head.Controls.Add(_pageTitle);

        shell.Controls.Add(side, 0, 0);
        shell.SetRowSpan(side, 3);
        shell.Controls.Add(top, 1, 0);
        shell.Controls.Add(head, 1, 1);
        shell.Controls.Add(_workspace, 1, 2);
        Controls.Add(shell);
    }

    private static void AddNav(Control menu, string title, Action action)
    {
        var b = new Button { Text = title, Width = 198, Height = 44, TextAlign = ContentAlignment.MiddleLeft, FlatStyle = FlatStyle.Flat, BackColor = Color.FromArgb(17, 31, 58), ForeColor = Color.FromArgb(220, 229, 241), Font = new Font("Segoe UI", 9, FontStyle.Regular), Padding = new Padding(12, 0, 0, 0), Margin = new Padding(0, 2, 0, 3), Cursor = Cursors.Hand };
        b.FlatAppearance.BorderSize = 0; b.FlatAppearance.MouseOverBackColor = Color.FromArgb(36, 55, 88); b.Click += (_, _) => action(); menu.Controls.Add(b);
    }

    private void SetPage(string title, Control content)
    {
        _pageTitle.Text = title;foreach(Control existing in _workspace.Controls.Cast<Control>().ToArray()){_workspace.Controls.Remove(existing);existing.Dispose();}content.Dock = DockStyle.Top; _workspace.Controls.Add(content);
    }

    private void ShowDashboard()
    {
        SetPage("Tableau de bord",new DashboardControl(_session,_reporting,OpenSalesReport,OpenStockReport));
    }

    private void ShowSale()
    {
        using var form=new NewSaleForm(_session,_sales);form.ShowDialog(this);
    }

    private void ShowStock()
    {
        using var form=new StockManagementForm(_session,_stock,_catalog);form.ShowDialog(this);
    }

    private void ShowSalesHistory()
    {
        using var form=new SalesHistoryForm(_session,_sales,_users);form.ShowDialog(this);
    }

    private void ShowReports(){using var form=new ReportsForm(_session,_reporting,_users);form.ShowDialog(this);}
    private void OpenSalesReport(string from,string through){using var form=new SalesHistoryForm(_session,_sales,_users,from,through,"Validee");form.ShowDialog(this);}
    private void OpenStockReport(string status){using var form=new StockManagementForm(_session,_stock,_catalog,status);form.ShowDialog(this);}

    private void ShowProducts(){using var form=new ProductManagementForm(_session,_catalog);form.ShowDialog(this);}
    private void ShowAdministration(){using var form=new AdministrationForm(_session,_users,_catalog,_audit,_backups);if(form.ShowDialog(this)==DialogResult.Retry){_restoringDatabase=true;DialogResult=DialogResult.Retry;Close();}}

    private void RecordLogout()
    {
        if(_logoutRecorded)return;_logoutRecorded=true;
        try{_authentication.Logout(_session);}catch(Exception){MessageBox.Show(this,"La fermeture de session n’a pas pu être inscrite au journal d’audit.","Journal d’audit",MessageBoxButtons.OK,MessageBoxIcon.Warning);}
    }

}
