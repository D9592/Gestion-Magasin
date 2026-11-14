using StoreManager.App.Data;
using StoreManager.App.Models;
using Microsoft.Data.Sqlite;

namespace StoreManager.App.Services;

internal sealed class CatalogManagementService(CatalogRepository catalog)
{
    public IReadOnlyList<Category> Categories(UserSession actor,string? search=null,bool activeOnly=false){return catalog.Categories(search,activeOnly);}
    public void SaveCategory(UserSession actor,long? id,string name,bool active)
    {
        UserManagementService.RequireAdmin(actor); if(string.IsNullOrWhiteSpace(name)||name.Trim().Length>100)throw new ArgumentException("Le nom de catégorie est obligatoire (100 caractères maximum).");
        try{catalog.SaveCategory(id,name,active,actor.UserId);}catch(SqliteException ex) when(ex.SqliteErrorCode==19&&(ex.Message.Contains("Categorie.Nom",StringComparison.Ordinal)||ex.Message.Contains("Categorie.NomNormalise",StringComparison.Ordinal))){throw new InvalidOperationException("Une catégorie porte déjà ce nom.",ex);}
    }
    public IReadOnlyList<Produit> Products(UserSession actor,string? search=null,long? categoryId=null,bool? active=null,string? availability=null){return catalog.Products(search,categoryId,active,availability);}
    public PageResult<Produit> ProductsPage(UserSession actor,string? search,long? categoryId,bool? active,string? availability,int page,int pageSize=100){return catalog.ProductsPage(search,categoryId,active,availability,page,pageSize);}
    public IReadOnlyList<Produit> SellableProducts(UserSession actor,string? search){if(actor.Role is not (UserRole.Administrateur or UserRole.Vendeur))throw new UnauthorizedAccessException("Accès refusé.");return catalog.SellableProducts(search);}
    public void SaveProduct(UserSession actor,long? id,string reference,string name,long? categoryId,long salePrice,int threshold,bool active)
    {
        UserManagementService.RequireAdmin(actor);
        if(string.IsNullOrWhiteSpace(reference)||reference.Trim().Length>60)throw new ArgumentException("La référence est obligatoire (60 caractères maximum).");
        if(string.IsNullOrWhiteSpace(name)||name.Trim().Length>160)throw new ArgumentException("La désignation est obligatoire (160 caractères maximum).");
        if(salePrice<=0)throw new ArgumentException("Le prix de vente doit être strictement positif en FCFA XAF.");
        if(threshold<0)throw new ArgumentException("Le seuil d'alerte doit être positif ou nul.");
        if(categoryId is not null&&!catalog.Categories(null,true).Any(c=>c.CategoryId==categoryId)&&(id is null||catalog.ProductCategoryId(id.Value)!=categoryId))throw new ArgumentException("La catégorie est inactive ou introuvable.");
        try{catalog.SaveProduct(id,reference,name,categoryId,salePrice,threshold,active,actor.UserId);}catch(SqliteException ex) when(ex.SqliteErrorCode==19&&ex.Message.Contains("Produit.Reference",StringComparison.OrdinalIgnoreCase)){throw new InvalidOperationException("Cette référence est déjà utilisée. Les références archivées restent réservées.",ex);}
    }
}
