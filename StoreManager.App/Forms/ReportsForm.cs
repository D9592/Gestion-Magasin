using StoreManager.App.Controls;
using StoreManager.App.Helpers;
using StoreManager.App.Models;
using StoreManager.App.Services;

namespace StoreManager.App.Forms;

internal sealed class ReportsForm : Form
{
    private sealed record SellerItem(long? Id,string Name);
    private sealed record StatusItem(string Value,string Name);
    private readonly UserSession _actor;private readonly ReportingService _reporting;private readonly UserManagementService _users;
    private readonly DateTimePicker _from=new(){Format=DateTimePickerFormat.Custom,CustomFormat="dd/MM/yyyy",Width=130};private readonly DateTimePicker _through=new(){Format=DateTimePickerFormat.Custom,CustomFormat="dd/MM/yyyy",Width=130};
    private readonly TextBox _search=new(){Width=230,PlaceholderText="Référence, produit ou numéro"};private readonly ComboBox _saleStatus=new(){DropDownStyle=ComboBoxStyle.DropDownList,Width=145};private readonly ComboBox _stockStatus=new(){DropDownStyle=ComboBoxStyle.DropDownList,Width=145};private readonly ComboBox _seller=new(){DropDownStyle=ComboBoxStyle.DropDownList,Width=190};
    private readonly TabControl _tabs=new(){Dock=DockStyle.Fill};private readonly DataGridView _salesGrid=StockManagementForm.MakeGridPublic();private readonly DataGridView _productsGrid=StockManagementForm.MakeGridPublic();private readonly DataGridView _stockGrid=StockManagementForm.MakeGridPublic();
    private readonly Label _status=Ui.Label("Choisissez une période puis appliquez les filtres.",9,false,Ui.Muted);private readonly Label _page=Ui.Label("",9,false,Ui.Muted);private readonly Button _previous=Ui.Button("← Précédent");private readonly Button _next=Ui.Button("Suivant →");private readonly Button _apply=Ui.Button("Appliquer les filtres",true);private readonly Button _export=Ui.Button("Exporter CSV");private bool _busy;private int _pageIndex;private bool _hasMore;

    public ReportsForm(UserSession actor,ReportingService reporting,UserManagementService users)
    {
        if(actor.Role!=UserRole.Administrateur)throw new UnauthorizedAccessException("Les rapports globaux sont réservés à l'administrateur.");
        _actor=actor;_reporting=reporting;_users=users;Text="Rapports — NOVA";StartPosition=FormStartPosition.CenterParent;Size=new Size(1320,800);MinimumSize=new Size(1100,680);BackColor=Ui.Canvas;Font=new Font("Segoe UI",10);AutoScaleMode=AutoScaleMode.Font;
        var root=new TableLayoutPanel{Dock=DockStyle.Fill,ColumnCount=1,RowCount=4,Padding=new Padding(20)};root.RowStyles.Add(new RowStyle(SizeType.AutoSize));root.RowStyles.Add(new RowStyle(SizeType.AutoSize));root.RowStyles.Add(new RowStyle(SizeType.Percent,100));root.RowStyles.Add(new RowStyle(SizeType.AutoSize));Controls.Add(root);
        root.Controls.Add(Ui.Label("Rapports et exports",20,true),0,0);
        var filters=new FlowLayoutPanel{Dock=DockStyle.Fill,AutoSize=true,WrapContents=true,Padding=new Padding(0,16,0,14)};filters.Controls.Add(Ui.Label("Du",9,true));filters.Controls.Add(_from);filters.Controls.Add(Ui.Label("Au",9,true));filters.Controls.Add(_through);filters.Controls.Add(_search);
        _saleStatus.Items.AddRange(new object[]{new StatusItem("","Tous les statuts"),new StatusItem("Validee","Validées"),new StatusItem("Annulee","Annulées")});_saleStatus.DisplayMember=nameof(StatusItem.Name);_saleStatus.SelectedIndex=0;filters.Controls.Add(_saleStatus);
        _stockStatus.Items.AddRange(new object[]{"Tous les statuts","En rupture","Stock faible","Disponible","Archivé"});_stockStatus.SelectedIndex=0;filters.Controls.Add(_stockStatus);
        _seller.Items.Add(new SellerItem(null,"Tous les vendeurs"));foreach(var user in users.List(actor,null).Where(x=>x.Role==UserRole.Vendeur))_seller.Items.Add(new SellerItem(user.UserId,user.Name));_seller.DisplayMember=nameof(SellerItem.Name);_seller.SelectedIndex=0;filters.Controls.Add(_seller);
        _apply.Click+=async(_,_)=>await LoadCurrentAsync();_export.Click+=async(_,_)=>await ExportCurrentAsync();filters.Controls.Add(_apply);filters.Controls.Add(_export);root.Controls.Add(filters,0,1);
        var salesTab=new TabPage("Ventes");salesTab.Controls.Add(_salesGrid);var productsTab=new TabPage("Produits vendus");productsTab.Controls.Add(_productsGrid);var stockTab=new TabPage("Stocks");stockTab.Controls.Add(_stockGrid);_tabs.TabPages.AddRange([salesTab,productsTab,stockTab]);_tabs.SelectedIndexChanged+=(_,_)=>{_pageIndex=0;UpdateFilterVisibility();_ = LoadCurrentAsync();};root.Controls.Add(_tabs,0,2);
        var footer=new FlowLayoutPanel{Dock=DockStyle.Fill,AutoSize=true,Padding=new Padding(0,12,0,0)};_previous.Click+=async(_,_)=>{if(_pageIndex>0){_pageIndex--;await LoadCurrentAsync(false);}};_next.Click+=async(_,_)=>{if(_hasMore){_pageIndex++;await LoadCurrentAsync(false);}};footer.Controls.Add(_previous);footer.Controls.Add(_page);footer.Controls.Add(_next);footer.Controls.Add(_status);root.Controls.Add(footer,0,3);
        _salesGrid.CellFormatting+=FormatStatus;_stockGrid.CellFormatting+=FormatStatus;_search.KeyDown+=async(_,e)=>{if(e.KeyCode==Keys.Enter){_pageIndex=0;await LoadCurrentAsync();e.SuppressKeyPress=true;}};
        var today=DateTime.ParseExact(reporting.CurrentBusinessDate(),"yyyy-MM-dd",System.Globalization.CultureInfo.InvariantCulture);_from.Value=today;_through.Value=today;UpdateFilterVisibility();Load+=async(_,_)=>await LoadCurrentAsync();
    }

