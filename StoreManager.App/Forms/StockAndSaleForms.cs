using StoreManager.App.Controls;
using StoreManager.App.Models;
using StoreManager.App.Services;
using System.Drawing.Printing;

namespace StoreManager.App.Forms;

internal sealed class NewSaleForm : Form
{
    private sealed class CartRow
    {
        public long ProductId { get; init; }
        public string Reference { get; set; }="";
        public string Name { get; set; }="";
        public int Quantity { get; set; }
        public long UnitPrice { get; set; }
        public int Available { get; set; }
        public long Subtotal=>checked(UnitPrice*Quantity);
    }
    private sealed record PaymentChoice(PaymentMode Mode,string Label);
    private readonly UserSession _actor;private readonly SalesService _sales;private readonly TextBox _search=new(){Width=320,PlaceholderText="Référence ou désignation"};private readonly DataGridView _products=Grid();private readonly DataGridView _cart=Grid();private readonly NumericUpDown _quantity=new(){Minimum=1,Maximum=1000000,Value=1,Width=100,ThousandsSeparator=true};private readonly ComboBox _payment=new(){DropDownStyle=ComboBoxStyle.DropDownList,Width=220};private readonly NumericUpDown _received=new(){Minimum=0,Maximum=999999999999,DecimalPlaces=0,ThousandsSeparator=true,Width=220};private readonly Label _total=new(){AutoSize=true,Font=new Font("Segoe UI",22,FontStyle.Bold),ForeColor=Ui.Ink};private readonly Label _change=new(){AutoSize=true,Font=new Font("Segoe UI",10,FontStyle.Bold),ForeColor=Ui.Green};private readonly Label _message=new(){AutoSize=true,ForeColor=Ui.Red};private readonly Button _validate=Ui.Button("Valider et encaisser",true);private readonly List<CartRow> _rows=[];private IReadOnlyList<Produit> _found=[];private string _token=Guid.NewGuid().ToString("N");private bool _saving;

    public NewSaleForm(UserSession actor,SalesService sales)
    {
        _actor=actor;_sales=sales;Text="Nouvelle vente — NOVA";StartPosition=FormStartPosition.CenterParent;Size=new Size(1280,760);MinimumSize=new Size(1080,680);BackColor=Ui.Canvas;Font=new Font("Segoe UI",10);AutoScaleMode=AutoScaleMode.Font;
        var root=new TableLayoutPanel{Dock=DockStyle.Fill,ColumnCount=2,RowCount=1,Padding=new Padding(20)};root.ColumnStyles.Add(new ColumnStyle(SizeType.Percent,64));root.ColumnStyles.Add(new ColumnStyle(SizeType.Percent,36));Controls.Add(root);
        var left=CardPanel();var right=CardPanel();root.Controls.Add(left,0,0);root.Controls.Add(right,1,0);
        var leftLayout=new TableLayoutPanel{Dock=DockStyle.Fill,ColumnCount=1,RowCount=4};leftLayout.RowStyles.Add(new RowStyle(SizeType.AutoSize));leftLayout.RowStyles.Add(new RowStyle(SizeType.AutoSize));leftLayout.RowStyles.Add(new RowStyle(SizeType.Percent,52));leftLayout.RowStyles.Add(new RowStyle(SizeType.Percent,48));left.Controls.Add(leftLayout);
        leftLayout.Controls.Add(Ui.Label("Nouvelle vente",18,true),0,0);
        var searchPanel=new FlowLayoutPanel{Dock=DockStyle.Fill,AutoSize=true,Padding=new Padding(0,12,0,12)};searchPanel.Controls.Add(_search);var searchButton=Ui.Button("Rechercher");searchButton.Click+=(_,_)=>Search();searchPanel.Controls.Add(searchButton);searchPanel.Controls.Add(_quantity);var add=Ui.Button("＋ Ajouter au panier",true);add.Click+=(_,_)=>AddSelected();searchPanel.Controls.Add(add);leftLayout.Controls.Add(searchPanel,0,1);
        leftLayout.Controls.Add(_products,0,2);leftLayout.Controls.Add(_cart,0,3);_cart.MultiSelect=false;
        _products.SelectionChanged+=(_,_)=>{if(_products.CurrentRow?.DataBoundItem is Produit p)_quantity.Maximum=Math.Max(1,p.QuantiteStock);};_products.CellFormatting+=FormatAvailability;
        _search.KeyDown+=(_,e)=>{if(e.KeyCode==Keys.Enter){Search();e.SuppressKeyPress=true;}};
        var rightLayout=new TableLayoutPanel{Dock=DockStyle.Fill,ColumnCount=1,RowCount=10,Padding=new Padding(8)};right.Controls.Add(rightLayout);rightLayout.Controls.Add(Ui.Label("Récapitulatif et paiement",16,true));
        var counts=Ui.Label("Le panier ne réserve pas le stock. Le contrôle final se fait lors de la validation.",9,false,Ui.Muted);rightLayout.Controls.Add(counts);
        var cartActions=new FlowLayoutPanel{AutoSize=true};var update=Ui.Button("Appliquer la quantité");update.Click+=(_,_)=>UpdateSelectedQuantity();cartActions.Controls.Add(update);var remove=Ui.Button("Retirer la ligne");remove.Click+=(_,_)=>RemoveSelected();cartActions.Controls.Add(remove);rightLayout.Controls.Add(cartActions);
        _cart.SelectionChanged+=(_,_)=>{if(_cart.CurrentRow?.DataBoundItem is CartRow row){_quantity.Maximum=Math.Max(1,row.Available);_quantity.Value=Math.Min(row.Quantity,_quantity.Maximum);}};
        rightLayout.Controls.Add(Ui.Label("Mode de paiement",9,true));foreach(var choice in new[]{new PaymentChoice(PaymentMode.Especes,"Espèces"),new PaymentChoice(PaymentMode.MobileMoney,"Mobile Money"),new PaymentChoice(PaymentMode.Carte,"Carte bancaire")})_payment.Items.Add(choice);_payment.DisplayMember=nameof(PaymentChoice.Label);_payment.SelectedIndex=0;_payment.SelectedIndexChanged+=(_,_)=>UpdateTotals();rightLayout.Controls.Add(_payment);
        rightLayout.Controls.Add(Ui.Label("Montant reçu (FCFA XAF)",9,true));_received.ValueChanged+=(_,_)=>UpdateTotals();rightLayout.Controls.Add(_received);
        rightLayout.Controls.Add(Ui.Label("Total à payer (FCFA XAF)",9,true));rightLayout.Controls.Add(_total);
        rightLayout.Controls.Add(Ui.Label("Monnaie à rendre",9,true));rightLayout.Controls.Add(_change);
        _message.AutoSize=true;rightLayout.Controls.Add(_message);
        _validate.Height=44;_validate.Click+=ValidateSale;rightLayout.Controls.Add(_validate);
        var clear=Ui.Button("Vider le panier");clear.Click+=(_,_)=>{_rows.Clear();RefreshCart();UpdateTotals();};rightLayout.Controls.Add(clear);
        _products.CellDoubleClick+=(_,_)=>AddSelected();RefreshCart();Search();UpdateTotals();
    }

