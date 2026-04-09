using ICP.Platform.Modeling.Definitions;
using ICP.Platform.Persistence.EfCore.Projection;
using Microsoft.EntityFrameworkCore;

namespace ICP.Platform.Persistence.EfCore;

public sealed class EfPlatformDbContext : DbContext
{
    private readonly IReadOnlyList<ModelDefinition> _definitions;

    public EfPlatformDbContext(
        DbContextOptions<EfPlatformDbContext> options,
        IReadOnlyList<ModelDefinition> definitions)
        : base(options)
    {
        _definitions = definitions;
    }

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        var projector = new EfModelProjector();
        projector.Project(modelBuilder, _definitions);
        base.OnModelCreating(modelBuilder);
    }
}
