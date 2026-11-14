using Microsoft.Data.Sqlite;
using StoreManager.App.Models;

namespace StoreManager.App.Data;

internal sealed class ReportingRepository(SqliteConnectionFactory database)
{
    public DashboardData Dashboard(UserSession actor,string businessDate,string trendStartDate)
    {
        using var c=database.Create();using var t=c.BeginTransaction(deferred:true);
        var role=actor.Role.ToString();
        var statistics=ReadStatistics(c,t,actor,businessDate);
        var trend=new List<SalesTrendPoint>();
        using(var q=c.CreateCommand())
        {
            q.Transaction=t;q.CommandText="SELECT v.DateMetier,COALESCE(SUM(v.Total),0) FROM Vente v WHERE v.Statut='Validee' AND v.DateMetier>=$start AND v.DateMetier<=$end AND ($role='Administrateur' OR v.VendeurId=$actor) GROUP BY v.DateMetier ORDER BY v.DateMetier;";
            q.Parameters.AddWithValue("$start",trendStartDate);q.Parameters.AddWithValue("$end",businessDate);q.Parameters.AddWithValue("$role",role);q.Parameters.AddWithValue("$actor",actor.UserId);using var r=q.ExecuteReader();while(r.Read())trend.Add(new(r.GetString(0),r.GetInt64(1)));
        }
        var topProducts=new List<TopProductPoint>();
        using(var q=c.CreateCommand())
        {
            q.Transaction=t;q.CommandText="SELECT p.Reference,p.Nom,COALESCE(SUM(l.Quantite),0) AS Units FROM Vente v JOIN LigneVente l ON l.VenteId=v.VenteId JOIN Produit p ON p.ProduitId=l.ProduitId WHERE v.Statut='Validee' AND v.DateMetier>=$start AND v.DateMetier<=$end AND ($role='Administrateur' OR v.VendeurId=$actor) GROUP BY p.ProduitId,p.Reference,p.Nom ORDER BY Units DESC,p.Nom COLLATE NOCASE LIMIT 6;";
            q.Parameters.AddWithValue("$start",trendStartDate);q.Parameters.AddWithValue("$end",businessDate);q.Parameters.AddWithValue("$role",role);q.Parameters.AddWithValue("$actor",actor.UserId);using var r=q.ExecuteReader();while(r.Read())topProducts.Add(new(r.GetString(0),r.GetString(1),r.GetInt64(2)));
        }
        var payments=new List<PaymentModePoint>();
        using(var q=c.CreateCommand())
        {
            q.Transaction=t;q.CommandText="SELECT v.ModePaiement,COALESCE(SUM(v.Total),0) FROM Vente v WHERE v.Statut='Validee' AND v.DateMetier=$date AND ($role='Administrateur' OR v.VendeurId=$actor) GROUP BY v.ModePaiement ORDER BY v.ModePaiement;";
            q.Parameters.AddWithValue("$date",businessDate);q.Parameters.AddWithValue("$role",role);q.Parameters.AddWithValue("$actor",actor.UserId);using var r=q.ExecuteReader();while(r.Read())payments.Add(new(r.GetString(0),r.GetInt64(1)));
        }
        var alerts=ReadAlerts(c,t);
        t.Commit();return new DashboardData(statistics,trend,topProducts,payments,alerts);
    }

    public PageResult<ProductSalesReportRow> ProductSales(string from,string through,string? search,int page,int pageSize)
    {
        ValidatePage(page,pageSize);using var c=database.Create();using var q=c.CreateCommand();
        q.CommandText="SELECT p.ProduitId,p.Reference,p.Nom,SUM(l.Quantite),SUM(l.SousTotal) FROM Vente v JOIN LigneVente l ON l.VenteId=v.VenteId JOIN Produit p ON p.ProduitId=l.ProduitId WHERE v.Statut='Validee' AND v.DateMetier>=$from AND v.DateMetier<=$through AND ($search='' OR p.Reference LIKE $like OR p.Nom LIKE $like OR l.ReferenceSnapshot LIKE $like OR l.NomSnapshot LIKE $like) GROUP BY p.ProduitId,p.Reference,p.Nom ORDER BY p.Nom COLLATE NOCASE,p.Reference LIMIT $limit OFFSET $offset;";
        q.Parameters.AddWithValue("$from",from);q.Parameters.AddWithValue("$through",through);q.Parameters.AddWithValue("$search",search?.Trim()??"");q.Parameters.AddWithValue("$like",$"%{search?.Trim()}%");AddPaging(q,page,pageSize);
        using var r=q.ExecuteReader();var rows=new List<ProductSalesReportRow>();while(r.Read())rows.Add(new(r.GetInt64(0),r.GetString(1),r.GetString(2),r.GetInt64(3),r.GetInt64(4)));return BuildPage(rows,pageSize);
    }

