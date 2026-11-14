using System.Globalization;
using System.Text;
using StoreManager.App.Models;

namespace StoreManager.App.Helpers;

internal static class CsvReportExporter
{
    public static void WriteSales(Stream destination,IEnumerable<SalesReportRow> rows)
    {
        using var writer=new StreamWriter(destination,new UTF8Encoding(encoderShouldEmitUTF8Identifier:true),1024,leaveOpen:true);writer.NewLine="\r\n";
        WriteRow(writer,"Numéro","Date magasin","Date/heure UTC","Vendeur","Mode de paiement","Montant XAF","Statut");
        foreach(var row in rows)WriteRow(writer,row.Number,row.BusinessDate,row.DateUtc,row.Seller,row.PaymentMode,Number(row.Amount),row.Status);
        writer.Flush();
    }

    public static void WriteStocks(Stream destination,IEnumerable<StockReportRow> rows)
    {
        using var writer=new StreamWriter(destination,new UTF8Encoding(encoderShouldEmitUTF8Identifier:true),1024,leaveOpen:true);writer.NewLine="\r\n";
        WriteRow(writer,"Référence","Produit","Quantité disponible","Seuil d'alerte","Statut");
        foreach(var row in rows)WriteRow(writer,row.Reference,row.Name,Number(row.Quantity),Number(row.Threshold),row.Status);
        writer.Flush();
    }

    internal static string ProtectFormula(string value)
    {
        var index=0;while(index<value.Length&&char.IsWhiteSpace(value[index]))index++;
        return index<value.Length&&value[index] is '=' or '+' or '-' or '@'?'\''+value:value;
    }

    private static string Number(long value)=>value.ToString(CultureInfo.InvariantCulture);
    private static void WriteRow(TextWriter writer,params string[] cells)=>writer.WriteLine(string.Join(';',cells.Select(FormatCell)));
    private static string FormatCell(string value)=>'"'+ProtectFormula(value).Replace("\"","\"\"",StringComparison.Ordinal)+'"';
}
