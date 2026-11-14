using Microsoft.Data.Sqlite;
using System.Text;
using StoreManager.App.Models;

namespace StoreManager.App.Data;

internal sealed class CatalogRepository(SqliteConnectionFactory database)
{
    public IReadOnlyList<Category> Categories(string? search, bool activeOnly = false)
    {
        using var c=database.Create(); using var q=c.CreateCommand(); q.CommandText="SELECT CategorieId,Nom,Active FROM Categorie WHERE ($search='' OR Nom LIKE $like) AND ($active=0 OR Active=1) ORDER BY Nom COLLATE NOCASE;";
        q.Parameters.AddWithValue("$search", search?.Trim() ?? ""); q.Parameters.AddWithValue("$like", $"%{search?.Trim()}%"); q.Parameters.AddWithValue("$active", activeOnly?1:0);
        using var r=q.ExecuteReader(); var list=new List<Category>(); while(r.Read())list.Add(new(r.GetInt64(0),r.GetString(1),r.GetInt32(2)==1)); return list;
    }
    public void SaveCategory(long? id, string name, bool active, long actor)
    {
        using var c=database.Create(); using var t=c.BeginTransaction(); using var q=c.CreateCommand(); q.Transaction=t;
        object? before = null;
        if (id is not null) { using var old=c.CreateCommand(); old.Transaction=t; old.CommandText="SELECT Nom,Active FROM Categorie WHERE CategorieId=$id;"; old.Parameters.AddWithValue("$id",id.Value); using var r=old.ExecuteReader(); if(!r.Read())throw new InvalidOperationException("Catégorie introuvable."); before=new { Nom=r.GetString(0), Active=r.GetInt32(1)==1 }; }
        q.CommandText=id is null?"INSERT INTO Categorie(Nom,NomNormalise,Active) VALUES($name,$normalised,$active); SELECT last_insert_rowid();":"UPDATE Categorie SET Nom=$name,NomNormalise=$normalised,Active=$active WHERE CategorieId=$id; SELECT $id;";
        q.Parameters.AddWithValue("$name",name.Trim());q.Parameters.AddWithValue("$normalised",name.Trim().Normalize(NormalizationForm.FormKC).ToUpperInvariant()); q.Parameters.AddWithValue("$active",active?1:0); q.Parameters.AddWithValue("$id",id is null?DBNull.Value:id.Value); var savedId=Convert.ToInt64(q.ExecuteScalar());
        var after=new { Nom=name.Trim(), Active=active }; UserRepository.WriteAudit(c,t,actor,id is null?"CategorieCreee":"CategorieModifiee","Categorie",savedId.ToString(),System.Text.Json.JsonSerializer.Serialize(new { Avant=before, Apres=after })); t.Commit();
    }
    public IReadOnlyList<Produit> Products(string? search, long? categoryId, bool? active, string? availability = null)
        => ProductsPage(search,categoryId,active,availability,0,int.MaxValue).Items;
    public PageResult<Produit> ProductsPage(string? search,long? categoryId,bool? active,string? availability,int page,int pageSize)
    {
        if(page<0||pageSize<1||(pageSize>500&&pageSize!=int.MaxValue))throw new ArgumentOutOfRangeException(nameof(page));
        using var c=database.Create(); using var q=c.CreateCommand();
        q.CommandText="SELECT p.ProduitId,p.Reference,p.Nom,p.CategorieId,c.Nom,p.PrixVente,p.SeuilAlerte,p.Actif,COALESCE(s.QuantiteStock,0),CASE WHEN p.Actif=0 THEN 'Archivé' WHEN COALESCE(s.QuantiteStock,0)=0 THEN 'En rupture' WHEN COALESCE(s.QuantiteStock,0)<=p.SeuilAlerte THEN 'Stock faible' ELSE 'Disponible' END FROM Produit p LEFT JOIN Categorie c ON c.CategorieId=p.CategorieId LEFT JOIN VueStockCourant s ON s.ProduitId=p.ProduitId WHERE ($search='' OR p.Reference LIKE $like OR p.Nom LIKE $like) AND ($category IS NULL OR p.CategorieId=$category) AND ($active IS NULL OR p.Actif=$active) AND ($availability IS NULL OR (p.Actif=1 AND (($availability='En rupture' AND COALESCE(s.QuantiteStock,0)=0) OR ($availability='Stock faible' AND COALESCE(s.QuantiteStock,0)>0 AND COALESCE(s.QuantiteStock,0)<=p.SeuilAlerte) OR ($availability='Disponible' AND COALESCE(s.QuantiteStock,0)>p.SeuilAlerte)))) ORDER BY p.Nom COLLATE NOCASE,p.ProduitId LIMIT $limit OFFSET $offset;";
        q.Parameters.AddWithValue("$search",search?.Trim()??"");q.Parameters.AddWithValue("$like",$"%{search?.Trim()}%");q.Parameters.AddWithValue("$category",categoryId is null?DBNull.Value:categoryId.Value);q.Parameters.AddWithValue("$active",active is null?DBNull.Value:active.Value?1:0);q.Parameters.AddWithValue("$availability",string.IsNullOrWhiteSpace(availability)?DBNull.Value:availability);
        var limit=pageSize==int.MaxValue?int.MaxValue:pageSize+1;q.Parameters.AddWithValue("$limit",limit);q.Parameters.AddWithValue("$offset",pageSize==int.MaxValue?0:checked(page*pageSize));
        using var r=q.ExecuteReader(); var list=new List<Produit>(); while(r.Read()) list.Add(new(r.GetInt64(0),r.GetString(1),r.GetString(2),r.IsDBNull(3)?null:r.GetInt64(3),r.IsDBNull(4)?null:r.GetString(4),r.GetInt64(5),r.GetInt32(6),r.GetInt32(7)==1,r.GetInt32(8),r.GetString(9)));
        var hasMore=list.Count>pageSize;if(hasMore)list.RemoveAt(list.Count-1);return new PageResult<Produit>(list,hasMore);
    }
    public long? ProductCategoryId(long productId)
    {
        using var c=database.Create();using var q=c.CreateCommand();q.CommandText="SELECT CategorieId FROM Produit WHERE ProduitId=$id;";q.Parameters.AddWithValue("$id",productId);var value=q.ExecuteScalar();return value is null or DBNull?null:Convert.ToInt64(value);
    }
    public IReadOnlyList<Produit> SellableProducts(string? search,int limit=100)
    {
        using var c=database.Create();using var q=c.CreateCommand();q.CommandText="SELECT p.ProduitId,p.Reference,p.Nom,p.CategorieId,c.Nom,p.PrixVente,p.SeuilAlerte,p.Actif,CAST(s.QuantiteStock AS INTEGER),CASE WHEN s.QuantiteStock<=p.SeuilAlerte THEN 'Stock faible' ELSE 'Disponible' END FROM Produit p LEFT JOIN Categorie c ON c.CategorieId=p.CategorieId JOIN VueStockCourant s ON s.ProduitId=p.ProduitId WHERE p.Actif=1 AND s.QuantiteStock>0 AND ($search='' OR p.Reference LIKE $like OR p.Nom LIKE $like) ORDER BY p.Nom COLLATE NOCASE LIMIT $limit;";
        q.Parameters.AddWithValue("$search",search?.Trim()??"");q.Parameters.AddWithValue("$like",$"%{search?.Trim()}%");q.Parameters.AddWithValue("$limit",limit);using var r=q.ExecuteReader();var list=new List<Produit>();while(r.Read())list.Add(new(r.GetInt64(0),r.GetString(1),r.GetString(2),r.IsDBNull(3)?null:r.GetInt64(3),r.IsDBNull(4)?null:r.GetString(4),r.GetInt64(5),r.GetInt32(6),r.GetInt32(7)==1,r.GetInt32(8),r.GetString(9)));return list;
    }
    public void SaveProduct(long? id,string reference,string name,long? category,long salePrice,int threshold,bool active,long actor)
    {
        using var c=database.Create();using var t=c.BeginTransaction();using var q=c.CreateCommand();q.Transaction=t;
        object? before=null;
        if(id is not null){using var old=c.CreateCommand();old.Transaction=t;old.CommandText="SELECT Reference,Nom,CategorieId,PrixVente,SeuilAlerte,Actif FROM Produit WHERE ProduitId=$id;";old.Parameters.AddWithValue("$id",id.Value);using var r=old.ExecuteReader();if(!r.Read())throw new InvalidOperationException("Produit introuvable.");before=new {Reference=r.GetString(0),Nom=r.GetString(1),CategorieId=r.IsDBNull(2)?(long?)null:r.GetInt64(2),PrixVente=r.GetInt64(3),SeuilAlerte=r.GetInt32(4),Actif=r.GetInt32(5)==1};}
        q.CommandText=id is null?"INSERT INTO Produit(Reference,Nom,CategorieId,PrixVente,SeuilAlerte,Actif) VALUES($ref,$name,$cat,$price,$threshold,$active); SELECT last_insert_rowid();":"UPDATE Produit SET Reference=$ref,Nom=$name,CategorieId=$cat,PrixVente=$price,SeuilAlerte=$threshold,Actif=$active WHERE ProduitId=$id; SELECT $id;";
        q.Parameters.AddWithValue("$ref",reference.Trim());q.Parameters.AddWithValue("$name",name.Trim());q.Parameters.AddWithValue("$cat",category is null?DBNull.Value:category.Value);q.Parameters.AddWithValue("$price",salePrice);q.Parameters.AddWithValue("$threshold",threshold);q.Parameters.AddWithValue("$active",active?1:0);q.Parameters.AddWithValue("$id",id is null?DBNull.Value:id.Value);var savedId=Convert.ToInt64(q.ExecuteScalar());
        var after=new {Reference=reference.Trim(),Nom=name.Trim(),CategorieId=category,PrixVente=salePrice,SeuilAlerte=threshold,Actif=active};UserRepository.WriteAudit(c,t,actor,id is null?"ProduitCree":"ProduitModifie","Produit",savedId.ToString(),System.Text.Json.JsonSerializer.Serialize(new {Avant=before,Apres=after}));t.Commit();
    }
}