    private static Panel CardPanel()=>new(){Dock=DockStyle.Fill,BackColor=Color.White,Padding=new Padding(18),Margin=new Padding(0,0,12,0)};
    private static DataGridView Grid()=>new(){Dock=DockStyle.Fill,ReadOnly=true,AllowUserToAddRows=false,AllowUserToDeleteRows=false,AutoGenerateColumns=true,SelectionMode=DataGridViewSelectionMode.FullRowSelect,MultiSelect=false,RowHeadersVisible=false,BackgroundColor=Color.White,BorderStyle=BorderStyle.None,AutoSizeColumnsMode=DataGridViewAutoSizeColumnsMode.Fill,Font=new Font("Segoe UI",9),ColumnHeadersHeight=36,RowTemplate={Height=32}};
    private void Search()
    {
        try{_found=_sales.SearchProducts(_actor,_search.Text);_products.DataSource=_found.ToList();Header(_products,nameof(Produit.ProduitId),"ID");Header(_products,nameof(Produit.Reference),"RÉFÉRENCE");Header(_products,nameof(Produit.Nom),"PRODUIT");Header(_products,nameof(Produit.CategorieNom),"CATÉGORIE");Header(_products,nameof(Produit.PrixVente),"PRIX XAF");Header(_products,nameof(Produit.QuantiteStock),"STOCK");Header(_products,nameof(Produit.SeuilAlerte),"SEUIL");Header(_products,nameof(Produit.StatutStock),"STATUT");Header(_products,nameof(Produit.Actif),"ACTIF");Hide(_products,nameof(Produit.ProduitId));Hide(_products,nameof(Produit.CategorieId));Hide(_products,nameof(Produit.SeuilAlerte));Hide(_products,nameof(Produit.Actif));}
        catch(Exception ex){_message.Text=ex.Message;}
    }
    private void AddSelected()
    {
        if(_products.CurrentRow?.DataBoundItem is not Produit product)return;var quantity=decimal.ToInt32(_quantity.Value);var existing=_rows.SingleOrDefault(r=>r.ProductId==product.ProduitId);var combined=checked((existing?.Quantity??0)+quantity);
        if(combined>product.QuantiteStock){_message.Text=$"Stock disponible : {product.QuantiteStock} unité(s).";return;}
        if(existing is null)_rows.Add(new CartRow{ProductId=product.ProduitId,Reference=product.Reference,Name=product.Nom,Quantity=quantity,UnitPrice=product.PrixVente,Available=product.QuantiteStock});else{existing.Quantity=combined;existing.Available=product.QuantiteStock;existing.UnitPrice=product.PrixVente;existing.Name=product.Nom;existing.Reference=product.Reference;}
        _message.Text="";RefreshCart();UpdateTotals();
    }
    private void RemoveSelected(){if(_cart.CurrentRow?.DataBoundItem is CartRow row){_rows.RemoveAll(r=>r.ProductId==row.ProductId);RefreshCart();UpdateTotals();}}
    private void UpdateSelectedQuantity()
    {
        if(_cart.CurrentRow?.DataBoundItem is not CartRow selected){_message.Text="Sélectionnez une ligne du panier à modifier.";return;}
        var row=_rows.Single(r=>r.ProductId==selected.ProductId);var quantity=decimal.ToInt32(_quantity.Value);
        if(quantity>row.Available){_message.Text=$"Stock disponible : {row.Available} unité(s).";return;}
        row.Quantity=quantity;_message.Text="Quantité du panier mise à jour.";_message.ForeColor=Ui.Green;RefreshCart();UpdateTotals();
    }
    private void RefreshCart()
    {
        _cart.DataSource=null;_cart.DataSource=_rows.Select(r=>new CartRow{ProductId=r.ProductId,Reference=r.Reference,Name=r.Name,Quantity=r.Quantity,UnitPrice=r.UnitPrice,Available=r.Available}).ToList();
        Header(_cart,nameof(CartRow.Reference),"RÉFÉRENCE");Header(_cart,nameof(CartRow.Name),"PRODUIT");Header(_cart,nameof(CartRow.Quantity),"QTÉ");Header(_cart,nameof(CartRow.UnitPrice),"PRIX UNIT. XAF");Header(_cart,nameof(CartRow.Subtotal),"SOUS-TOTAL XAF");Hide(_cart,nameof(CartRow.ProductId));Hide(_cart,nameof(CartRow.Available));
    }
    private void UpdateTotals()
    {
        try{var total=_rows.Aggregate(0L,(sum,row)=>checked(sum+row.Subtotal));_total.Text=$"{total:N0} FCFA";var mode=(_payment.SelectedItem as PaymentChoice)?.Mode??PaymentMode.Especes;if(mode!=PaymentMode.Especes){_received.Value=Math.Min(_received.Maximum,total);_received.Enabled=false;_change.Text="0 FCFA";}else{_received.Enabled=true;var received=decimal.ToInt64(_received.Value);_change.Text=received>=total?$"{received-total:N0} FCFA":"Montant insuffisant";if(received<total)_change.ForeColor=Ui.Red;else _change.ForeColor=Ui.Green;}}
        catch(OverflowException){_total.Text="Total trop élevé";_message.Text="Le total dépasse la capacité autorisée.";}
    }
    private void ValidateSale(object? sender,EventArgs e)
    {
        if(_saving)return;if(_rows.Count==0){_message.Text="Ajoutez au moins un produit.";return;}_saving=true;_validate.Enabled=false;_message.Text="Validation en cours…";
        try
        {
            var mode=((PaymentChoice)_payment.SelectedItem!).Mode;var total=_rows.Aggregate(0L,(sum,row)=>checked(sum+row.Subtotal));var received=mode==PaymentMode.Especes?decimal.ToInt64(_received.Value):total;
            var drafts=_rows.Select(r=>new SaleLineDraft(r.ProductId,r.Quantity,r.UnitPrice,r.Reference,r.Name)).ToArray();var result=_sales.Validate(_actor,drafts,mode,received,_token);
            var detail=_sales.Detail(_actor,result.Sale.SaleId);_message.ForeColor=Ui.Green;_message.Text=result.AlreadyCommitted?"Cette tentative avait déjà été enregistrée.":$"Vente {result.Sale.Number} enregistrée.";
            using var ticket=new TicketDialog(detail);ticket.ShowDialog(this);
            _rows.Clear();_token=Guid.NewGuid().ToString("N");RefreshCart();UpdateTotals();
        }
        catch(SalePriceChangedException ex)
        {
            foreach(var updated in ex.CurrentLines){var row=_rows.Single(x=>x.ProductId==updated.ProductId);row.UnitPrice=updated.CurrentUnitPrice;row.Reference=updated.CurrentReference;row.Name=updated.CurrentName;}
            RefreshCart();UpdateTotals();_message.ForeColor=Ui.Orange;_message.Text=$"Les prix ont changé. Nouveau total : {ex.CurrentTotal:N0} FCFA. Vérifiez-le puis validez à nouveau.";
        }
        catch(Exception ex){_message.ForeColor=Ui.Red;_message.Text=ex.Message;}
        finally{_saving=false;_validate.Enabled=true;}
    }
    private static void Header(DataGridView grid,string property,string label){if(grid.Columns.Contains(property)&&grid.Columns[property] is{} col)col.HeaderText=label;}
    private static void Hide(DataGridView grid,string property){if(grid.Columns.Contains(property)&&grid.Columns[property] is{} col)col.Visible=false;}
    private static void FormatAvailability(object? sender,DataGridViewCellFormattingEventArgs e)
    {
        if(sender is not DataGridView grid||e.RowIndex<0||grid.Rows[e.RowIndex].DataBoundItem is not Produit p)return;
        var color=p.StatutStock switch{"En rupture"=>(Color.FromArgb(254,226,226),Ui.Red),"Stock faible"=>(Color.FromArgb(255,247,237),Ui.Orange),_=>(Color.FromArgb(220,252,231),Ui.Green)};e.CellStyle.BackColor=color.Item1;e.CellStyle.ForeColor=color.Item2;e.CellStyle.SelectionBackColor=color.Item1;e.CellStyle.SelectionForeColor=color.Item2;
    }
}

