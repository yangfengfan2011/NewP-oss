namespace ICP.Platform.Persistence;

public sealed class PersistenceOptions
{
    public const string SectionName = "Persistence";

    public PersistenceProvider Provider { get; set; } = PersistenceProvider.SqlServer;

    public PersistenceAdapterKind? Adapter { get; set; } = PersistenceAdapterKind.EfCore;

    public bool AutoMigrate { get; set; } = true;

    public bool AutoSeed { get; set; } = true;
}
