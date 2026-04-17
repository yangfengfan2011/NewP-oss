using ICP.Platform.Modeling.Definitions;
using ICP.Platform.Persistence.EfCore.Projection;
using Microsoft.EntityFrameworkCore;

namespace ICP.Platform.Persistence.EfCore;

public sealed class EfPlatformDbContext : DbContext
{
    private readonly IReadOnlyList<ModelDefinition> _definitions;
    private readonly string _modelCacheSignature;

    public EfPlatformDbContext(
        DbContextOptions<EfPlatformDbContext> options,
        IReadOnlyList<ModelDefinition> definitions)
        : base(options)
    {
        _definitions = definitions;
        _modelCacheSignature = BuildModelCacheSignature(definitions);
    }

    internal string ModelCacheSignature => _modelCacheSignature;

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        var projector = new EfModelProjector();
        projector.Project(modelBuilder, _definitions);
        base.OnModelCreating(modelBuilder);
    }

    private static string BuildModelCacheSignature(IReadOnlyList<ModelDefinition> definitions) =>
        string.Join(
            "||",
            definitions.Select(static definition =>
                string.Join(
                    "|",
                    new[]
                    {
                        definition.Name,
                        definition.TableName,
                        string.Join(
                            ",",
                            definition.Fields.Select(static field => $"{field.Name}:{field.StoreName}:{field.IsPrimaryKey}:{field.Required}:{field.IsAuditField}")),
                        string.Join(
                            ",",
                            definition.Indexes.Select(static index => $"{index.Name}:{string.Join("+", index.Fields)}:{index.IsUnique}"))
                    })));
}
