namespace StoreManager.App.Models;

internal sealed record DailyStoreStatistics(string BusinessDate,long SalesTotal,int SaleCount,long UnitsSold,int OutOfStockCount,int LowStockCount);
internal sealed record SalesTrendPoint(string BusinessDate,long Amount);
internal sealed record TopProductPoint(string Reference,string Name,long Quantity);
internal sealed record PaymentModePoint(string PaymentMode,long Amount);
internal sealed record StockAlert(long ProductId,string Reference,string Name,int Quantity,int Threshold,string Status);
internal sealed record DashboardData(DailyStoreStatistics Statistics,IReadOnlyList<SalesTrendPoint> SalesTrend,IReadOnlyList<TopProductPoint> TopProducts,IReadOnlyList<PaymentModePoint> PaymentModes,IReadOnlyList<StockAlert> Alerts);
internal sealed record ProductSalesReportRow(long ProductId,string Reference,string Name,long Quantity,long Amount);
internal sealed record SalesReportRow(long SaleId,string Number,string BusinessDate,string DateUtc,string Seller,string PaymentMode,long Amount,string Status);
internal sealed record StockReportRow(long ProductId,string Reference,string Name,int Quantity,int Threshold,string Status);
