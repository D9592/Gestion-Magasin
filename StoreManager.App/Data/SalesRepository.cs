using Microsoft.Data.Sqlite;
using StoreManager.App.Models;

namespace StoreManager.App.Data;

internal sealed class SalesRepository(SqliteConnectionFactory database,StoreSettingsRepository settings)
{
    private sealed record CurrentProduct(long Id,string Reference,string Name,long Price,bool Active,long Balance);

    public SaleValidationResult ValidateAndCommit(UserSession actor,IReadOnlyList<SaleLineDraft> drafts,PaymentMode mode,long amountReceived,string token)
    {
        if(drafts.Count==0)throw new InvalidOperationException("Le panier est vide.");
        if(!Guid.TryParseExact(token,"N",out _))throw new ArgumentException("Jeton de validation invalide.",nameof(token));
        if(!Enum.IsDefined(mode))throw new ArgumentOutOfRangeException(nameof(mode));
        var lines=drafts.Select(d=>{if(d.ProductId<=0||d.Quantity<=0||d.ExpectedUnitPrice<=0)throw new ArgumentException("Le panier contient une ligne invalide.");return d;})
            .GroupBy(d=>d.ProductId).Select(g=>new SaleLineDraft(g.Key,checked(g.Sum(x=>x.Quantity)),g.First().ExpectedUnitPrice,g.First().Reference,g.First().Name)).ToArray();
        using var c=database.Create();using var t=c.BeginTransaction(deferred:false);
        var existing=FindByToken(c,t,token);if(existing is not null)
        {
            if(!IsSameRequest(c,t,existing.SaleId,actor.UserId,lines,mode,amountReceived))throw new InvalidOperationException("Ce jeton de validation est déjà lié à une autre vente.");
            t.Commit();return new SaleValidationResult(existing,true);
        }
        var current=new List<CurrentProduct>(lines.Length);var updatedLines=new List<RepricedSaleLine>(lines.Length);long currentTotal=0;var priceChanged=false;
        foreach(var draft in lines)
        {
            var product=ReadCurrentProduct(c,t,draft.ProductId);if(!product.Active)throw new InvalidOperationException($"Le produit {product.Name} ({product.Reference}) est archivé et ne peut pas être vendu.");
            if(product.Balance<draft.Quantity)throw new InsufficientStockException(product.Reference,product.Name,checked((int)Math.Max(0,product.Balance)),draft.Quantity);
            current.Add(product);priceChanged|=product.Price!=draft.ExpectedUnitPrice;currentTotal=checked(currentTotal+checked(product.Price*draft.Quantity));
            updatedLines.Add(new(draft.ProductId,draft.Quantity,product.Price,product.Reference,product.Name));
        }
        if(priceChanged)throw new SalePriceChangedException(updatedLines,currentTotal);
        if(currentTotal<=0)throw new InvalidOperationException("Le total de la vente doit être positif.");
        if(mode==PaymentMode.Especes&&amountReceived<currentTotal)throw new InvalidOperationException("Le montant reçu est insuffisant.");
        if(mode!=PaymentMode.Especes&&amountReceived!=currentTotal)throw new InvalidOperationException("Pour Mobile Money et carte, le montant reçu doit correspondre au total.");
        var change=mode==PaymentMode.Especes?amountReceived-currentTotal:0L;
        var utc=DateTime.UtcNow;var zone=settings.TimeZone();var businessDate=TimeZoneInfo.ConvertTimeFromUtc(utc,zone).ToString("yyyy-MM-dd");var utcText=utc.ToString("O");
        var number=$"V-{businessDate.Replace("-",string.Empty)}-{Guid.NewGuid():N}"[..23];
        using(var insert=c.CreateCommand())
        {
            insert.Transaction=t;insert.CommandText="INSERT INTO Vente(Numero,JetonValidation,DateHeureUtc,DateMetier,VendeurId,Total,ModePaiement,MontantRecu,MonnaieRendue,Statut) VALUES($number,$token,$utc,$businessDate,$seller,$total,$mode,$received,$change,'Validee'); SELECT last_insert_rowid();";
            insert.Parameters.AddWithValue("$number",number);insert.Parameters.AddWithValue("$token",token);insert.Parameters.AddWithValue("$utc",utcText);insert.Parameters.AddWithValue("$businessDate",businessDate);insert.Parameters.AddWithValue("$seller",actor.UserId);insert.Parameters.AddWithValue("$total",currentTotal);insert.Parameters.AddWithValue("$mode",mode.ToString());insert.Parameters.AddWithValue("$received",amountReceived);insert.Parameters.AddWithValue("$change",change);
            var saleId=Convert.ToInt64(insert.ExecuteScalar());
            foreach(var line in lines)
            {
                var product=current.Single(p=>p.Id==line.ProductId);var subtotal=checked(product.Price*line.Quantity);long lineId;
                using(var lineCommand=c.CreateCommand())
                {
                    lineCommand.Transaction=t;lineCommand.CommandText="INSERT INTO LigneVente(VenteId,ProduitId,ReferenceSnapshot,NomSnapshot,Quantite,PrixUnitaire,SousTotal) VALUES($sale,$product,$reference,$name,$quantity,$price,$subtotal); SELECT last_insert_rowid();";
                    lineCommand.Parameters.AddWithValue("$sale",saleId);lineCommand.Parameters.AddWithValue("$product",product.Id);lineCommand.Parameters.AddWithValue("$reference",product.Reference);lineCommand.Parameters.AddWithValue("$name",product.Name);lineCommand.Parameters.AddWithValue("$quantity",line.Quantity);lineCommand.Parameters.AddWithValue("$price",product.Price);lineCommand.Parameters.AddWithValue("$subtotal",subtotal);lineId=Convert.ToInt64(lineCommand.ExecuteScalar());
                }
                StockRepository.InsertMovement(c,t,product.Id,actor.UserId,utcText,"Vente",-line.Quantity,$"Vente {number}",lineId,null,null,null);
            }
            UserRepository.WriteAudit(c,t,actor.UserId,"VenteValidee","Vente",saleId.ToString(),$"Numero={number};Total={currentTotal};Paiement={mode}");
            t.Commit();return new SaleValidationResult(ReadSummary(c,saleId),false);
        }
    }

