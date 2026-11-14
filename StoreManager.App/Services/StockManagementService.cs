using StoreManager.App.Data;
using StoreManager.App.Models;

namespace StoreManager.App.Services;

internal sealed class StockManagementService(StockRepository stock,CatalogManagementService catalog)
{
    public PageResult<Produit> Search(UserSession actor,string? query,long? categoryId,string? availability,int page,int pageSize=100)
    {
        var active=availability=="Archivé"?false:true;var status=availability is "En rupture" or "Stock faible" or "Disponible"?availability:null;
        return catalog.ProductsPage(actor,query,categoryId,active,status,page,pageSize);
    }
    public IReadOnlyList<StockMovement> Movements(UserSession actor,long productId){_ = actor;return stock.Movements(productId);}
    public long CurrentBalance(long productId)=>stock.Balance(productId);
    public bool HasInitial(long productId)=>stock.HasInitial(productId);
    public void Initialize(UserSession actor,long productId,int quantity)
    {UserManagementService.RequireAdmin(actor);stock.Initialize(productId,quantity,actor.UserId);}
    public void Replenish(UserSession actor,long productId,int quantity,string reason)
    {UserManagementService.RequireAdmin(actor);stock.Replenish(productId,quantity,reason,actor.UserId);}
    public long Correct(UserSession actor,long productId,int countedQuantity,string reason)
    {UserManagementService.RequireAdmin(actor);return stock.Correct(productId,countedQuantity,reason,actor.UserId);}
}
