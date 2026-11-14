using StoreManager.App.Controls;
using StoreManager.App.Models;
using StoreManager.App.Services;

namespace StoreManager.App.Forms;

internal abstract class AdminListForm : Form
{
    protected readonly UserSession Actor;
    protected AdminListForm(UserSession actor,string title)
    {
        Actor=actor;Text=title;StartPosition=FormStartPosition.CenterParent;Size=new Size(1050,680);MinimumSize=new Size(900,560);BackColor=Ui.Canvas;Font=new Font("Segoe UI",10);AutoScaleMode=AutoScaleMode.Font;
    }
    protected static DataGridView Grid()
    {
        var grid=new DataGridView{Dock=DockStyle.Fill,ReadOnly=true,AllowUserToAddRows=false,AllowUserToDeleteRows=false,AutoGenerateColumns=true,SelectionMode=DataGridViewSelectionMode.FullRowSelect,MultiSelect=false,RowHeadersVisible=false,BackgroundColor=Color.White,BorderStyle=BorderStyle.None,AutoSizeColumnsMode=DataGridViewAutoSizeColumnsMode.Fill,Font=new Font("Segoe UI",9),ColumnHeadersHeight=38,RowTemplate={Height=34}};
        grid.Paint+=(_,e)=>{if(grid.Rows.Count==0)TextRenderer.DrawText(e.Graphics,"Aucun résultat à afficher.",grid.Font,grid.ClientRectangle,Ui.Muted,TextFormatFlags.HorizontalCenter|TextFormatFlags.VerticalCenter);};
        return grid;
    }
    protected static void Header(DataGridView grid,string property,string title){if(grid.Columns.Contains(property)&&grid.Columns[property] is { } column)column.HeaderText=title;}
    protected static void ShowError(Exception ex)=>MessageBox.Show(ex.Message,"Opération impossible",MessageBoxButtons.OK,MessageBoxIcon.Warning);
}

internal sealed class UserManagementForm : AdminListForm
{
    private readonly UserManagementService _service;private readonly TextBox _search=new(){Width=300,PlaceholderText="Nom ou identifiant"};private readonly DataGridView _grid=Grid();
    public UserManagementForm(UserSession actor,UserManagementService service):base(actor,"Administration — Utilisateurs")
    {
        _service=service;var root=new TableLayoutPanel{Dock=DockStyle.Fill,RowCount=3,ColumnCount=1,Padding=new Padding(18)};root.RowStyles.Add(new RowStyle(SizeType.AutoSize));root.RowStyles.Add(new RowStyle(SizeType.Percent,100));root.RowStyles.Add(new RowStyle(SizeType.AutoSize));Controls.Add(root);
        var top=new FlowLayoutPanel{Dock=DockStyle.Fill,AutoSize=true,Padding=new Padding(0,0,0,12)};top.Controls.Add(Ui.Label("Comptes et rôles",16,true));top.Controls.Add(_search);var find=Ui.Button("Rechercher");find.Click+=(_,_)=>RefreshRows();top.Controls.Add(find);var add=Ui.Button("＋ Ajouter un utilisateur",true);add.Click+=Add;top.Controls.Add(add);root.Controls.Add(top,0,0);
        root.Controls.Add(_grid,0,1);var actions=new FlowLayoutPanel{Dock=DockStyle.Fill,AutoSize=true,Padding=new Padding(0,12,0,0)};
        var edit=Ui.Button("Modifier");edit.Click+=Edit;actions.Controls.Add(edit);var toggle=Ui.Button("Activer / désactiver");toggle.Click+=Toggle;actions.Controls.Add(toggle);var reset=Ui.Button("Réinitialiser le mot de passe");reset.Click+=Reset;actions.Controls.Add(reset);root.Controls.Add(actions,0,2);_search.KeyDown+=(_,e)=>{if(e.KeyCode==Keys.Enter){RefreshRows();e.SuppressKeyPress=true;}};RefreshRows();
    }
    private UserSummary? Selected=>_grid.CurrentRow?.DataBoundItem as UserSummary;
    private void RefreshRows(){try{_grid.DataSource=_service.List(Actor,_search.Text).ToList();Header(_grid,nameof(UserSummary.UserId),"ID");Header(_grid,nameof(UserSummary.Name),"NOM");Header(_grid,nameof(UserSummary.Username),"IDENTIFIANT");Header(_grid,nameof(UserSummary.Role),"RÔLE");Header(_grid,nameof(UserSummary.Active),"ACTIF");}catch(Exception ex){ShowError(ex);}}
    private void Add(object? s,EventArgs e){using var f=new UserEditDialog(null);if(f.ShowDialog(this)==DialogResult.OK)try{_service.Create(Actor,f.NameValue,f.UsernameValue,f.RoleValue,f.PasswordValue);RefreshRows();}catch(Exception ex){ShowError(ex);}}
    private void Edit(object? s,EventArgs e){var u=Selected;if(u is null)return;using var f=new UserEditDialog(u);if(f.ShowDialog(this)==DialogResult.OK)try{_service.Update(Actor,u.UserId,f.NameValue,f.UsernameValue,f.RoleValue,f.ActiveValue);RefreshRows();}catch(Exception ex){ShowError(ex);}}
    private void Toggle(object? s,EventArgs e){var u=Selected;if(u is null)return;if(MessageBox.Show($"{(u.Active?"Désactiver":"Activer")} le compte {u.Username} ?","Confirmer",MessageBoxButtons.YesNo,MessageBoxIcon.Question)!=DialogResult.Yes)return;try{_service.Update(Actor,u.UserId,u.Name,u.Username,u.Role,!u.Active);RefreshRows();}catch(Exception ex){ShowError(ex);}}
    private void Reset(object? s,EventArgs e){var u=Selected;if(u is null)return;using var f=new PasswordResetDialog();if(f.ShowDialog(this)==DialogResult.OK)try{_service.ResetPassword(Actor,u.UserId,f.PasswordValue);MessageBox.Show("Le mot de passe a été réinitialisé.","Confirmation",MessageBoxButtons.OK,MessageBoxIcon.Information);}catch(Exception ex){ShowError(ex);}}
}