    public PageResult<SaleSummary> History(UserSession actor,string? search,string? fromDate,string? throughDate,long? sellerId,string? status,int page,int pageSize)
    {
        if(page<0||pageSize is <1 or >500)throw new ArgumentOutOfRangeException(nameof(page));
        using var c=database.Create();using var q=c.CreateCommand();
        q.CommandText="SELECT v.VenteId,v.Numero,v.DateHeureUtc,v.DateMetier,u.Nom,v.ModePaiement,v.Total,v.MontantRecu,v.MonnaieRendue,v.Statut,v.VendeurId FROM Vente v JOIN Utilisateur u ON u.UtilisateurId=v.VendeurId WHERE ($search='' OR v.Numero LIKE $like) AND ($from IS NULL OR v.DateMetier>=$from) AND ($through IS NULL OR v.DateMetier<=$through) AND ($seller IS NULL OR v.VendeurId=$seller) AND ($status IS NULL OR v.Statut=$status) AND ($actorRole='Administrateur' OR v.VendeurId=$actorId) ORDER BY v.DateHeureUtc DESC,v.VenteId DESC LIMIT $limit OFFSET $offset;";
        q.Parameters.AddWithValue("$search",search?.Trim()??"");q.Parameters.AddWithValue("$like",$"%{search?.Trim()}%");q.Parameters.AddWithValue("$from",string.IsNullOrWhiteSpace(fromDate)?DBNull.Value:fromDate);q.Parameters.AddWithValue("$through",string.IsNullOrWhiteSpace(throughDate)?DBNull.Value:throughDate);q.Parameters.AddWithValue("$seller",sellerId is null?DBNull.Value:sellerId.Value);q.Parameters.AddWithValue("$status",string.IsNullOrWhiteSpace(status)?DBNull.Value:status);q.Parameters.AddWithValue("$actorRole",actor.Role.ToString());q.Parameters.AddWithValue("$actorId",actor.UserId);q.Parameters.AddWithValue("$limit",pageSize+1);q.Parameters.AddWithValue("$offset",checked(page*pageSize));
        using var r=q.ExecuteReader();var list=new List<SaleSummary>();while(r.Read())list.Add(ReadSummary(r));var more=list.Count>pageSize;if(more)list.RemoveAt(list.Count-1);return new PageResult<SaleSummary>(list,more);
    }

