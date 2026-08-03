namespace MafPlayground.Retrieval.Postgres;

public sealed class PostgresRetrievalOptions
{
    public const string ConfigurationSectionName = "AI:Retrieval:Postgres";
    public string ConnectionString { get; set; } = "Host=localhost;Port=5432;Database=maf_playground;Username=postgres;Password=postgres";
}
