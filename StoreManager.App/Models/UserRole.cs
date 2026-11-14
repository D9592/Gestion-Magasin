namespace StoreManager.App.Models;

internal enum UserRole { Administrateur, Vendeur }

internal sealed record UserSession(long UserId, string Name, string Username, UserRole Role);

internal sealed record UserSummary(long UserId, string Name, string Username, UserRole Role, bool Active);