    public SaleDetail Detail(long saleId)
    {
        using var c=database.Create();var summary=ReadSummary(c,saleId);string token;
        using(var q=c.CreateCommand()){q.CommandText="SELECT JetonValidation FROM Vente WHERE VenteId=$id;";q.Parameters.AddWithValue("$id",saleId);token=Convert.ToString(q.ExecuteScalar())??throw new InvalidOperationException("Vente introuvable.");}
        var lines=new List<SaleLineDetail>();using(var q=c.CreateCommand()){q.CommandText="SELECT ProduitId,ReferenceSnapshot,NomSnapshot,Quantite,PrixUnitaire,SousTotal FROM LigneVente WHERE VenteId=$id ORDER BY LigneVenteId;";q.Parameters.AddWithValue("$id",saleId);using var r=q.ExecuteReader();while(r.Read())lines.Add(new(r.GetInt64(0),r.GetString(1),r.GetString(2),r.GetInt32(3),r.GetInt64(4),r.GetInt64(5)));}
        return new SaleDetail(summary,token,lines,settings.StoreName(),settings.TimeZoneId());
    }

    public void Cancel(long saleId,string reason,long actorId)
    {
        if(string.IsNullOrWhiteSpace(reason))throw new ArgumentException("Le motif d'annulation est obligatoire.",nameof(reason));
        using var c=database.Create();using var t=c.BeginTransaction(deferred:false);
        string status;string number;using(var q=c.CreateCommand()){q.Transaction=t;q.CommandText="SELECT Statut,Numero FROM Vente WHERE VenteId=$id;";q.Parameters.AddWithValue("$id",saleId);using var r=q.ExecuteReader();if(!r.Read())throw new InvalidOperationException("Vente introuvable.");status=r.GetString(0);number=r.GetString(1);}
        if(status!="Validee")throw new InvalidOperationException("Cette vente a déjà été annulée ou n'est plus annulable.");
        var now=DateTime.UtcNow.ToString("O");long annulId;
        using(var q=c.CreateCommand()){q.Transaction=t;q.CommandText="INSERT INTO AnnulationVente(VenteId,AuteurId,DateHeureUtc,Motif) VALUES($sale,$author,$date,$reason); SELECT last_insert_rowid();";q.Parameters.AddWithValue("$sale",saleId);q.Parameters.AddWithValue("$author",actorId);q.Parameters.AddWithValue("$date",now);q.Parameters.AddWithValue("$reason",reason.Trim());annulId=Convert.ToInt64(q.ExecuteScalar());}
        var lines=new List<(long LineId,long ProductId,int Quantity)>();using(var q=c.CreateCommand()){q.Transaction=t;q.CommandText="SELECT LigneVenteId,ProduitId,Quantite FROM LigneVente WHERE VenteId=$sale;";q.Parameters.AddWithValue("$sale",saleId);using var r=q.ExecuteReader();while(r.Read())lines.Add((r.GetInt64(0),r.GetInt64(1),r.GetInt32(2)));}
        foreach(var line in lines)StockRepository.InsertMovement(c,t,line.ProductId,actorId,now,"AnnulationVente",line.Quantity,$"Annulation vente {number}",line.LineId,annulId,null,null);
        using(var q=c.CreateCommand()){q.Transaction=t;q.CommandText="UPDATE Vente SET Statut='Annulee' WHERE VenteId=$id AND Statut='Validee';";q.Parameters.AddWithValue("$id",saleId);if(q.ExecuteNonQuery()!=1)throw new InvalidOperationException("La vente n'est plus annulable.");}
        UserRepository.WriteAudit(c,t,actorId,"VenteAnnulee","Vente",saleId.ToString(),$"Numero={number};Motif={reason.Trim()}");t.Commit();
    }

