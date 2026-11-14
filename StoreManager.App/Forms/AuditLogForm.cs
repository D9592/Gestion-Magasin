using StoreManager.App.Controls;
using StoreManager.App.Services;

namespace StoreManager.App.Forms;

internal sealed class AuditLogForm : Form
{
    private readonly AuditService _audit; private readonly StoreManager.App.Models.UserSession _actor; private readonly TextBox _search=new(){Width=260,PlaceholderText="Rechercher dans les événements"}; private readonly DataGridView _grid=new();
    public AuditLogForm(StoreManager.App.Models.UserSession actor,AuditService audit)
    {
        if(actor.Role!=StoreManager.App.Models.UserRole.Administrateur)throw new UnauthorizedAccessException("Consultation réservée à l’administrateur.");_actor=actor;_audit=audit;
        Text="Journal d’audit — NOVA";StartPosition=FormStartPosition.CenterParent;Size=new Size(1160,650);MinimumSize=new Size(900,500);BackColor=Ui.Canvas;Font=new Font("Segoe UI",9);AutoScaleMode=AutoScaleMode.Font;
        var root=new TableLayoutPanel{Dock=DockStyle.Fill,ColumnCount=1,RowCount=3,Padding=new Padding(22)};root.RowStyles.Add(new RowStyle(SizeType.AutoSize));root.RowStyles.Add(new RowStyle(SizeType.AutoSize));root.RowStyles.Add(new RowStyle(SizeType.Percent,100));Controls.Add(root);
        root.Controls.Add(Ui.Label("Journal d’audit",20,true),0,0);var bar=new FlowLayoutPanel{Dock=DockStyle.Fill,AutoSize=true,Padding=new Padding(0,14,0,12)};var refresh=Ui.Button("Actualiser",true);refresh.Click+=(_,_)=>LoadRows();_search.TextChanged+=(_,_)=>LoadRows();bar.Controls.Add(_search);bar.Controls.Add(refresh);bar.Controls.Add(Ui.Label("Lecture seule · 500 événements récents maximum",9,false,Ui.Muted));root.Controls.Add(bar,0,1);
        _grid.Dock=DockStyle.Fill;_grid.ReadOnly=true;_grid.AllowUserToAddRows=false;_grid.AllowUserToDeleteRows=false;_grid.AutoGenerateColumns=false;_grid.BackgroundColor=Color.White;_grid.BorderStyle=BorderStyle.None;_grid.RowHeadersVisible=false;_grid.SelectionMode=DataGridViewSelectionMode.FullRowSelect;_grid.AutoSizeRowsMode=DataGridViewAutoSizeRowsMode.AllCells;_grid.DefaultCellStyle.WrapMode=DataGridViewTriState.True;_grid.ColumnHeadersDefaultCellStyle.BackColor=Color.FromArgb(241,245,249);_grid.EnableHeadersVisualStyles=false;
        AddColumn("Date UTC","TimestampUtc",155);AddColumn("Utilisateur","Author",140);AddColumn("Opération","Action",155);AddColumn("Élément","Entity",105);AddColumn("Identifiant","EntityId",100);AddColumn("Détails","Details",450);root.Controls.Add(_grid,0,2);LoadRows();
    }
    private void AddColumn(string title,string property,int width)=>_grid.Columns.Add(new DataGridViewTextBoxColumn{HeaderText=title,DataPropertyName=property,Width=width,AutoSizeMode=property=="Details"?DataGridViewAutoSizeColumnMode.Fill:DataGridViewAutoSizeColumnMode.None});
    private void LoadRows(){try{_grid.DataSource=_audit.List(_actor,_search.Text).Select(x=>new{TimestampUtc=x.TimestampUtc.ToLocalTime().ToString("dd/MM/yyyy HH:mm:ss"),x.Author,x.Action,x.Entity,x.EntityId,x.Details}).ToList();}catch(Exception){MessageBox.Show(this,"Impossible de charger le journal d’audit.","Erreur",MessageBoxButtons.OK,MessageBoxIcon.Error);}}
}
