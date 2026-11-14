namespace StoreManager.App.Validators;

internal static class ProduitValidator
{
    public static IReadOnlyList<string> Valider(string nom, string reference, long prixVente, int seuil)
    {
        var erreurs = new List<string>();
        if (string.IsNullOrWhiteSpace(nom)) erreurs.Add("Le nom du produit est obligatoire.");
        if (string.IsNullOrWhiteSpace(reference)) erreurs.Add("La référence est obligatoire.");
        if (prixVente <= 0) erreurs.Add("Le prix de vente doit être strictement positif.");
        if (seuil < 0) erreurs.Add("Le seuil d’alerte doit être positif ou nul.");
        return erreurs;
    }
}