internal sealed class UserEditDialog : Form
{
    private readonly TextBox _name=new(){Width=340};private readonly TextBox _username=new(){Width=340};private readonly TextBox _password=new(){Width=340,UseSystemPasswordChar=true};private readonly ComboBox _role=new(){Width=340,DropDownStyle=ComboBoxStyle.DropDownList};private readonly CheckBox _active=new(){Text="Compte actif",Checked=true,AutoSize=true};private readonly bool _creating;
    public string NameValue=>_name.Text;public string UsernameValue=>_username.Text;public string PasswordValue=>_password.Text;public UserRole RoleValue=>(UserRole)_role.SelectedItem!;public bool ActiveValue=>_active.Checked;
    public UserEditDialog(UserSummary? user)
    {
        _creating=user is null;Text=_creating?"Nouvel utilisateur":"Modifier l'utilisateur";StartPosition=FormStartPosition.CenterParent;FormBorderStyle=FormBorderStyle.FixedDialog;MaximizeBox=false;MinimizeBox=false;ClientSize=new Size(460,_creating?400:350);BackColor=Color.White;Padding=new Padding(28);AutoScaleMode=AutoScaleMode.Font;
        var layout=new TableLayoutPanel{Dock=DockStyle.Fill,ColumnCount=1,AutoSize=true};Controls.Add(layout);Add(layout,"Nom complet",_name);Add(layout,"Identifiant",_username);Add(layout,"Rôle",_role);_role.Items.AddRange(new object[]{UserRole.Administrateur,UserRole.Vendeur});_role.SelectedIndex=1;
        if(_creating)Add(layout,"Mot de passe initial (12 caractères minimum)",_password);else{_name.Text=user!.Name;_username.Text=user.Username;_role.SelectedItem=user.Role;_active.Checked=user.Active;layout.Controls.Add(_active);}
        var buttons=new FlowLayoutPanel{AutoSize=true,FlowDirection=FlowDirection.RightToLeft,Padding=new Padding(0,18,0,0)};var save=Ui.Button("Enregistrer",true);save.Click+=(_,_)=>{if(_creating&&_password.Text.Length<12){MessageBox.Show("Le mot de passe doit comporter au moins 12 caractères.");return;}DialogResult=DialogResult.OK;};var cancel=Ui.Button("Annuler");cancel.Click+=(_,_)=>DialogResult=DialogResult.Cancel;buttons.Controls.Add(save);buttons.Controls.Add(cancel);layout.Controls.Add(buttons);AcceptButton=save;CancelButton=cancel;
    }
    private static void Add(TableLayoutPanel p,string label,Control input){p.Controls.Add(Ui.Label(label,9,true));input.Margin=new Padding(0,0,0,8);p.Controls.Add(input);}
}

