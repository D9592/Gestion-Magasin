using Microsoft.Data.Sqlite;
using StoreManager.App.Models;

namespace StoreManager.App.Data;

internal sealed class StockRepository(SqliteConnectionFactory database)
{
    public long Balance(long productId)
    {
        using var c=database.Create();using var q=c.CreateCommand();q.CommandText="SELECT COALESCE(SUM(QuantiteSignee),0) FROM MouvementStock WHERE ProduitId=$id;";q.Parameters.AddWithValue("$id",productId);return Convert.ToInt64(q.ExecuteScalar());
    }

    public bool HasInitial(long productId)
    {
        using var c=database.Create();using var q=c.CreateCommand();q.CommandText="SELECT EXISTS(SELECT 1 FROM JournalAudit WHERE Action='StockInitialise' AND Entite='Produit' AND EntiteId=$id);";q.Parameters.AddWithValue("$id",productId.ToString());return Convert.ToInt64(q.ExecuteScalar())!=0;
    }

    public void Initialize(long productId,int quantity,long actorId)
    {
        if(quantity<0)throw new ArgumentOutOfRangeException(nameof(quantity),"La quantité initiale ne peut pas être négative.");
        using var c=database.Create();using var t=c.BeginTransaction(deferred:false);
        var product=ReadProduct(c,t,productId);if(!product.Active)throw new InvalidOperationException("Réactivez le produit avant de définir son stock initial.");
        if(HasInitial(c,t,productId))throw new InvalidOperationException("Le stock initial de ce produit a déjà été enregistré.");
        var balance=ReadBalance(c,t,productId);var hasMoves=Exists(c,t,"SELECT EXISTS(SELECT 1 FROM MouvementStock WHERE ProduitId=$id);",productId);
        if(balance!=0||hasMoves)throw new InvalidOperationException("Le stock initial doit être enregistré avant les autres mouvements.");
        var now=DateTime.UtcNow.ToString("O");
        if(quantity>0)InsertMovement(c,t,productId,actorId,now,"Initial",quantity,"Stock initial",null,null,null,null);
        UserRepository.WriteAudit(c,t,actorId,"StockInitialise","Produit",productId.ToString(),$"QuantiteInitiale={quantity}");t.Commit();
    }

    public void Replenish(long productId,int quantity,string reason,long actorId)
    {
        if(quantity<=0)throw new ArgumentOutOfRangeException(nameof(quantity),"La quantité reçue doit être strictement positive.");
        if(string.IsNullOrWhiteSpace(reason))throw new ArgumentException("Un motif ou une référence est obligatoire.",nameof(reason));
        using var c=database.Create();using var t=c.BeginTransaction(deferred:false);var product=ReadProduct(c,t,productId);
        if(!product.Active)throw new InvalidOperationException("Réactivez le produit archivé avant de le réapprovisionner.");
        var now=DateTime.UtcNow.ToString("O");InsertMovement(c,t,productId,actorId,now,"Reapprovisionnement",quantity,reason.Trim(),null,null,null,null);
        UserRepository.WriteAudit(c,t,actorId,"Reapprovisionnement","Produit",productId.ToString(),$"Quantite={quantity};Motif={reason.Trim()}");t.Commit();
    }

    public long Correct(long productId,int countedQuantity,string reason,long actorId)
    {
        if(countedQuantity<0)throw new ArgumentOutOfRangeException(nameof(countedQuantity),"La quantité comptée ne peut pas être négative.");
        if(string.IsNullOrWhiteSpace(reason))throw new ArgumentException("Le motif de correction est obligatoire.",nameof(reason));
        using var c=database.Create();using var t=c.BeginTransaction(deferred:false);_ = ReadProduct(c,t,productId);var before=ReadBalance(c,t,productId);
        if(before<0||before>int.MaxValue)throw new InvalidOperationException("Le stock actuel est incohérent ou dépasse la limite autorisée.");
        var delta=checked(countedQuantity-(int)before);var now=DateTime.UtcNow.ToString("O");
        if(delta!=0)InsertMovement(c,t,productId,actorId,now,"CorrectionInventaire",delta,reason.Trim(),null,null,(int)before,countedQuantity);
        UserRepository.WriteAudit(c,t,actorId,"CorrectionInventaire","Produit",productId.ToString(),$"QuantiteAvant={before};QuantiteComptee={countedQuantity};Ecart={delta};Motif={reason.Trim()}");t.Commit();return delta;
    }