    public PageResult<SalesReportRow> Sales(string from,string through,string? search,long? sellerId,string? status,int page,int pageSize)
    {
        ValidatePage(page,pageSize);using var c=database.Create();using var q=c.CreateCommand();
        q.CommandText="SELECT v.VenteId,v.Numero,v.DateMetier,v.DateHeureUtc,u.Nom,v.ModePaiement,v.Total,v.Statut FROM Vente v JOIN Utilisateur u ON u.UtilisateurId=v.VendeurId WHERE v.DateMetier>=$from AND v.DateMetier<=$through AND ($search='' OR v.Numero LIKE $like) AND ($seller IS NULL OR v.VendeurId=$seller) AND ($status IS NULL OR v.Statut=$status) ORDER BY v.DateHeureUtc DESC,v.VenteId DESC LIMIT $limit OFFSET $offset;";
        q.Parameters.AddWithValue("$from",from);q.Parameters.AddWithValue("$through",through);q.Parameters.AddWithValue("$search",search?.Trim()??"");q.Parameters.AddWithValue("$like",$"%{search?.Trim()}%");q.Parameters.AddWithValue("$seller",sellerId is null?DBNull.Value:sellerId.Value);q.Parameters.AddWithValue("$status",status is null?DBNull.Value:status);AddPaging(q,page,pageSize);
        using var r=q.ExecuteReader();var rows=new List<SalesReportRow>();while(r.Read())rows.Add(new(r.GetInt64(0),r.GetString(1),r.GetString(2),r.GetString(3),r.GetString(4),r.GetString(5),r.GetInt64(6),r.GetString(7)));return BuildPage(rows,pageSize);
    }

    public PageResult<StockReportRow> Stocks(string? search,string? status,int page,int pageSize)
    {
        ValidatePage(page,pageSize);using var c=database.Create();using var q=c.CreateCommand();
        q.CommandText="SELECT ProduitId,Reference,Nom,Quantite,SeuilAlerte,Statut FROM (SELECT p.ProduitId,p.Reference,p.Nom,CAST(COALESCE(s.QuantiteStock,0) AS INTEGER) AS Quantite,p.SeuilAlerte,CASE WHEN p.Actif=0 THEN 'Archivé' WHEN COALESCE(s.QuantiteStock,0)=0 THEN 'En rupture' WHEN COALESCE(s.QuantiteStock,0)<=p.SeuilAlerte THEN 'Stock faible' ELSE 'Disponible' END AS Statut FROM Produit p LEFT JOIN VueStockCourant s ON s.ProduitId=p.ProduitId) AS stock WHERE ($search='' OR Reference LIKE $like OR Nom LIKE $like) AND ($status IS NULL OR Statut=$status) ORDER BY Nom COLLATE NOCASE,Reference LIMIT $limit OFFSET $offset;";
        q.Parameters.AddWithValue("$search",search?.Trim()??"");q.Parameters.AddWithValue("$like",$"%{search?.Trim()}%");q.Parameters.AddWithValue("$status",status is null?DBNull.Value:status);AddPaging(q,page,pageSize);
        using var r=q.ExecuteReader();var rows=new List<StockReportRow>();while(r.Read())rows.Add(new(r.GetInt64(0),r.GetString(1),r.GetString(2),r.GetInt32(3),r.GetInt32(4),r.GetString(5)));return BuildPage(rows,pageSize);
    }

