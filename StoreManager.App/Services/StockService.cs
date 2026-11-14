namespace StoreManager.App.Services;

/// <summary>Contrat de lecture métier du stock; l'implémentation repository sera ajoutée avec les workflows.</summary>
internal interface IStockService
{
    Task<IReadOnlyList<Models.Produit>> RechercherAsync(string? recherche, CancellationToken cancellationToken = default);
}
