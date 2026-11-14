using StoreManager.App.Data;
using StoreManager.App.Models;

namespace StoreManager.App.Services;

internal sealed class SalesService(SalesRepository sales,CatalogManagementService catalog)
{
    public IReadOnlyList<Produit> SearchProducts(UserSession actor,string? search)
    {RequireSeller(actor);return catalog.SellableProducts(actor,search);}
    public SaleValidationResult Validate(UserSession actor,IReadOnlyList<SaleLineDraft> lines,PaymentMode mode,long amountReceived,string token)
    {RequireSeller(actor);return sales.ValidateAndCommit(actor,lines,mode,amountReceived,token);}
    public PageResult<SaleSummary> History(UserSession actor,string? search,string? from,string? through,long? sellerId,string? status,int page,int pageSize=100)
    {RequireSeller(actor);if(status is not null&&status is not ("Validee" or "Annulee"))throw new ArgumentException("Filtre de statut invalide.");return sales.History(actor,search,from,through,sellerId,status,page,pageSize);}
    public SaleDetail Detail(UserSession actor,long saleId)
    {RequireSeller(actor);var detail=sales.Detail(saleId);if(actor.Role==UserRole.Vendeur&&detail.Summary.SellerId!=actor.UserId)throw new UnauthorizedAccessException("Un vendeur ne peut consulter que ses propres ventes.");return detail;}
    public void Cancel(UserSession actor,long saleId,string reason)
    {UserManagementService.RequireAdmin(actor);sales.Cancel(saleId,reason,actor.UserId);}
    private static void RequireSeller(UserSession actor){if(actor.Role is not (UserRole.Administrateur or UserRole.Vendeur))throw new UnauthorizedAccessException("Accès refusé.");}
}