    public IReadOnlyList<StockAlert> Alerts()
    {
        using var c=database.Create();using var q=c.CreateCommand();q.CommandText="SELECT p.ProduitId,p.Reference,p.Nom,CAST(COALESCE(s.QuantiteStock,0) AS INTEGER),p.SeuilAlerte,CASE WHEN COALESCE(s.QuantiteStock,0)=0 THEN 'En rupture' ELSE 'Stock faible' END FROM Produit p LEFT JOIN VueStockCourant s ON s.ProduitId=p.ProduitId WHERE p.Actif=1 AND COALESCE(s.QuantiteStock,0)<=p.SeuilAlerte ORDER BY CASE WHEN COALESCE(s.QuantiteStock,0)=0 THEN 0 ELSE 1 END,p.Nom COLLATE NOCASE;";
        using var r=q.ExecuteReader();var rows=new List<StockAlert>();while(r.Read())rows.Add(new(r.GetInt64(0),r.GetString(1),r.GetString(2),r.GetInt32(3),r.GetInt32(4),r.GetString(5)));return rows;
    }

    private static DailyStoreStatistics ReadStatistics(SqliteConnection c,SqliteTransaction t,UserSession actor,string date)
    {
        using var q=c.CreateCommand();q.Transaction=t;
        q.CommandText="SELECT COALESCE((SELECT SUM(v.Total) FROM Vente v WHERE v.DateMetier=$date AND v.Statut='Validee' AND ($role='Administrateur' OR v.VendeurId=$actor)),0),COALESCE((SELECT COUNT(*) FROM Vente v WHERE v.DateMetier=$date AND v.Statut='Validee' AND ($role='Administrateur' OR v.VendeurId=$actor)),0),COALESCE((SELECT SUM(l.Quantite) FROM Vente v JOIN LigneVente l ON l.VenteId=v.VenteId WHERE v.DateMetier=$date AND v.Statut='Validee' AND ($role='Administrateur' OR v.VendeurId=$actor)),0),COALESCE((SELECT SUM(CASE WHEN COALESCE(s.QuantiteStock,0)=0 THEN 1 ELSE 0 END) FROM Produit p LEFT JOIN VueStockCourant s ON s.ProduitId=p.ProduitId WHERE p.Actif=1),0),COALESCE((SELECT SUM(CASE WHEN COALESCE(s.QuantiteStock,0)>0 AND COALESCE(s.QuantiteStock,0)<=p.SeuilAlerte THEN 1 ELSE 0 END) FROM Produit p LEFT JOIN VueStockCourant s ON s.ProduitId=p.ProduitId WHERE p.Actif=1),0);";
        q.Parameters.AddWithValue("$date",date);q.Parameters.AddWithValue("$role",actor.Role.ToString());q.Parameters.AddWithValue("$actor",actor.UserId);using var r=q.ExecuteReader();if(!r.Read())throw new InvalidOperationException("Les statistiques du tableau de bord sont indisponibles.");return new(date,r.GetInt64(0),r.GetInt32(1),r.GetInt64(2),r.GetInt32(3),r.GetInt32(4));
    }

    private IReadOnlyList<StockAlert> ReadAlerts(SqliteConnection c,SqliteTransaction t)
    {
        using var q=c.CreateCommand();q.Transaction=t;q.CommandText="SELECT p.ProduitId,p.Reference,p.Nom,CAST(COALESCE(s.QuantiteStock,0) AS INTEGER),p.SeuilAlerte,CASE WHEN COALESCE(s.QuantiteStock,0)=0 THEN 'En rupture' ELSE 'Stock faible' END FROM Produit p LEFT JOIN VueStockCourant s ON s.ProduitId=p.ProduitId WHERE p.Actif=1 AND COALESCE(s.QuantiteStock,0)<=p.SeuilAlerte ORDER BY CASE WHEN COALESCE(s.QuantiteStock,0)=0 THEN 0 ELSE 1 END,p.Nom COLLATE NOCASE LIMIT 100;";
        using var r=q.ExecuteReader();var rows=new List<StockAlert>();while(r.Read())rows.Add(new(r.GetInt64(0),r.GetString(1),r.GetString(2),r.GetInt32(3),r.GetInt32(4),r.GetString(5)));return rows;
    }

    private static void AddPaging(SqliteCommand q,int page,int pageSize){q.Parameters.AddWithValue("$limit",pageSize+1);q.Parameters.AddWithValue("$offset",checked(page*pageSize));}
    private static PageResult<T> BuildPage<T>(List<T> rows,int pageSize){var more=rows.Count>pageSize;if(more)rows.RemoveAt(rows.Count-1);return new PageResult<T>(rows,more);}
    private static void ValidatePage(int page,int pageSize){if(page<0||pageSize is <1 or >500)throw new ArgumentOutOfRangeException(nameof(page));}
}
