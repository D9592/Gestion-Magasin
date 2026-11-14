namespace StoreManager.App.Data;

internal sealed class StoreSettingsRepository(SqliteConnectionFactory database)
{
    public string StoreName()=>Read("NomMagasin")??"NOVA";
    public string TimeZoneId()=>Read("FuseauHoraire")??TimeZoneInfo.Local.Id;
    public TimeZoneInfo TimeZone()=>TimeZoneInfo.FindSystemTimeZoneById(TimeZoneId());
    private string? Read(string key)
    {
        using var c=database.Create();using var q=c.CreateCommand();q.CommandText="SELECT Valeur FROM Parametre WHERE Cle=$key;";q.Parameters.AddWithValue("$key",key);var value=q.ExecuteScalar();return value is null or DBNull?null:Convert.ToString(value);
    }
}