internal sealed class StockManagementForm : Form
{
    private sealed record CategoryChoice(long? Id,string Name);
    private readonly UserSession _actor;private readonly StockManagementService _stock;private readonly CatalogManagementService _catalog;private readonly TextBox _search=new(){Width=240,PlaceholderText="Référence ou désignation"};private readonly ComboBox _category=new(){DropDownStyle=ComboBoxStyle.DropDownList,Width=180};private readonly ComboBox _status=new(){DropDownStyle=ComboBoxStyle.DropDownList,Width=160};private readonly DataGridView _grid=MakeGridPublic();private readonly Label _page=new(){AutoSize=true};private readonly Button _previous=Ui.Button("← Précédent");private readonly Button _next=Ui.Button("Suivant →");private int _pageIndex;private bool _hasMore;
    public StockManagementForm(UserSession actor,StockManagementService stock,CatalogManagementService catalog,string? availabilityFilter=null)
    {
        _actor=actor;_stock=stock;_catalog=catalog;Text="Gestion des stocks — NOVA";StartPosition=FormStartPosition.CenterParent;Size=new Size(1240,740);MinimumSize=new Size(1080,680);BackColor=Ui.Canvas;Font=new Font("Segoe UI",10);AutoScaleMode=AutoScaleMode.Font;
        var root=new TableLayoutPanel{Dock=DockStyle.Fill,ColumnCount=1,RowCount=3,Padding=new Padding(18)};root.RowStyles.Add(new RowStyle(SizeType.AutoSize));root.RowStyles.Add(new RowStyle(SizeType.Percent,100));root.RowStyles.Add(new RowStyle(SizeType.AutoSize));Controls.Add(root);
        var filter=new FlowLayoutPanel{Dock=DockStyle.Fill,AutoSize=true,Padding=new Padding(0,0,0,12)};filter.Controls.Add(Ui.Label("Stock et mouvements",18,true));filter.Controls.Add(_search);foreach(var cat in catalog.Categories(actor))_category.Items.Add(new CategoryChoice(cat.CategoryId,cat.Name));_category.Items.Insert(0,new CategoryChoice(null,"Toutes catégories"));_category.DisplayMember=nameof(CategoryChoice.Name);_category.SelectedIndex=0;filter.Controls.Add(_category);_status.Items.AddRange(new object[]{"Produits actifs","En rupture","Stock faible","Disponible","Archivé"});_status.SelectedIndex=string.IsNullOrEmpty(availabilityFilter)?0:Math.Max(0,_status.Items.IndexOf(availabilityFilter));filter.Controls.Add(_status);var apply=Ui.Button("Filtrer");apply.Click+=(_,_)=>LoadRows();filter.Controls.Add(apply);root.Controls.Add(filter,0,0);root.Controls.Add(_grid,0,1);
        var footer=new FlowLayoutPanel{AutoSize=true,Dock=DockStyle.Fill,Padding=new Padding(0,10,0,0)};_previous.Click+=(_,_)=>{if(_pageIndex>0){_pageIndex--;LoadRows(false);}};_next.Click+=(_,_)=>{if(_hasMore){_pageIndex++;LoadRows(false);}};footer.Controls.Add(_previous);footer.Controls.Add(_page);footer.Controls.Add(_next);
        if(actor.Role==UserRole.Administrateur)
        {
            var initial=Ui.Button("Stock initial");initial.Click+=(_,_)=>Operate(StockOperation.Initial);footer.Controls.Add(initial);
            var intake=Ui.Button("Réapprovisionner",true);intake.Click+=(_,_)=>Operate(StockOperation.Replenish);footer.Controls.Add(intake);
            var count=Ui.Button("Correction d'inventaire");count.Click+=(_,_)=>Operate(StockOperation.Correction);footer.Controls.Add(count);
        }
        var movements=Ui.Button("Mouvements du produit");movements.Click+=(_,_)=>ShowMovements();footer.Controls.Add(movements);root.Controls.Add(footer,0,2);
        _search.KeyDown+=(_,e)=>{if(e.KeyCode==Keys.Enter){LoadRows();e.SuppressKeyPress=true;}};_grid.CellFormatting+=Format;_grid.CellDoubleClick+=(_,_)=>ShowMovements();LoadRows();
    }
    private void LoadRows(bool reset=true)
    {
        try{if(reset)_pageIndex=0;var status=Convert.ToString(_status.SelectedItem);if(status=="Produits actifs")status=null;var category=(_category.SelectedItem as CategoryChoice)?.Id;var result=_stock.Search(_actor,_search.Text,category,status,_pageIndex);_grid.DataSource=result.Items.ToList();_hasMore=result.HasMore;
            Header(nameof(Produit.Reference),"RÉFÉRENCE");Header(nameof(Produit.Nom),"DÉSIGNATION");Header(nameof(Produit.CategorieNom),"CATÉGORIE");Header(nameof(Produit.QuantiteStock),"QUANTITÉ");Header(nameof(Produit.SeuilAlerte),"SEUIL");Header(nameof(Produit.StatutStock),"STATUT");Header(nameof(Produit.PrixVente),"PRIX XAF");Hide(nameof(Produit.ProduitId));Hide(nameof(Produit.CategorieId));Hide(nameof(Produit.Actif));_page.Text=$"Page {_pageIndex+1} · {result.Items.Count} produit(s)";_previous.Enabled=_pageIndex>0;_next.Enabled=_hasMore;
        }catch(Exception ex){MessageBox.Show(ex.Message,"Chargement du stock",MessageBoxButtons.OK,MessageBoxIcon.Error);}
    }
    private void Operate(StockOperation operation)
    {
        if(_grid.CurrentRow?.DataBoundItem is not Produit p){MessageBox.Show("Sélectionnez un produit.");return;}
        if(operation==StockOperation.Initial&&_stock.HasInitial(p.ProduitId)){MessageBox.Show("Le stock initial de ce produit a déjà été enregistré.");return;}
        using var dialog=new StockOperationDialog(_actor,p,operation,_stock);if(dialog.ShowDialog(this)==DialogResult.OK)LoadRows();
    }
    private void ShowMovements(){if(_grid.CurrentRow?.DataBoundItem is Produit p){using var history=new MovementHistoryForm(_actor,p,_stock);history.ShowDialog(this);}}
    private void Header(string prop,string title){if(_grid.Columns.Contains(prop)&&_grid.Columns[prop] is{} col)col.HeaderText=title;}
    private void Hide(string prop){if(_grid.Columns.Contains(prop)&&_grid.Columns[prop] is{} col)col.Visible=false;}
    internal static DataGridView MakeGridPublic()=>new(){Dock=DockStyle.Fill,ReadOnly=true,AllowUserToAddRows=false,AllowUserToDeleteRows=false,AutoGenerateColumns=true,SelectionMode=DataGridViewSelectionMode.FullRowSelect,MultiSelect=false,RowHeadersVisible=false,BackgroundColor=Color.White,BorderStyle=BorderStyle.None,AutoSizeColumnsMode=DataGridViewAutoSizeColumnsMode.Fill,Font=new Font("Segoe UI",9),ColumnHeadersHeight=38,RowTemplate={Height=34}};
    private static void Format(object? sender,DataGridViewCellFormattingEventArgs e)
    {
        if(sender is not DataGridView g||e.RowIndex<0||g.Rows[e.RowIndex].DataBoundItem is not Produit p)return;var color=p.StatutStock switch{"En rupture"=>(Color.FromArgb(254,226,226),Ui.Red),"Stock faible"=>(Color.FromArgb(255,247,237),Ui.Orange),"Archivé"=>(Color.FromArgb(243,244,246),Ui.Muted),_=>(Color.FromArgb(220,252,231),Ui.Green)};e.CellStyle.BackColor=color.Item1;e.CellStyle.ForeColor=color.Item2;e.CellStyle.SelectionBackColor=color.Item1;e.CellStyle.SelectionForeColor=color.Item2;
    }
}