internal sealed class PasswordResetDialog : Form
{
    private readonly TextBox _password=new(){Width=330,UseSystemPasswordChar=true};private readonly TextBox _confirm=new(){Width=330,UseSystemPasswordChar=true};public string PasswordValue=>_password.Text;
    public PasswordResetDialog(){Text="Réinitialiser le mot de passe";StartPosition=FormStartPosition.CenterParent;FormBorderStyle=FormBorderStyle.FixedDialog;ClientSize=new Size(430,250);Padding=new Padding(24);BackColor=Color.White;AutoScaleMode=AutoScaleMode.Font;var p=new TableLayoutPanel{Dock=DockStyle.Fill,AutoSize=true,ColumnCount=1};Controls.Add(p);p.Controls.Add(Ui.Label("Nouveau mot de passe (12 caractères minimum)",9,true));p.Controls.Add(_password);p.Controls.Add(Ui.Label("Confirmer le mot de passe",9,true));p.Controls.Add(_confirm);var b=Ui.Button("Réinitialiser",true);b.Click+=(_,_)=>{if(_password.Text.Length<12||_password.Text!=_confirm.Text){MessageBox.Show("Vérifiez les deux mots de passe (12 caractères minimum).");return;}DialogResult=DialogResult.OK;};p.Controls.Add(b);AcceptButton=b;}
}

internal sealed class CategoryManagementForm : AdminListForm
{
    private readonly CatalogManagementService _service;private readonly TextBox _search=new(){Width=300,PlaceholderText="Rechercher une catégorie"};private readonly DataGridView _grid=Grid();
    public CategoryManagementForm(UserSession actor,CatalogManagementService service):base(actor,"Catalogue — Catégories")
    {
        _service=service;var root=new TableLayoutPanel{Dock=DockStyle.Fill,RowCount=3,ColumnCount=1,Padding=new Padding(18)};root.RowStyles.Add(new RowStyle(SizeType.AutoSize));root.RowStyles.Add(new RowStyle(SizeType.Percent,100));root.RowStyles.Add(new RowStyle(SizeType.AutoSize));Controls.Add(root);
        var top=new FlowLayoutPanel{AutoSize=true};top.Controls.Add(Ui.Label("Catégories",16,true));top.Controls.Add(_search);var find=Ui.Button("Rechercher");find.Click+=(_,_)=>RefreshRows();top.Controls.Add(find);var add=Ui.Button("＋ Ajouter une catégorie",true);add.Click+=Add;top.Controls.Add(add);root.Controls.Add(top,0,0);root.Controls.Add(_grid,0,1);
        var edit=Ui.Button("Modifier");edit.Click+=Edit;root.Controls.Add(edit,0,2);RefreshRows();_search.KeyDown+=(_,e)=>{if(e.KeyCode==Keys.Enter){RefreshRows();e.SuppressKeyPress=true;}};
    }
    private Category? Selected=>_grid.CurrentRow?.DataBoundItem as Category;
    private void RefreshRows(){try{_grid.DataSource=_service.Categories(Actor,_search.Text).ToList();Header(_grid,nameof(Category.CategoryId),"ID");Header(_grid,nameof(Category.Name),"NOM");Header(_grid,nameof(Category.Active),"ACTIVE");}catch(Exception ex){ShowError(ex);}}
    private void Add(object? s,EventArgs e){using var f=new CategoryEditDialog(null);if(f.ShowDialog(this)==DialogResult.OK)try{_service.SaveCategory(Actor,null,f.NameValue,f.ActiveValue);RefreshRows();}catch(Exception ex){ShowError(ex);}}
    private void Edit(object? s,EventArgs e){var c=Selected;if(c is null)return;using var f=new CategoryEditDialog(c);if(f.ShowDialog(this)==DialogResult.OK)try{_service.SaveCategory(Actor,c.CategoryId,f.NameValue,f.ActiveValue);RefreshRows();}catch(Exception ex){ShowError(ex);}}
}

