using StoreManager.App.Data;
using StoreManager.App.Models;
using Microsoft.Data.Sqlite;

namespace StoreManager.App.Services;

internal sealed class UserManagementService(UserRepository users,PasswordHasher hasher)
{
    public IReadOnlyList<UserSummary> List(UserSession actor,string? search){RequireAdmin(actor);return users.List(search);}
    public void Create(UserSession actor,string name,string username,UserRole role,string password)
    {
        RequireAdmin(actor);AuthenticationService.ValidateIdentity(name,username,password);
        try{users.Create(name,username,role,true,password,actor.UserId,false,hasher);}catch(SqliteException ex) when(ex.SqliteErrorCode==19&&ex.Message.Contains("Utilisateur.Identifiant",StringComparison.OrdinalIgnoreCase)){throw new InvalidOperationException("Cet identifiant est déjà utilisé.",ex);}
    }
    public void Update(UserSession actor,long id,string name,string username,UserRole role,bool active)
    {
        RequireAdmin(actor);
        if(actor.UserId==id&&(!active||role!=UserRole.Administrateur))throw new InvalidOperationException("Vous ne pouvez pas désactiver ou rétrograder votre propre compte pendant votre session.");
        if(role!=UserRole.Administrateur||!active)
            if(users.CountActiveAdministrators(id)==0&&users.List(null).Any(u=>u.UserId==id&&u.Role==UserRole.Administrateur&&u.Active))
                throw new InvalidOperationException("Impossible de désactiver ou de rétrograder le dernier administrateur actif.");
        if(string.IsNullOrWhiteSpace(name)||name.Trim().Length<2||string.IsNullOrWhiteSpace(username)||username.Trim().Length<3)throw new ArgumentException("Nom ou identifiant invalide.");
        try{users.Update(id,name,username,role,active,actor.UserId);}catch(SqliteException ex) when(ex.SqliteErrorCode==19&&ex.Message.Contains("Utilisateur.Identifiant",StringComparison.OrdinalIgnoreCase)){throw new InvalidOperationException("Cet identifiant est déjà utilisé.",ex);}
    }
    public void ResetPassword(UserSession actor,long id,string password){RequireAdmin(actor);if(password.Length<12)throw new ArgumentException("Le mot de passe doit comporter au moins 12 caractères.");users.ResetPassword(id,password,actor.UserId,hasher);}
    internal static void RequireAdmin(UserSession actor){if(actor.Role!=UserRole.Administrateur)throw new UnauthorizedAccessException("Cette action est réservée à l'administrateur.");}
}