internal enum StockOperation { Initial,Replenish,Correction }

internal sealed class StockOperationDialog : Form
{
    private readonly NumericUpDown _quantity=new(){Minimum=0,Maximum=1000000,DecimalPlaces=0,ThousandsSeparator=true,Width=300};private readonly TextBox _reason=new(){Width=300};private readonly Label _error=new(){AutoSize=true,ForeColor=Ui.Red};private readonly UserSession _actor;private readonly Produit _product;private readonly StockOperation _operation;private readonly StockManagementService _service;
    public StockOperationDialog(UserSession actor,Produit product,StockOperation operation,StockManagementService service)
    {
        _actor=actor;_product=product;_operation=operation;_service=service;Text=operation switch{StockOperation.Initial=>"Stock initial",StockOperation.Replenish=>"Réapprovisionnement",_=>"Correction d'inventaire"};StartPosition=FormStartPosition.CenterParent;FormBorderStyle=FormBorderStyle.FixedDialog;ClientSize=new Size(500,330);Padding=new Padding(26);BackColor=Color.White;AutoScaleMode=AutoScaleMode.Font;
        var root=new TableLayoutPanel{Dock=DockStyle.Fill,ColumnCount=1,AutoSize=true};Controls.Add(root);root.Controls.Add(Ui.Label($"{product.Nom} · {product.Reference} · stock actuel {product.QuantiteStock}",11,true));
        var label=operation switch{StockOperation.Initial=>"Quantité initiale (0 ou plus)",StockOperation.Replenish=>"Quantité reçue (strictement positive)",_=>"Quantité réellement comptée"};root.Controls.Add(Ui.Label(label,9,true));root.Controls.Add(_quantity);
        if(operation==StockOperation.Replenish)_quantity.Minimum=1;
        if(operation!=StockOperation.Initial){root.Controls.Add(Ui.Label("Motif ou référence *",9,true));root.Controls.Add(_reason);}
        var hint=Ui.Label(operation==StockOperation.Correction?"L'écart signé sera calculé à partir du stock actuel.":"L'opération sera enregistrée dans l'historique.",9,false,Ui.Muted);root.Controls.Add(hint);root.Controls.Add(_error);
        var save=Ui.Button("Enregistrer le mouvement",true);save.Click+=Save;root.Controls.Add(save);AcceptButton=save;
    }
    private void Save(object? sender,EventArgs e)
    {
        try
        {
            var amount=decimal.ToInt32(_quantity.Value);switch(_operation){case StockOperation.Initial:_service.Initialize(_actor,_product.ProduitId,amount);break;case StockOperation.Replenish:_service.Replenish(_actor,_product.ProduitId,amount,_reason.Text);break;case StockOperation.Correction:var delta=_service.Correct(_actor,_product.ProduitId,amount,_reason.Text);MessageBox.Show($"Correction enregistrée. Écart : {delta:+#;-#;0} unité(s).","Confirmation",MessageBoxButtons.OK,MessageBoxIcon.Information);break;}
            if(_operation!=StockOperation.Correction)MessageBox.Show("Le mouvement de stock a été enregistré.","Confirmation",MessageBoxButtons.OK,MessageBoxIcon.Information);DialogResult=DialogResult.OK;
        }
        catch(Exception ex){_error.Text=ex.Message;}
    }
}