    private async Task LoadCurrentAsync(bool resetPage=true)
    {
        if(_busy)return;if(resetPage)_pageIndex=0;SetBusy(true);var from=_from.Value.ToString("yyyy-MM-dd");var through=_through.Value.ToString("yyyy-MM-dd");var search=_search.Text;var page=_pageIndex;var tab=_tabs.SelectedIndex;
        try
        {
            if(tab==0)
            {
                var status=(_saleStatus.SelectedItem as StatusItem)?.Value;if(string.IsNullOrEmpty(status))status=null;var seller=(_seller.SelectedItem as SellerItem)?.Id;var result=await Task.Run(()=>_reporting.Sales(_actor,from,through,search,seller,status,page));if(IsDisposed||Disposing)return;_salesGrid.DataSource=result.Items.ToList();SetHeader(_salesGrid,nameof(SalesReportRow.Number),"NUMÉRO");SetHeader(_salesGrid,nameof(SalesReportRow.BusinessDate),"DATE MAGASIN");SetHeader(_salesGrid,nameof(SalesReportRow.DateUtc),"DATE / HEURE UTC");SetHeader(_salesGrid,nameof(SalesReportRow.Seller),"VENDEUR");SetHeader(_salesGrid,nameof(SalesReportRow.PaymentMode),"PAIEMENT");SetHeader(_salesGrid,nameof(SalesReportRow.Amount),"MONTANT XAF");SetHeader(_salesGrid,nameof(SalesReportRow.Status),"STATUT");Hide(_salesGrid,nameof(SalesReportRow.SaleId));SetPager(result);
            }
            else if(tab==1)
            {
                var result=await Task.Run(()=>_reporting.ProductSales(_actor,from,through,search,page));if(IsDisposed||Disposing)return;_productsGrid.DataSource=result.Items.ToList();SetHeader(_productsGrid,nameof(ProductSalesReportRow.Reference),"RÉFÉRENCE");SetHeader(_productsGrid,nameof(ProductSalesReportRow.Name),"PRODUIT");SetHeader(_productsGrid,nameof(ProductSalesReportRow.Quantity),"QUANTITÉ VENDUE");SetHeader(_productsGrid,nameof(ProductSalesReportRow.Amount),"MONTANT VENDU XAF");Hide(_productsGrid,nameof(ProductSalesReportRow.ProductId));SetPager(result);
            }
            else
            {
                var status=Convert.ToString(_stockStatus.SelectedItem);if(status=="Tous les statuts")status=null;var result=await Task.Run(()=>_reporting.Stocks(_actor,search,status,page));if(IsDisposed||Disposing)return;_stockGrid.DataSource=result.Items.ToList();SetHeader(_stockGrid,nameof(StockReportRow.Reference),"RÉFÉRENCE");SetHeader(_stockGrid,nameof(StockReportRow.Name),"PRODUIT");SetHeader(_stockGrid,nameof(StockReportRow.Quantity),"QUANTITÉ DISPONIBLE");SetHeader(_stockGrid,nameof(StockReportRow.Threshold),"SEUIL");SetHeader(_stockGrid,nameof(StockReportRow.Status),"STATUT");Hide(_stockGrid,nameof(StockReportRow.ProductId));SetPager(result);
            }
            var visibleCount=_tabs.SelectedIndex switch{0=>_salesGrid.Rows.Count,1=>_productsGrid.Rows.Count,_=>_stockGrid.Rows.Count};_status.ForeColor=visibleCount==0?Ui.Muted:Ui.Green;_status.Text=visibleCount==0?"Aucun résultat pour ces filtres.":$"Rapport mis à jour · {DateTime.Now:HH:mm}";
        }
        catch(Exception ex){if(!IsDisposed&&!Disposing){_status.ForeColor=Ui.Red;_status.Text=ex.Message;}}
        finally{if(!IsDisposed&&!Disposing)SetBusy(false);}
    }

