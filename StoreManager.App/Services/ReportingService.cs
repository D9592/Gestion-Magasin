using System.Globalization;
using StoreManager.App.Data;
using StoreManager.App.Models;

namespace StoreManager.App.Services;

internal sealed class ReportingService(ReportingRepository reports,StoreSettingsRepository settings)
{
    private const int PageSize=200;
    public string StoreName()=>settings.StoreName();
    public string CurrentBusinessDate()=>GetBusinessDate(DateTime.UtcNow).ToString("yyyy-MM-dd",CultureInfo.InvariantCulture);

    public DashboardData Dashboard(UserSession actor,DateTime? utcNow=null)
    {
        if(actor.Role is not (UserRole.Administrateur or UserRole.Vendeur))throw new UnauthorizedAccessException("Accès refusé.");
        var utc=utcNow??DateTime.UtcNow;var today=GetBusinessDate(utc);var data=reports.Dashboard(actor,today.ToString("yyyy-MM-dd",CultureInfo.InvariantCulture),today.AddDays(-6).ToString("yyyy-MM-dd",CultureInfo.InvariantCulture));var values=data.SalesTrend.ToDictionary(x=>x.BusinessDate,x=>x.Amount,StringComparer.Ordinal);
        var completeTrend=Enumerable.Range(0,7).Select(offset=>today.AddDays(offset-6).ToString("yyyy-MM-dd",CultureInfo.InvariantCulture)).Select(date=>new SalesTrendPoint(date,values.GetValueOrDefault(date))).ToArray();return data with{SalesTrend=completeTrend};
    }

    public PageResult<ProductSalesReportRow> ProductSales(UserSession actor,string from,string through,string? search,int page=0)
    {RequireAdministrator(actor);ValidateRange(from,through);return reports.ProductSales(from,through,search,page,PageSize);}
    public PageResult<SalesReportRow> Sales(UserSession actor,string from,string through,string? search,long? sellerId,string? status,int page=0)
    {RequireAdministrator(actor);ValidateRange(from,through);ValidateStatus(status);return reports.Sales(from,through,search,sellerId,status,page,PageSize);}
    public PageResult<StockReportRow> Stocks(UserSession actor,string? search,string? status,int page=0)
    {RequireAdministrator(actor);if(status is not null&&status is not ("En rupture" or "Stock faible" or "Disponible" or "Archivé"))throw new ArgumentException("Filtre de stock invalide.");return reports.Stocks(search,status,page,PageSize);}
    public IReadOnlyList<StockAlert> Alerts(UserSession actor)
    {if(actor.Role is not (UserRole.Administrateur or UserRole.Vendeur))throw new UnauthorizedAccessException("Accès refusé.");return reports.Alerts();}

    public IReadOnlyList<SalesReportRow> AllSales(UserSession actor,string from,string through,string? search,long? sellerId,string? status)
    {
        RequireAdministrator(actor);ValidateRange(from,through);ValidateStatus(status);var rows=new List<SalesReportRow>();var page=0;
        while(true){var result=reports.Sales(from,through,search,sellerId,status,page++,PageSize);rows.AddRange(result.Items);if(!result.HasMore)return rows;}
    }
    public IReadOnlyList<StockReportRow> AllStocks(UserSession actor,string? search,string? status)
    {
        RequireAdministrator(actor);if(status is not null&&status is not ("En rupture" or "Stock faible" or "Disponible" or "Archivé"))throw new ArgumentException("Filtre de stock invalide.");var rows=new List<StockReportRow>();var page=0;
        while(true){var result=reports.Stocks(search,status,page++,PageSize);rows.AddRange(result.Items);if(!result.HasMore)return rows;}
    }

    private static void ValidateRange(string from,string through)
    {
        if(!DateOnly.TryParseExact(from,"yyyy-MM-dd",CultureInfo.InvariantCulture,DateTimeStyles.None,out var start)||!DateOnly.TryParseExact(through,"yyyy-MM-dd",CultureInfo.InvariantCulture,DateTimeStyles.None,out var end))throw new ArgumentException("Dates du rapport invalides.");
        if(end<start)throw new ArgumentException("La date de fin doit être égale ou postérieure à la date de début.");
    }
    private DateOnly GetBusinessDate(DateTime utc)
    {
        utc=utc.Kind switch{DateTimeKind.Utc=>utc,DateTimeKind.Local=>utc.ToUniversalTime(),_=>DateTime.SpecifyKind(utc,DateTimeKind.Utc)};return DateOnly.FromDateTime(TimeZoneInfo.ConvertTimeFromUtc(utc,settings.TimeZone()).Date);
    }
    private static void ValidateStatus(string? status){if(status is not null&&status is not ("Validee" or "Annulee"))throw new ArgumentException("Filtre de statut invalide.");}
    private static void RequireAdministrator(UserSession actor){if(actor.Role!=UserRole.Administrateur)throw new UnauthorizedAccessException("Les rapports globaux sont réservés à l'administrateur.");}
}