internal sealed class MovementHistoryForm : Form
{
    public MovementHistoryForm(UserSession actor,Produit product,StockManagementService stock)
    {
        _=actor;Text=$"Mouvements — {product.Reference}";StartPosition=FormStartPosition.CenterParent;Size=new Size(1050,600);BackColor=Ui.Canvas;Font=new Font("Segoe UI",10);AutoScaleMode=AutoScaleMode.Font;
        var root=new TableLayoutPanel{Dock=DockStyle.Fill,ColumnCount=1,RowCount=2,Padding=new Padding(18)};root.RowStyles.Add(new RowStyle(SizeType.AutoSize));root.RowStyles.Add(new RowStyle(SizeType.Percent,100));Controls.Add(root);root.Controls.Add(Ui.Label($"{product.Nom} · {product.Reference} · stock actuel {stock.CurrentBalance(product.ProduitId)}",16,true),0,0);
        var grid=StockManagementForm.MakeGridPublic();grid.DataSource=stock.Movements(actor,product.ProduitId).ToList();SetHeader(grid,nameof(StockMovement.TimestampUtc),"DATE UTC");SetHeader(grid,nameof(StockMovement.Type),"MOUVEMENT");SetHeader(grid,nameof(StockMovement.SignedQuantity),"ÉCART SIGNÉ");SetHeader(grid,nameof(StockMovement.BalanceAfter),"SOLDE APRÈS");SetHeader(grid,nameof(StockMovement.Reason),"MOTIF / ORIGINE");SetHeader(grid,nameof(StockMovement.Author),"AUTEUR");Hide(grid,nameof(StockMovement.MovementId));Hide(grid,nameof(StockMovement.ProductId));Hide(grid,nameof(StockMovement.Reference));Hide(grid,nameof(StockMovement.ProductName));root.Controls.Add(grid,0,1);
    }
    private static void SetHeader(DataGridView grid,string property,string text){if(grid.Columns.Contains(property)&&grid.Columns[property] is{} col)col.HeaderText=text;}
    private static void Hide(DataGridView grid,string property){if(grid.Columns.Contains(property)&&grid.Columns[property] is{} col)col.Visible=false;}
}