    private CurrentProduct ReadCurrentProduct(SqliteConnection c,SqliteTransaction t,long id)
    {
        using var q=c.CreateCommand();q.Transaction=t;q.CommandText="SELECT p.ProduitId,p.Reference,p.Nom,p.PrixVente,p.Actif,COALESCE((SELECT SUM(m.QuantiteSignee) FROM MouvementStock m WHERE m.ProduitId=p.ProduitId),0) FROM Produit p WHERE p.ProduitId=$id;";q.Parameters.AddWithValue("$id",id);using var r=q.ExecuteReader();if(!r.Read())throw new InvalidOperationException($"Le produit #{id} n'existe plus.");return new(r.GetInt64(0),r.GetString(1),r.GetString(2),r.GetInt64(3),r.GetInt32(4)==1,r.GetInt64(5));
    }
    private SaleSummary? FindByToken(SqliteConnection c,SqliteTransaction t,string token)
    {using var q=c.CreateCommand();q.Transaction=t;q.CommandText="SELECT VenteId FROM Vente WHERE JetonValidation=$token;";q.Parameters.AddWithValue("$token",token);var id=q.ExecuteScalar();return id is null?null:ReadSummary(c,Convert.ToInt64(id),t);}
    private static bool IsSameRequest(SqliteConnection c,SqliteTransaction t,long saleId,long actorId,IReadOnlyList<SaleLineDraft> lines,PaymentMode mode,long received)
    {
        long seller,total,storedReceived;string payment;
        using(var q=c.CreateCommand()){q.Transaction=t;q.CommandText="SELECT VendeurId,Total,ModePaiement,MontantRecu FROM Vente WHERE VenteId=$id;";q.Parameters.AddWithValue("$id",saleId);using var r=q.ExecuteReader();if(!r.Read())return false;seller=r.GetInt64(0);total=r.GetInt64(1);payment=r.GetString(2);storedReceived=r.GetInt64(3);}
        if(seller!=actorId||payment!=mode.ToString()||storedReceived!=received)return false;
        var stored=new Dictionary<long,(int Quantity,long Price)>();using(var q=c.CreateCommand()){q.Transaction=t;q.CommandText="SELECT ProduitId,Quantite,PrixUnitaire FROM LigneVente WHERE VenteId=$id;";q.Parameters.AddWithValue("$id",saleId);using var r=q.ExecuteReader();while(r.Read())stored.Add(r.GetInt64(0),(r.GetInt32(1),r.GetInt64(2)));}
        if(stored.Count!=lines.Count)return false;long requestedTotal=0;
        foreach(var line in lines){if(!stored.TryGetValue(line.ProductId,out var value)||value.Quantity!=line.Quantity||value.Price!=line.ExpectedUnitPrice)return false;requestedTotal=checked(requestedTotal+checked(value.Price*value.Quantity));}
        return requestedTotal==total;
    }
    private SaleSummary ReadSummary(SqliteConnection c,long id,SqliteTransaction? transaction=null)
    {using var q=c.CreateCommand();q.Transaction=transaction;q.CommandText="SELECT v.VenteId,v.Numero,v.DateHeureUtc,v.DateMetier,u.Nom,v.ModePaiement,v.Total,v.MontantRecu,v.MonnaieRendue,v.Statut,v.VendeurId FROM Vente v JOIN Utilisateur u ON u.UtilisateurId=v.VendeurId WHERE v.VenteId=$id;";q.Parameters.AddWithValue("$id",id);using var r=q.ExecuteReader();if(!r.Read())throw new InvalidOperationException("Vente introuvable.");return ReadSummary(r);}
    private static SaleSummary ReadSummary(SqliteDataReader r)=>new(r.GetInt64(0),r.GetString(1),r.GetString(2),r.GetString(3),r.GetString(4),r.GetString(5),r.GetInt64(6),r.GetInt64(7),r.GetInt64(8),r.GetString(9),r.GetInt64(10));
}