    private async Task ExportCurrentAsync()
    {
        if(_tabs.SelectedIndex is not (0 or 2))return;using var dialog=new SaveFileDialog{Filter="Fichier CSV (*.csv)|*.csv",DefaultExt="csv",AddExtension=true,FileName=_tabs.SelectedIndex==0?"rapport_ventes.csv":"rapport_stocks.csv",OverwritePrompt=true};if(dialog.ShowDialog(this)!=DialogResult.OK)return;
        if(_busy)return;SetBusy(true);var path=dialog.FileName;var tab=_tabs.SelectedIndex;var from=_from.Value.ToString("yyyy-MM-dd");var through=_through.Value.ToString("yyyy-MM-dd");var search=_search.Text;var seller=(_seller.SelectedItem as SellerItem)?.Id;var saleStatus=(_saleStatus.SelectedItem as StatusItem)?.Value;if(string.IsNullOrEmpty(saleStatus))saleStatus=null;var stockStatus=Convert.ToString(_stockStatus.SelectedItem);if(stockStatus=="Tous les statuts")stockStatus=null;
        try
        {
            await Task.Run(()=>{using var stream=new FileStream(path,FileMode.Create,FileAccess.Write,FileShare.None);if(tab==0)CsvReportExporter.WriteSales(stream,_reporting.AllSales(_actor,from,through,search,seller,saleStatus));else CsvReportExporter.WriteStocks(stream,_reporting.AllStocks(_actor,search,stockStatus));});if(IsDisposed||Disposing)return;_status.ForeColor=Ui.Green;_status.Text="Export CSV enregistré.";
        }
        catch(Exception ex){if(!IsDisposed&&!Disposing){_status.ForeColor=Ui.Red;_status.Text=$"Export impossible : {ex.Message}";}}
        finally{if(!IsDisposed&&!Disposing)SetBusy(false);}
    }

    private void UpdateFilterVisibility()
    {
        var sales=_tabs.SelectedIndex==0;var stocks=_tabs.SelectedIndex==2;_saleStatus.Visible=sales;_seller.Visible=sales;_stockStatus.Visible=stocks;_from.Enabled=!stocks;_through.Enabled=!stocks;_export.Visible=sales||stocks;_export.Text="Exporter CSV";_search.PlaceholderText=sales?"Numéro de vente":stocks?"Référence ou produit":"Référence ou produit";
    }
    private void SetPager<T>(PageResult<T> result){_hasMore=result.HasMore;_previous.Enabled=_pageIndex>0;_next.Enabled=_hasMore;_page.Text=$"Page {_pageIndex+1} · {result.Items.Count} ligne(s)";}
    private void SetBusy(bool busy){_busy=busy;_apply.Enabled=!busy;_export.Enabled=!busy;_tabs.Enabled=!busy;_previous.Enabled=!busy&&_pageIndex>0;_next.Enabled=!busy&&_hasMore;_status.ForeColor=busy?Ui.Muted:Ui.Ink;if(busy)_status.Text="Chargement du rapport…";}
    private static void SetHeader(DataGridView grid,string name,string title){if(grid.Columns.Contains(name)&&grid.Columns[name] is{} column)column.HeaderText=title;}
    private static void Hide(DataGridView grid,string name){if(grid.Columns.Contains(name)&&grid.Columns[name] is{} column)column.Visible=false;}
    private static void FormatStatus(object? sender,DataGridViewCellFormattingEventArgs e)
    {
        if(sender is not DataGridView grid||e.RowIndex<0)return;var status=grid.Rows[e.RowIndex].DataBoundItem switch{SalesReportRow sale=>sale.Status,StockReportRow stock=>stock.Status,_=>null};if(status is null)return;var (back,fore)=status switch{"Annulee"=>(Color.FromArgb(254,226,226),Ui.Red),"En rupture"=>(Color.FromArgb(254,226,226),Ui.Red),"Stock faible"=>(Color.FromArgb(255,247,237),Ui.Orange),"Validee"=>(Color.FromArgb(220,252,231),Ui.Green),"Disponible"=>(Color.FromArgb(220,252,231),Ui.Green),_=>(Color.White,Ui.Ink)};e.CellStyle!.BackColor=back;e.CellStyle!.ForeColor=fore;e.CellStyle!.SelectionBackColor=back;e.CellStyle!.SelectionForeColor=fore;
    }
}