internal sealed class SalesHistoryForm : Form
{
    private sealed record SellerChoice(long? Id,string Name);
    private readonly UserSession _actor;private readonly SalesService _sales;private readonly UserManagementService _users;private readonly TextBox _search=new(){Width=220,PlaceholderText="Numéro de vente"};private readonly DateTimePicker _from=new(){Format=DateTimePickerFormat.Custom,CustomFormat="dd/MM/yyyy",ShowCheckBox=true,Checked=false,Width=130};private readonly DateTimePicker _through=new(){Format=DateTimePickerFormat.Custom,CustomFormat="dd/MM/yyyy",ShowCheckBox=true,Checked=false,Width=130};private readonly ComboBox _seller=new(){DropDownStyle=ComboBoxStyle.DropDownList,Width=200};private readonly ComboBox _status=new(){DropDownStyle=ComboBoxStyle.DropDownList,Width=150};private readonly DataGridView _grid=StockManagementForm.MakeGridPublic();private readonly Label _page=new(){AutoSize=true};private readonly Button _previous=Ui.Button("← Précédent");private readonly Button _next=Ui.Button("Suivant →");private int _pageIndex;private bool _hasMore;
    public SalesHistoryForm(UserSession actor,SalesService sales,UserManagementService users,string? fromDate=null,string? throughDate=null,string? initialStatus=null)
    {
        _actor=actor;_sales=sales;_users=users;Text="Historique des ventes — NOVA";StartPosition=FormStartPosition.CenterParent;Size=new Size(1240,720);MinimumSize=new Size(1080,640);BackColor=Ui.Canvas;Font=new Font("Segoe UI",10);AutoScaleMode=AutoScaleMode.Font;
        var root=new TableLayoutPanel{Dock=DockStyle.Fill,ColumnCount=1,RowCount=3,Padding=new Padding(18)};root.RowStyles.Add(new RowStyle(SizeType.AutoSize));root.RowStyles.Add(new RowStyle(SizeType.Percent,100));root.RowStyles.Add(new RowStyle(SizeType.AutoSize));Controls.Add(root);
        var filters=new FlowLayoutPanel{Dock=DockStyle.Fill,AutoSize=true,WrapContents=true,Padding=new Padding(0,0,0,12)};filters.Controls.Add(Ui.Label("Historique",18,true));filters.Controls.Add(_search);if(fromDate is not null){_from.Value=DateTime.ParseExact(fromDate,"yyyy-MM-dd",System.Globalization.CultureInfo.InvariantCulture);_from.Checked=true;}if(throughDate is not null){_through.Value=DateTime.ParseExact(throughDate,"yyyy-MM-dd",System.Globalization.CultureInfo.InvariantCulture);_through.Checked=true;}filters.Controls.Add(_from);filters.Controls.Add(_through);_status.Items.AddRange(new object[]{"Tous les statuts","Validée","Annulée"});_status.SelectedIndex=initialStatus switch{"Validee"=>1,"Annulee"=>2,_=>0};filters.Controls.Add(_status);
        if(actor.Role==UserRole.Administrateur){_seller.Items.Add(new SellerChoice(null,"Tous les vendeurs"));foreach(var seller in users.List(actor,null).Where(u=>u.Role==UserRole.Vendeur))_seller.Items.Add(new SellerChoice(seller.UserId,seller.Name));_seller.DisplayMember=nameof(SellerChoice.Name);_seller.SelectedIndex=0;filters.Controls.Add(_seller);}
        var apply=Ui.Button("Filtrer");apply.Click+=(_,_)=>LoadSales();filters.Controls.Add(apply);var detail=Ui.Button("Détail / ticket");detail.Click+=OpenDetail;filters.Controls.Add(detail);root.Controls.Add(filters,0,0);root.Controls.Add(_grid,0,1);
        var footer=new FlowLayoutPanel{AutoSize=true,Padding=new Padding(0,10,0,0)};_previous.Click+=(_,_)=>{if(_pageIndex>0){_pageIndex--;LoadSales(false);}};_next.Click+=(_,_)=>{if(_hasMore){_pageIndex++;LoadSales(false);}};footer.Controls.Add(_previous);footer.Controls.Add(_page);footer.Controls.Add(_next);root.Controls.Add(footer,0,2);
        _search.KeyDown+=(_,e)=>{if(e.KeyCode==Keys.Enter){LoadSales();e.SuppressKeyPress=true;}};_grid.CellDoubleClick+=(_,_)=>OpenDetail(this,EventArgs.Empty);LoadSales();
    }
    private void LoadSales(bool reset=true)
    {
        try{if(reset)_pageIndex=0;var status=Convert.ToString(_status.SelectedItem) switch{"Validée"=>"Validee","Annulée"=>"Annulee",_=>null};var seller=(_seller.SelectedItem as SellerChoice)?.Id;var from=_from.Checked?_from.Value.ToString("yyyy-MM-dd"):null;var through=_through.Checked?_through.Value.ToString("yyyy-MM-dd"):null;var result=_sales.History(_actor,_search.Text,from,through,seller,status,_pageIndex);_grid.DataSource=result.Items.ToList();Header(nameof(SaleSummary.Number),"NUMÉRO");Header(nameof(SaleSummary.DateUtc),"DATE / HEURE UTC");Header(nameof(SaleSummary.BusinessDate),"DATE MAGASIN");Header(nameof(SaleSummary.Seller),"VENDEUR");Header(nameof(SaleSummary.PaymentMode),"PAIEMENT");Header(nameof(SaleSummary.Total),"TOTAL XAF");Header(nameof(SaleSummary.Status),"STATUT");Hide(nameof(SaleSummary.SaleId));Hide(nameof(SaleSummary.SellerId));Hide(nameof(SaleSummary.AmountReceived));Hide(nameof(SaleSummary.Change));_page.Text=$"Page {_pageIndex+1} · {result.Items.Count} vente(s)";_hasMore=result.HasMore;_previous.Enabled=_pageIndex>0;_next.Enabled=_hasMore;}
        catch(Exception ex){MessageBox.Show(ex.Message,"Historique des ventes",MessageBoxButtons.OK,MessageBoxIcon.Error);}
    }
    private void OpenDetail(object? sender,EventArgs e){if(_grid.CurrentRow?.DataBoundItem is not SaleSummary sale)return;using var dialog=new SaleDetailForm(_actor,sale.SaleId,_sales);dialog.ShowDialog(this);LoadSales(false);}
    private void Header(string prop,string title){if(_grid.Columns.Contains(prop)&&_grid.Columns[prop] is{} col)col.HeaderText=title;}
    private void Hide(string prop){if(_grid.Columns.Contains(prop)&&_grid.Columns[prop] is{} col)col.Visible=false;}
}

