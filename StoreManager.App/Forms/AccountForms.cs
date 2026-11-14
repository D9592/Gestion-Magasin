using StoreManager.App.Controls;
using StoreManager.App.Models;
using StoreManager.App.Services;

namespace StoreManager.App.Forms;

internal sealed class InitialAdministratorForm : Form
{
    private readonly AuthenticationService _authentication;
    private readonly TextBox _name=Field("Nom complet");
    private readonly TextBox _username=Field("Identifiant");
    private readonly TextBox _password=Field("Mot de passe");
    private readonly TextBox _confirm=Field("Confirmer le mot de passe");
    private readonly Label _error=new(){AutoSize=true,ForeColor=Ui.Red};

    public InitialAdministratorForm(AuthenticationService authentication)
    {
        _authentication=authentication;
        Text="Configuration initiale — NOVA";StartPosition=FormStartPosition.CenterScreen;FormBorderStyle=FormBorderStyle.FixedDialog;MaximizeBox=false;MinimizeBox=false;ClientSize=new Size(520,590);BackColor=Ui.Canvas;Font=new Font("Segoe UI",10);AutoScaleMode=AutoScaleMode.Font;
        var card=new Panel{BackColor=Color.White,Location=new Point(38,28),Size=new Size(444,530),Padding=new Padding(32)};Controls.Add(card);
        var title=new Label{Text="Bienvenue dans NOVA",Font=new Font("Segoe UI",20,FontStyle.Bold),ForeColor=Ui.Ink,AutoSize=true,Location=new Point(32,26)};card.Controls.Add(title);
        var info=Ui.Label("Créez le premier compte administrateur pour sécuriser ce magasin.",10,false,Ui.Muted);info.Location=new Point(34,70);card.Controls.Add(info);
        AddField(card,"Nom complet",_name,120);AddField(card,"Identifiant",_username,202);AddField(card,"Mot de passe (12 caractères minimum)",_password,284);AddField(card,"Confirmer le mot de passe",_confirm,366);
        _password.UseSystemPasswordChar=true;_confirm.UseSystemPasswordChar=true;_error.Location=new Point(34,426);card.Controls.Add(_error);
        var create=Ui.Button("Créer l'administrateur",true);create.Location=new Point(34,462);create.Width=360;create.Height=42;create.Click+=Create;card.Controls.Add(create);AcceptButton=create;
    }
    private static TextBox Field(string placeholder)=>new(){Width=360,Height=34,Font=new Font("Segoe UI",10),PlaceholderText=placeholder};
    private static void AddField(Control parent,string label,TextBox field,int y){var l=Ui.Label(label,9,true);l.Location=new Point(34,y);parent.Controls.Add(l);field.Location=new Point(34,y+23);parent.Controls.Add(field);}
    private void Create(object? sender,EventArgs e){try{if(_password.Text!=_confirm.Text)throw new ArgumentException("Les deux mots de passe ne correspondent pas.");_authentication.CreateInitialAdministrator(_name.Text,_username.Text,_password.Text);DialogResult=DialogResult.OK;Close();}catch(Exception ex){_error.Text=ex.Message;}}
}

internal sealed class LoginForm : Form
{
    private readonly AuthenticationService _authentication;
    private readonly TextBox _username=new(){Width=330,Font=new Font("Segoe UI",11),PlaceholderText="Votre identifiant"};
    private readonly TextBox _password=new(){Width=330,Font=new Font("Segoe UI",11),PlaceholderText="Votre mot de passe",UseSystemPasswordChar=true};
    private readonly Label _error=new(){AutoSize=true,ForeColor=Ui.Red};
    public UserSession? Session{get;private set;}
    public LoginForm(AuthenticationService authentication)
    {
        _authentication=authentication;
        Text="Connexion — NOVA";StartPosition=FormStartPosition.CenterScreen;FormBorderStyle=FormBorderStyle.FixedDialog;MaximizeBox=false;MinimizeBox=false;ClientSize=new Size(860,570);BackColor=Ui.Canvas;Font=new Font("Segoe UI",10);AutoScaleMode=AutoScaleMode.Font;
        var brand=new Panel{Dock=DockStyle.Left,Width=330,BackColor=Color.FromArgb(17,31,58)};Controls.Add(brand);
        brand.Controls.Add(new Label{Text="◈  NOVA",Font=new Font("Segoe UI",26,FontStyle.Bold),ForeColor=Color.White,Location=new Point(54,170),AutoSize=true});
        brand.Controls.Add(new Label{Text="GESTION DE MAGASIN\n\nVentes et suivi des stocks",Font=new Font("Segoe UI",11),ForeColor=Color.FromArgb(198,210,228),Location=new Point(58,224),Size=new Size(220,90)});
        var panel=new Panel{Location=new Point(385,95),Size=new Size(410,380),BackColor=Color.White,Padding=new Padding(34)};Controls.Add(panel);
        panel.Controls.Add(new Label{Text="Connexion",Font=new Font("Segoe UI",20,FontStyle.Bold),ForeColor=Ui.Ink,AutoSize=true,Location=new Point(34,28)});
        AddField(panel,"Identifiant",_username,88);AddField(panel,"Mot de passe",_password,166);
        _error.Location=new Point(34,238);panel.Controls.Add(_error);
        var sign=Ui.Button("Se connecter",true);sign.Location=new Point(34,278);sign.Width=330;sign.Height=44;sign.Click+=SignIn;panel.Controls.Add(sign);AcceptButton=sign;
        var footer=Ui.Label("Application locale · Les accès sont journalisés",9,false,Ui.Muted);footer.Location=new Point(390,492);Controls.Add(footer);
    }
    private static void AddField(Control parent,string label,TextBox field,int y){var l=Ui.Label(label,9,true);l.Location=new Point(34,y);parent.Controls.Add(l);field.Location=new Point(34,y+24);parent.Controls.Add(field);}
    private void SignIn(object? sender,EventArgs e){try{Session=_authentication.Login(_username.Text,_password.Text);DialogResult=DialogResult.OK;Close();}catch(Exception ex){_error.Text=ex.Message;_password.Clear();_password.Focus();}}
}
