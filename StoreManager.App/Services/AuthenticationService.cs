using StoreManager.App.Data;
using StoreManager.App.Models;

namespace StoreManager.App.Services;

internal sealed class AuthenticationService(UserRepository users, PasswordHasher hasher)
{
    private sealed class FailureState { public int Count; public DateTime LockedUntilUtc; }
    private readonly Dictionary<string, FailureState> _failures = new(StringComparer.OrdinalIgnoreCase);
    private readonly object _sync = new();

    public bool NeedsInitialAdministrator => !users.AnyUsers();

    public void CreateInitialAdministrator(string name, string username, string password)
    {
        if (users.AnyUsers()) throw new InvalidOperationException("Le compte initial est déjà créé.");
        ValidateIdentity(name, username, password);
        users.Create(name, username, UserRole.Administrateur, true, password, null, true, hasher);
    }

    public UserSession Login(string username, string password)
    {
        if (string.IsNullOrWhiteSpace(username) || string.IsNullOrEmpty(password)) throw new InvalidOperationException("Saisissez votre identifiant et votre mot de passe.");
        var key=username.Trim();
        lock (_sync)
        {
            if (_failures.TryGetValue(key,out var current) && current.LockedUntilUtc>DateTime.UtcNow)
            { users.LogAuthentication(null,key,false); throw new InvalidOperationException($"Trop de tentatives. Réessayez après {Math.Ceiling((current.LockedUntilUtc-DateTime.UtcNow).TotalSeconds)} secondes."); }
        }
        var result=users.Authenticate(key,password,hasher);
        if(result is null)
        {
            users.LogAuthentication(null,key,false);
            lock(_sync)
            {
                if(!_failures.TryGetValue(key,out var state)) _failures[key]=state=new FailureState();
                state.Count++;
                if(state.Count>=5){var step=Math.Min(state.Count-5,4);state.LockedUntilUtc=DateTime.UtcNow.AddSeconds(30*Math.Pow(2,step));}
            }
            throw new InvalidOperationException("Identifiant ou mot de passe incorrect, ou compte désactivé.");
        }
        lock(_sync)_failures.Remove(key);
        users.LogAuthentication(result.Value.Id,key,true);
        return new UserSession(result.Value.Id,result.Value.Name,result.Value.Username,result.Value.Role);
    }

    public void Logout(UserSession session)
    {
        if (session.Role is not (UserRole.Administrateur or UserRole.Vendeur)) throw new UnauthorizedAccessException("Session invalide.");
        users.LogLogout(session.UserId);
    }

    internal static void ValidateIdentity(string name,string username,string password)
    {
        if(string.IsNullOrWhiteSpace(name)||name.Trim().Length<2||name.Trim().Length>120)throw new ArgumentException("Le nom doit contenir entre 2 et 120 caractères.");
        if(string.IsNullOrWhiteSpace(username)||username.Trim().Length<3||username.Trim().Length>80)throw new ArgumentException("L'identifiant doit contenir entre 3 et 80 caractères.");
        if(password.Length<12)throw new ArgumentException("Le mot de passe doit comporter au moins 12 caractères.");
    }
}