internal sealed class SaleDetailForm : Form
{
    private readonly UserSession _actor;private readonly SalesService _sales;private readonly SaleDetail _detail;
    public SaleDetailForm(UserSession actor,long saleId,SalesService sales)
    {
        _actor=actor;_sales=sales;_detail=sales.Detail(actor,saleId);Text=$"Vente {_detail.Summary.Number}";StartPosition=FormStartPosition.CenterParent;Size=new Size(900,650);MinimumSize=new Size(760,540);BackColor=Ui.Canvas;Font=new Font("Segoe UI",10);AutoScaleMode=AutoScaleMode.Font;
        var root=new TableLayoutPanel{Dock=DockStyle.Fill,ColumnCount=1,RowCount=4,Padding=new Padding(18)};root.RowStyles.Add(new RowStyle(SizeType.AutoSize));root.RowStyles.Add(new RowStyle(SizeType.Percent,100));root.RowStyles.Add(new RowStyle(SizeType.AutoSize));root.RowStyles.Add(new RowStyle(SizeType.AutoSize));Controls.Add(root);
        root.Controls.Add(Ui.Label($"{_detail.Summary.Number} · {_detail.Summary.BusinessDate} · {_detail.Summary.Seller} · {_detail.Summary.Status}",16,true),0,0);
        var grid=StockManagementForm.MakeGridPublic();grid.DataSource=_detail.Lines.ToList();SetHeader(grid,nameof(SaleLineDetail.Reference),"RÉFÉRENCE");SetHeader(grid,nameof(SaleLineDetail.Name),"PRODUIT");SetHeader(grid,nameof(SaleLineDetail.Quantity),"QTÉ");SetHeader(grid,nameof(SaleLineDetail.UnitPrice),"PRIX UNIT. XAF");SetHeader(grid,nameof(SaleLineDetail.Subtotal),"TOTAL XAF");Hide(grid,nameof(SaleLineDetail.ProductId));root.Controls.Add(grid,0,1);
        root.Controls.Add(Ui.Label($"Total : {_detail.Summary.Total:N0} XAF   ·   Paiement : {_detail.Summary.PaymentMode}   ·   Reçu : {_detail.Summary.AmountReceived:N0}   ·   Monnaie : {_detail.Summary.Change:N0}",10,true),0,2);
        var buttons=new FlowLayoutPanel{AutoSize=true,FlowDirection=FlowDirection.LeftToRight,Padding=new Padding(0,12,0,0)};var preview=Ui.Button("Aperçu / imprimer le ticket",true);preview.Click+=(_,_)=>PreviewTicket();buttons.Controls.Add(preview);
        if(actor.Role==UserRole.Administrateur&&_detail.Summary.Status=="Validee"){var cancel=Ui.Button("Annuler la vente");cancel.Click+=CancelSale;buttons.Controls.Add(cancel);}root.Controls.Add(buttons,0,3);
    }
    private void PreviewTicket(){try{using var dialog=new TicketDialog(_detail);dialog.ShowDialog(this);}catch(Exception ex){MessageBox.Show($"Impossible d'ouvrir l'aperçu du ticket : {ex.Message}\nLa vente reste enregistrée.","Ticket",MessageBoxButtons.OK,MessageBoxIcon.Warning);}}
    private void CancelSale(object? sender,EventArgs e){using var dialog=new CancellationReasonDialog();if(dialog.ShowDialog(this)!=DialogResult.OK)return;if(MessageBox.Show($"Annuler intégralement la vente {_detail.Summary.Number} et rétablir les stocks ?","Confirmer l'annulation",MessageBoxButtons.YesNo,MessageBoxIcon.Warning)!=DialogResult.Yes)return;try{_sales.Cancel(_actor,_detail.Summary.SaleId,dialog.Reason);MessageBox.Show("La vente a été annulée et les stocks rétablis.","Confirmation",MessageBoxButtons.OK,MessageBoxIcon.Information);Close();}catch(Exception ex){MessageBox.Show(ex.Message,"Annulation impossible",MessageBoxButtons.OK,MessageBoxIcon.Error);}}
    private static void SetHeader(DataGridView g,string p,string t){if(g.Columns.Contains(p)&&g.Columns[p] is{} c)c.HeaderText=t;}
    private static void Hide(DataGridView g,string p){if(g.Columns.Contains(p)&&g.Columns[p] is{} c)c.Visible=false;}
}

