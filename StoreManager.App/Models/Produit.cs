namespace StoreManager.App.Models;

internal sealed record Produit(long ProduitId, string Reference, string Nom, long? CategorieId, string? CategorieNom, long PrixVente, int SeuilAlerte, bool Actif, int QuantiteStock, string StatutStock);

internal sealed record PageResult<T>(IReadOnlyList<T> Items,bool HasMore);
