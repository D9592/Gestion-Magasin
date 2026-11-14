using StoreManager.App.Controls;
using StoreManager.App.Models;
using StoreManager.App.Services;

namespace StoreManager.App.Forms;

internal sealed class BackupManagementForm : Form
{
    private readonly UserSession _actor;private readonly DatabaseBackupService _backups;private readonly Label _status=new(){AutoSize=true,ForeColor=Ui.Muted};
    public BackupManagementForm(UserSession actor,DatabaseBackupService backups)
    {
        if(actor.Role!=UserRole.Administrateur)throw new UnauthorizedAccessException("Sauvegarde et restauration réservées à l’administrateur.");_actor=actor;_backups=backups;
        Text="Sauvegarde et restauration — NOVA";StartPosition=FormStartPosition.CenterParent;Size=new Size(800,500);MinimumSize=new Size(680,400);BackColor=Ui.Canvas;Font=new Font("Segoe UI",10);AutoScaleMode=AutoScaleMode.Font;
        var root=new TableLayoutPanel{Dock=DockStyle.Fill,ColumnCount=1,RowCount=4,Padding=new Padding(28)};root.RowStyles.Add(new RowStyle(SizeType.AutoSize));root.RowStyles.Add(new RowStyle(SizeType.AutoSize));root.RowStyles.Add(new RowStyle(SizeType.AutoSize));root.RowStyles.Add(new RowStyle(SizeType.Percent,100));Controls.Add(root);
        root.Controls.Add(Ui.Label("Protection des données",20,true),0,0);root.Controls.Add(Ui.Label("Les sauvegardes automatiques quotidiennes sont conservées sur les 7 dernières réussies. Vous pouvez aussi choisir un emplacement externe.",10,false,Ui.Muted),0,1);
        var buttons=new FlowLayoutPanel{Dock=DockStyle.Fill,AutoSize=true,Padding=new Padding(0,22,0,18)};var create=Ui.Button("Créer une sauvegarde",true);create.Height=48;create.Click+=CreateBackup;var restore=Ui.Button("Restaurer une sauvegarde");restore.Height=48;restore.Click+=RestoreBackup;buttons.Controls.Add(create);buttons.Controls.Add(restore);root.Controls.Add(buttons,0,2);root.Controls.Add(_status,0,3);
    }
    private void CreateBackup(object? sender,EventArgs e)
    {
        using var dialog=new SaveFileDialog{Title="Enregistrer la sauvegarde",Filter="Base SQLite (*.sqlite)|*.sqlite",DefaultExt="sqlite",AddExtension=true,FileName=$"nova_{DateTime.Now:yyyyMMdd_HHmmss}.sqlite",OverwritePrompt=true};if(dialog.ShowDialog(this)!=DialogResult.OK)return;
        try{var path=_backups.CreateManual(_actor,dialog.FileName);_status.Text=$"Sauvegarde créée et vérifiée : {path}";MessageBox.Show(this,"La sauvegarde a été créée et son intégrité vérifiée.","Sauvegarde terminée",MessageBoxButtons.OK,MessageBoxIcon.Information);}catch(Exception ex){_status.Text="Échec de la sauvegarde.";MessageBox.Show(this,$"La sauvegarde n’a pas abouti.\n{ex.Message}","Échec",MessageBoxButtons.OK,MessageBoxIcon.Error);}
    }
    private void RestoreBackup(object? sender,EventArgs e)
    {
        using var dialog=new OpenFileDialog{Title="Sélectionner une sauvegarde",Filter="Base SQLite (*.sqlite;*.db)|*.sqlite;*.db|Tous les fichiers (*.*)|*.*",CheckFileExists=true,Multiselect=false};if(dialog.ShowDialog(this)!=DialogResult.OK)return;
        var timestamp=File.GetLastWriteTime(dialog.FileName).ToString("dd/MM/yyyy HH:mm:ss");var confirm=MessageBox.Show(this,$"Restaurer la sauvegarde du {timestamp} ?\n\nUne sauvegarde de sécurité sera créée avant le remplacement. L’application reviendra ensuite à l’écran de connexion.","Confirmer la restauration",MessageBoxButtons.YesNo,MessageBoxIcon.Warning,MessageBoxDefaultButton.Button2);if(confirm!=DialogResult.Yes)return;
        try{var safety=_backups.Restore(_actor,dialog.FileName);MessageBox.Show(this,$"Restauration vérifiée. Sauvegarde de sécurité créée :\n{safety}","Restauration terminée",MessageBoxButtons.OK,MessageBoxIcon.Information);DialogResult=DialogResult.Retry;Close();}catch(Exception){_status.Text="Restauration annulée ; la base active a été conservée.";MessageBox.Show(this,"La restauration n’a pas abouti. La base active a été conservée. Vérifiez que le fichier est valide et que l’application dispose des droits d’accès au dossier de données.","Échec de la restauration",MessageBoxButtons.OK,MessageBoxIcon.Error);}
    }
}