internal sealed class CategoryEditDialog : Form
{
    private readonly TextBox _name=new(){Width=340};private readonly CheckBox _active=new(){Text="Catégorie active",Checked=true,AutoSize=true};public string NameValue=>_name.Text;public bool ActiveValue=>_active.Checked;
    public CategoryEditDialog(Category? item){Text=item is null?"Nouvelle catégorie":"Modifier la catégorie";StartPosition=FormStartPosition.CenterParent;FormBorderStyle=FormBorderStyle.FixedDialog;ClientSize=new Size(430,190);Padding=new Padding(24);BackColor=Color.White;AutoScaleMode=AutoScaleMode.Font;var p=new TableLayoutPanel{Dock=DockStyle.Fill,AutoSize=true,ColumnCount=1};Controls.Add(p);p.Controls.Add(Ui.Label("Nom de catégorie",9,true));_name.Text=item?.Name??"";p.Controls.Add(_name);_active.Checked=item?.Active??true;p.Controls.Add(_active);var save=Ui.Button("Enregistrer",true);save.Click+=(_,_)=>{if(string.IsNullOrWhiteSpace(_name.Text)){MessageBox.Show("Le nom est obligatoire.");return;}DialogResult=DialogResult.OK;};p.Controls.Add(save);AcceptButton=save;}
}

