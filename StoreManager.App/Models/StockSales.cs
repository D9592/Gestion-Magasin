namespace StoreManager.App.Models;

internal enum PaymentMode { Especes, MobileMoney, Carte }

internal sealed record StockMovement(long MovementId,long ProductId,string Reference,string ProductName,string Type,int SignedQuantity,int BalanceAfter,string Reason,string Author,string TimestampUtc);
internal sealed record SaleLineDraft(long ProductId,int Quantity,long ExpectedUnitPrice,string Reference,string Name);
internal sealed record RepricedSaleLine(long ProductId,int Quantity,long CurrentUnitPrice,string CurrentReference,string CurrentName);
internal sealed record SaleSummary(long SaleId,string Number,string DateUtc,string BusinessDate,string Seller,string PaymentMode,long Total,long AmountReceived,long Change,string Status,long SellerId);
internal sealed record SaleLineDetail(long ProductId,string Reference,string Name,int Quantity,long UnitPrice,long Subtotal);
internal sealed record SaleDetail(SaleSummary Summary,string ValidationToken,IReadOnlyList<SaleLineDetail> Lines,string StoreName,string TimeZoneId);
internal sealed record SaleValidationResult(SaleSummary Sale,bool AlreadyCommitted);
internal sealed record SalePage(PageResult<SaleSummary> Page);

internal sealed class SalePriceChangedException(IReadOnlyList<RepricedSaleLine> currentLines,long currentTotal)
    : InvalidOperationException("Un ou plusieurs prix ont changé. Vérifiez le nouveau total et confirmez à nouveau.")
{
    public IReadOnlyList<RepricedSaleLine> CurrentLines { get; }=currentLines;
    public long CurrentTotal { get; }=currentTotal;
}

internal sealed class InsufficientStockException(string reference,string name,int available,int requested)
    : InvalidOperationException($"Stock insuffisant pour {name} ({reference}) : {available} disponible(s), {requested} demandé(s).")
{
    public string Reference { get; }=reference;
    public int Available { get; }=available;
    public int Requested { get; }=requested;
}
