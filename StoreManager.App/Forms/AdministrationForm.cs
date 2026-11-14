using StoreManager.App.Controls;
using StoreManager.App.Models;
using StoreManager.App.Services;

namespace StoreManager.App.Forms;

internal sealed class AdministrationForm : Form
{
    private readonly UserSession _actor;private readonly UserManagementService _users;private readonly CatalogManagementService _catalog;private readonly AuditService _audit;private readonly DatabaseBackupService _backups;
    public AdministrationForm(UserSession actor,UserManagementService users,CatalogManagementService catalog,AuditService audit,DatabaseBackupService backups)
    {
        if(actor.Role!=UserRole.Administrateur)throw new UnauthorizedAccessException("L'administration est réservée à l'administrateur.");
        _actor=actor;_users=users;_catalog=catalog;_audit=audit;_backups=backups;Text="Administration — NOVA";StartPosition=FormStartPosition.CenterParent;Size=new Size(760,500);MinimumSize=new Size(640,420);BackColor=Ui.Canvas;Font=new Font("Segoe UI",10);AutoScaleMode=AutoScaleMode.Font;
        var root=new TableLayoutPanel{Dock=DockStyle.Fill,ColumnCount=1,RowCount=3,Padding=new Padding(28)};root.RowStyles.Add(new RowStyle(SizeType.AutoSize));root.RowStyles.Add(new RowStyle(SizeType.AutoSize));root.RowStyles.Add(new RowStyle(SizeType.Percent,100));Controls.Add(root);
        root.Controls.Add(Ui.Label("Administration",20,true),0,0);root.Controls.Add(Ui.Label("Gestion des accès et du catalogue",10,false,Ui.Muted),0,1);
        var actions=new FlowLayoutPanel{Dock=DockStyle.Fill,AutoSize=true,Padding=new Padding(0,24,0,0),WrapContents=true};var usersButton=Ui.Button("♙  Gérer les utilisateurs",true);usersButton.Height=52;usersButton.Click+=(_,_)=>{using var form=new UserManagementForm(_actor,_users);form.ShowDialog(this);};actions.Controls.Add(usersButton);var categoriesButton=Ui.Button("▧  Gérer les catégories");categoriesButton.Height=52;categoriesButton.Click+=(_,_)=>{using var form=new CategoryManagementForm(_actor,_catalog);form.ShowDialog(this);};actions.Controls.Add(categoriesButton);var auditButton=Ui.Button("▤  Journal d’audit");auditButton.Height=52;auditButton.Click+=(_,_)=>{using var form=new AuditLogForm(_actor,_audit);form.ShowDialog(this);};actions.Controls.Add(auditButton);var backupButton=Ui.Button("◉  Sauvegarde et restauration");backupButton.Height=52;backupButton.Click+=(_,_)=>{using var form=new BackupManagementForm(_actor,_backups);if(form.ShowDialog(this)==DialogResult.Retry){DialogResult=DialogResult.Retry;Close();}};actions.Controls.Add(backupButton);root.Controls.Add(actions,0,2);
    }
}