internal sealed class ProductManagementForm : AdminListForm
{
    private readonly CatalogManagementService _service;private readonly TextBox _search=new(){Width=240,PlaceholderText="Référence ou désignation"};private readonly ComboBox _category=new(){Width=175,DropDownStyle=ComboBoxStyle.DropDownList};private readonly ComboBox _active=new(){Width=120,DropDownStyle=ComboBoxStyle.DropDownList};private readonly ComboBox _availability=new(){Width=150,DropDownStyle=ComboBoxStyle.DropDownList};private readonly DataGridView _grid=Grid();private readonly Label _pageLabel=Ui.Label("Page 1",9,false,Ui.Muted);private readonly Button _previous=Ui.Button("← Précédent");private readonly Button _next=Ui.Button("Suivant →");private int _page;
    public ProductManagementForm(UserSession actor,CatalogManagementService service):base(actor,"Catalogue — Produits")
    {
        _service=service;_category.Items.Add(new CategoryChoice(null,"Toutes catégories"));foreach(var c in service.Categories(actor))_category.Items.Add(new CategoryChoice(c.CategoryId,c.Name));_category.DisplayMember=nameof(CategoryChoice.Label);_category.SelectedIndex=0;
        _active.Items.Add(new ActiveChoice(null,"Tous les états"));_active.Items.Add(new ActiveChoice(true,"Actifs"));_active.Items.Add(new ActiveChoice(false,"Archivés"));_active.DisplayMember=nameof(ActiveChoice.Label);_active.SelectedIndex=0;
        var root=new TableLayoutPanel{Dock=DockStyle.Fill,RowCount=4,ColumnCount=1,Padding=new Padding(18)};root.RowStyles.Add(new RowStyle(SizeType.AutoSize));root.RowStyles.Add(new RowStyle(SizeType.Percent,100));root.RowStyles.Add(new RowStyle(SizeType.AutoSize));root.RowStyles.Add(new RowStyle(SizeType.AutoSize));Controls.Add(root);
        _availability.Items.AddRange(new object[]{"Toutes disponibilités","En rupture","Stock faible","Disponible"});_availability.SelectedIndex=0;
        var top=new FlowLayoutPanel{AutoSize=true,WrapContents=true};top.Controls.Add(Ui.Label("Produits",16,true));top.Controls.Add(_search);top.Controls.Add(_category);top.Controls.Add(_active);top.Controls.Add(_availability);var find=Ui.Button("Filtrer");find.Click+=(_,_)=>RefreshRows();top.Controls.Add(find);if(actor.Role==UserRole.Administrateur){var add=Ui.Button("＋ Nouveau produit",true);add.Click+=Add;top.Controls.Add(add);}root.Controls.Add(top,0,0);root.Controls.Add(_grid,0,1);
        var paging=new FlowLayoutPanel{AutoSize=true,FlowDirection=FlowDirection.LeftToRight,Padding=new Padding(0,8,0,8)};_previous.Click+=(_,_)=>{if(_page>0){_page--;RefreshRows(false);}};_next.Click+=(_,_)=>{_page++;RefreshRows(false);};paging.Controls.Add(_previous);paging.Controls.Add(_pageLabel);paging.Controls.Add(_next);root.Controls.Add(paging,0,2);
        if(actor.Role==UserRole.Administrateur){var edit=Ui.Button("Modifier / archiver");edit.Click+=Edit;root.Controls.Add(edit,0,3);}else root.RowStyles[3]=new RowStyle(SizeType.Absolute,0);
        _search.KeyDown+=(_,e)=>{if(e.KeyCode==Keys.Enter){RefreshRows();e.SuppressKeyPress=true;}};RefreshRows();
        _grid.CellFormatting+=(_,e)=>
        {
            if(e.RowIndex<0||_grid.Rows[e.RowIndex].DataBoundItem is not Produit product)return;
            var status=product.StatutStock;
            if(status=="Archivé")SetStatusStyle(e.CellStyle,Color.FromArgb(243,244,246),Ui.Muted);
            else if(status=="En rupture")SetStatusStyle(e.CellStyle,Color.FromArgb(254,226,226),Ui.Red);
            else if(status=="Stock faible")SetStatusStyle(e.CellStyle,Color.FromArgb(255,247,237),Ui.Orange);
            else SetStatusStyle(e.CellStyle,Color.FromArgb(220,252,231),Ui.Green);
        };
    }
    private static void SetStatusStyle(DataGridViewCellStyle? style,Color back,Color fore){if(style is null)return;style.BackColor=back;style.ForeColor=fore;style.SelectionBackColor=back;style.SelectionForeColor=fore;}
    private sealed record CategoryChoice(long? Id,string Label);
    private sealed record ActiveChoice(bool? Value,string Label);
    private Produit? Selected=>_grid.CurrentRow?.DataBoundItem as Produit;
    private void RefreshRows(bool resetPage=true){try{if(resetPage)_page=0;var cat=(_category.SelectedItem as CategoryChoice)?.Id;var active=(_active.SelectedItem as ActiveChoice)?.Value;var availability=Convert.ToString(_availability.SelectedItem);if(availability=="Toutes disponibilités")availability=null;var result=_service.ProductsPage(Actor,_search.Text,cat,active,availability,_page);_grid.DataSource=result.Items.ToList();Header(_grid,nameof(Produit.ProduitId),"ID");Header(_grid,nameof(Produit.Reference),"RÉFÉRENCE");Header(_grid,nameof(Produit.Nom),"DÉSIGNATION");Header(_grid,nameof(Produit.CategorieNom),"CATÉGORIE");Header(_grid,nameof(Produit.PrixVente),"PRIX DE VENTE (XAF)");Header(_grid,nameof(Produit.QuantiteStock),"STOCK");Header(_grid,nameof(Produit.SeuilAlerte),"SEUIL");Header(_grid,nameof(Produit.StatutStock),"DISPONIBILITÉ");Header(_grid,nameof(Produit.Actif),"ACTIF");if(_grid.Columns[nameof(Produit.PrixVente)] is { } priceColumn)priceColumn.DefaultCellStyle.Alignment=DataGridViewContentAlignment.MiddleRight;_pageLabel.Text=$"Page {_page+1} · {result.Items.Count} produit(s)";_previous.Enabled=_page>0;_next.Enabled=result.HasMore;}catch(Exception ex){ShowError(ex);}}
    private void Add(object? s,EventArgs e){using var f=new ProductEditDialog(_service.Categories(Actor),null);if(f.ShowDialog(this)==DialogResult.OK)try{_service.SaveProduct(Actor,null,f.ReferenceValue,f.NameValue,f.CategoryValue,f.PriceValue,f.ThresholdValue,true);RefreshRows();}catch(Exception ex){ShowError(ex);}}
    private void Edit(object? s,EventArgs e){var p=Selected;if(p is null)return;using var f=new ProductEditDialog(_service.Categories(Actor),p);if(f.ShowDialog(this)==DialogResult.OK)try{_service.SaveProduct(Actor,p.ProduitId,f.ReferenceValue,f.NameValue,f.CategoryValue,f.PriceValue,f.ThresholdValue,f.ActiveValue);RefreshRows();}catch(Exception ex){ShowError(ex);}}
}