internal sealed class CancellationReasonDialog : Form
{
    private readonly TextBox _reason=new(){Multiline=true,Width=380,Height=90};private readonly Label _error=new(){AutoSize=true,ForeColor=Ui.Red};public string Reason=>_reason.Text;
    public CancellationReasonDialog(){Text="Motif d'annulation";StartPosition=FormStartPosition.CenterParent;FormBorderStyle=FormBorderStyle.FixedDialog;ClientSize=new Size(470,260);Padding=new Padding(24);BackColor=Color.White;AutoScaleMode=AutoScaleMode.Font;var root=new TableLayoutPanel{Dock=DockStyle.Fill,AutoSize=true,ColumnCount=1};Controls.Add(root);root.Controls.Add(Ui.Label("Motif obligatoire",10,true));root.Controls.Add(_reason);root.Controls.Add(_error);var cancel=Ui.Button("Annuler",true);cancel.Click+=(_,_)=>{if(string.IsNullOrWhiteSpace(_reason.Text)){_error.Text="Saisissez le motif de l'annulation.";return;}DialogResult=DialogResult.OK;};root.Controls.Add(cancel);AcceptButton=cancel;}
}

internal sealed class TicketDialog : Form
{
    private readonly PrintDocument _document;
    public TicketDialog(SaleDetail detail)
    {
        Text=$"Ticket {_detailNumber(detail)}";StartPosition=FormStartPosition.CenterParent;Size=new Size(1000,760);BackColor=Ui.Canvas;_document=new TicketPrintDocument(detail);
        var root=new TableLayoutPanel{Dock=DockStyle.Fill,ColumnCount=1,RowCount=1,Padding=new Padding(12)};Controls.Add(root);var buttons=new FlowLayoutPanel{AutoSize=true};var preview=Ui.Button("Aperçu",true);preview.Click+=(_,_)=>ShowPreview();var print=Ui.Button("Imprimer");print.Click+=(_,_)=>Print();buttons.Controls.Add(preview);buttons.Controls.Add(print);root.Controls.Add(buttons,0,0);
    }
    private static string _detailNumber(SaleDetail d)=>d.Summary.Number;
    private void ShowPreview(){try{using var preview=new PrintPreviewDialog{Document=_document,Width=1000,Height=800};preview.ShowDialog(this);}catch(Exception ex){MessageBox.Show($"L'aperçu d'impression est indisponible : {ex.Message}\nLa vente reste enregistrée.","Aperçu du ticket",MessageBoxButtons.OK,MessageBoxIcon.Warning);}}
    private void Print(){try{using var dialog=new PrintDialog{Document=_document,UseEXDialog=true};if(dialog.ShowDialog(this)==DialogResult.OK)_document.Print();}catch(Exception ex){MessageBox.Show($"L'impression a échoué : {ex.Message}\nLa vente reste enregistrée et le ticket reste réimprimable.","Impression",MessageBoxButtons.OK,MessageBoxIcon.Warning);}}
}

internal sealed class TicketPrintDocument : PrintDocument
{
    private readonly SaleDetail _detail;private readonly IReadOnlyList<string> _lines;private int _index;
    public TicketPrintDocument(SaleDetail detail){_detail=detail;_lines=TicketLines(detail);DefaultPageSettings.Margins=new Margins(28,28,28,28);PrintPage+=DrawPage;BeginPrint+=(_,_)=>_index=0;}
    internal static IReadOnlyList<string> TicketLines(SaleDetail d)
    {
        var zone=TimeZoneInfo.FindSystemTimeZoneById(d.TimeZoneId);var utc=DateTime.Parse(d.Summary.DateUtc,null,System.Globalization.DateTimeStyles.RoundtripKind).ToUniversalTime();var local=TimeZoneInfo.ConvertTimeFromUtc(utc,zone);
        var lines=new List<string>{$"             {d.StoreName}","             TICKET DE VENTE",$"Vente : {d.Summary.Number}",$"Date : {local:dd/MM/yyyy HH:mm:ss}",$"Vendeur : {d.Summary.Seller}",new string('-',54)};
        foreach(var item in d.Lines){lines.Add(item.Name);lines.Add($"{item.Reference}  {item.Quantity} x {item.UnitPrice:N0} = {item.Subtotal:N0} XAF");}
        lines.Add(new string('-',54));lines.Add($"TOTAL : {d.Summary.Total:N0} XAF");lines.Add($"Paiement : {d.Summary.PaymentMode}");lines.Add($"Montant reçu : {d.Summary.AmountReceived:N0} XAF");if(d.Summary.Change>0)lines.Add($"Monnaie rendue : {d.Summary.Change:N0} XAF");lines.Add("");lines.Add("       Merci de votre visite.");return lines;
    }
    private void DrawPage(object? sender,PrintPageEventArgs e)
    {
        if(e.Graphics is not{} graphics)return;using var font=new Font("Consolas",9);using var brush=new SolidBrush(Color.Black);float y=e.MarginBounds.Top;var lineHeight=font.GetHeight(graphics)+5;
        while(_index<_lines.Count&&y+lineHeight<=e.MarginBounds.Bottom){graphics.DrawString(_lines[_index++],font,brush,e.MarginBounds.Left,y);y+=lineHeight;}
        e.HasMorePages=_index<_lines.Count;
    }
}
