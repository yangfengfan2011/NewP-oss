using ICP.Platform.Modeling.Definitions;
using ICP.Platform.Persistence.EfCore.Projection;
using ICP.Platform.Persistence.Planning;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace ICP.Platform.Persistence.EfCore;

public sealed class EfPlatformDbContext : DbContext
{
    private readonly IReadOnlyList<ModelDefinition> _definitions;
    private readonly string _modelCacheSignature;

    [ActivatorUtilitiesConstructor]
    public EfPlatformDbContext(
        DbContextOptions<EfPlatformDbContext> options,
        CompiledPersistencePlan plan)
        : base(options)
    {
        PersistencePlan = plan;
        _definitions = plan.Models;
        _modelCacheSignature = plan.PlanDigest;
    }

    public EfPlatformDbContext(
        DbContextOptions<EfPlatformDbContext> options,
        IReadOnlyList<ModelDefinition> definitions)
        : this(options, CreateLegacyPlan(definitions))
    {
    }

    public CompiledPersistencePlan PersistencePlan { get; }

    public string ModelCacheSignature => _modelCacheSignature;

    /// <summary>
    /// 把 qualified key 或唯一的 legacy 裸模型名解析为 EF shared-type entity key。
    /// </summary>
    public string ResolveModelKey(string modelKey)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(modelKey);

        var exact = _definitions.SingleOrDefault(definition =>
            string.Equals(definition.LogicalKey, modelKey, StringComparison.Ordinal));
        if (exact is not null)
        {
            return exact.LogicalKey;
        }

        var legacyMatches = _definitions
            .Where(definition => string.Equals(definition.Name, modelKey, StringComparison.Ordinal))
            .ToArray();
        return legacyMatches.Length switch
        {
            1 => legacyMatches[0].LogicalKey,
            0 => throw new InvalidOperationException($"Persistence model '{modelKey}' is absent from the compiled plan."),
            _ => throw new InvalidOperationException($"Persistence model name '{modelKey}' is ambiguous; use moduleId/modelKey.")
        };
    }

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        var projector = new EfModelProjector();
        projector.Project(modelBuilder, _definitions);
        base.OnModelCreating(modelBuilder);
    }

    private static CompiledPersistencePlan CreateLegacyPlan(IReadOnlyList<ModelDefinition> definitions)
    {
        ArgumentNullException.ThrowIfNull(definitions);
        var digest = PersistenceModelDigest.Compute(definitions);
        return new CompiledPersistencePlan(
            "platform.core-legacy",
            digest,
            digest,
            PersistenceProvider.SqlServer,
            digest,
            Array.Empty<byte>(),
            definitions,
            Array.Empty<ModuleMigrationStream>());
    }
}