internal sealed class ProductEditDialog : Form
{
    private readonly TextBox _reference=new(){Width=360};private readonly TextBox _name=new(){Width=360};private readonly ComboBox _category=new(){Width=360,DropDownStyle=ComboBoxStyle.DropDownList};private readonly NumericUpDown _price=new(){Width=360,Maximum=999999999,Minimum=1,DecimalPlaces=0,ThousandsSeparator=true};private readonly NumericUpDown _threshold=new(){Width=360,Maximum=100000000,Minimum=0,DecimalPlaces=0,ThousandsSeparator=true};private readonly CheckBox _active=new(){Text="Produit actif",Checked=true,AutoSize=true};private readonly bool _creating;
    public string ReferenceValue=>_reference.Text;public string NameValue=>_name.Text;public long? CategoryValue=>(_category.SelectedItem as CategoryChoice)?.Id;public long PriceValue=>decimal.ToInt64(_price.Value);public int ThresholdValue=>decimal.ToInt32(_threshold.Value);public bool ActiveValue=>_active.Checked;
    private sealed record CategoryChoice(long? Id,string Label);
    public ProductEditDialog(IReadOnlyList<Category> categories,Produit? product)
    {
        _creating=product is null;Text=_creating?"Nouveau produit":"Modifier le produit";StartPosition=FormStartPosition.CenterParent;FormBorderStyle=FormBorderStyle.FixedDialog;MaximizeBox=false;MinimizeBox=false;ClientSize=new Size(470,570);Padding=new Padding(28);BackColor=Color.White;AutoScaleMode=AutoScaleMode.Font;
        var p=new TableLayoutPanel{Dock=DockStyle.Fill,AutoSize=true,ColumnCount=1};Controls.Add(p);Add(p,"Référence *",_reference);Add(p,"Désignation *",_name);
        _category.Items.Add(new CategoryChoice(null,"(Sans catégorie)"));foreach(var c in categories)_category.Items.Add(new CategoryChoice(c.CategoryId,c.Name));_category.DisplayMember=nameof(CategoryChoice.Label);_category.SelectedIndex=0;
        Add(p,"Catégorie",_category);Add(p,"Prix de vente (FCFA XAF) *",_price);Add(p,"Seuil d'alerte (unités)",_threshold);
        if(product is not null){_reference.Text=product.Reference;_name.Text=product.Nom;_price.Value=Math.Clamp(product.PrixVente,1,(long)_price.Maximum);_threshold.Value=product.SeuilAlerte;_active.Checked=product.Actif;for(var i=0;i<_category.Items.Count;i++)if(((CategoryChoice)_category.Items[i]!).Id==product.CategorieId){_category.SelectedIndex=i;break;}}else _active.Visible=false;
        p.Controls.Add(_active);p.Controls.Add(Ui.Label("Le stock n'est pas modifiable dans la fiche produit. Il sera géré par les mouvements.",9,false,Ui.Muted));
        var save=Ui.Button("Enregistrer",true);save.Click+=(_,_)=>{if(string.IsNullOrWhiteSpace(_reference.Text)||string.IsNullOrWhiteSpace(_name.Text)){MessageBox.Show("La référence et la désignation sont obligatoires.");return;}DialogResult=DialogResult.OK;};p.Controls.Add(save);AcceptButton=save;
    }
    private static void Add(TableLayoutPanel p,string label,Control field){p.Controls.Add(Ui.Label(label,9,true));field.Margin=new Padding(0,0,0,8);p.Controls.Add(field);}
}