    public IReadOnlyList<StockMovement> Movements(long productId)
    {
        using var c=database.Create();using var q=c.CreateCommand();q.CommandText="SELECT m.MouvementStockId,m.ProduitId,p.Reference,p.Nom,m.Type,m.QuantiteSignee,m.Motif,u.Nom,m.DateHeureUtc FROM MouvementStock m JOIN Produit p ON p.ProduitId=m.ProduitId JOIN Utilisateur u ON u.UtilisateurId=m.AuteurId WHERE m.ProduitId=$id ORDER BY m.DateHeureUtc,m.MouvementStockId;";q.Parameters.AddWithValue("$id",productId);
        using var r=q.ExecuteReader();var list=new List<StockMovement>();long balance=0;while(r.Read()){var quantity=r.GetInt32(5);balance+=quantity;list.Add(new(r.GetInt64(0),r.GetInt64(1),r.GetString(2),r.GetString(3),r.GetString(4),quantity,checked((int)balance),r.IsDBNull(6)?"":r.GetString(6),r.GetString(7),r.GetString(8)));}return list;
    }

    private sealed record ProductState(bool Active);
    internal static long ReadBalance(SqliteConnection c,SqliteTransaction t,long id)
    {
        using var q=c.CreateCommand();q.Transaction=t;q.CommandText="SELECT COALESCE(SUM(QuantiteSignee),0) FROM MouvementStock WHERE ProduitId=$id;";q.Parameters.AddWithValue("$id",id);return Convert.ToInt64(q.ExecuteScalar());
    }
    internal static void InsertMovement(SqliteConnection c,SqliteTransaction t,long productId,long actorId,string date,string type,int signedQuantity,string? reason,long? lineId,long? annulId,int? before,int? counted)
    {
        using var q=c.CreateCommand();q.Transaction=t;q.CommandText="INSERT INTO MouvementStock(ProduitId,AuteurId,DateHeureUtc,Type,QuantiteSignee,Motif,LigneVenteId,AnnulationId,QuantiteAvant,QuantiteComptee) VALUES($product,$actor,$date,$type,$quantity,$reason,$line,$annul,$before,$counted);";
        q.Parameters.AddWithValue("$product",productId);q.Parameters.AddWithValue("$actor",actorId);q.Parameters.AddWithValue("$date",date);q.Parameters.AddWithValue("$type",type);q.Parameters.AddWithValue("$quantity",signedQuantity);q.Parameters.AddWithValue("$reason",reason is null?DBNull.Value:reason);q.Parameters.AddWithValue("$line",lineId is null?DBNull.Value:lineId.Value);q.Parameters.AddWithValue("$annul",annulId is null?DBNull.Value:annulId.Value);q.Parameters.AddWithValue("$before",before is null?DBNull.Value:before.Value);q.Parameters.AddWithValue("$counted",counted is null?DBNull.Value:counted.Value);q.ExecuteNonQuery();
    }
    private static ProductState ReadProduct(SqliteConnection c,SqliteTransaction t,long id)
    {
        using var q=c.CreateCommand();q.Transaction=t;q.CommandText="SELECT Actif FROM Produit WHERE ProduitId=$id;";q.Parameters.AddWithValue("$id",id);var v=q.ExecuteScalar();if(v is null)throw new InvalidOperationException("Produit introuvable.");return new ProductState(Convert.ToInt32(v)==1);
    }
    private static bool HasInitial(SqliteConnection c,SqliteTransaction t,long id)
    {
        using var q=c.CreateCommand();q.Transaction=t;q.CommandText="SELECT EXISTS(SELECT 1 FROM JournalAudit WHERE Action='StockInitialise' AND Entite='Produit' AND EntiteId=$id);";q.Parameters.AddWithValue("$id",id.ToString());return Convert.ToInt64(q.ExecuteScalar())!=0;
    }
    private static bool Exists(SqliteConnection c,SqliteTransaction t,string sql,long id){using var q=c.CreateCommand();q.Transaction=t;q.CommandText=sql;q.Parameters.AddWithValue("$id",id);return Convert.ToInt64(q.ExecuteScalar())!=0;}
}
