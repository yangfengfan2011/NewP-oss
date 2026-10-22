using ICP.Platform.Crud.Abstractions;
using ICP.Platform.Crud.Extensions;
using ICP.Platform.Crud.LinkQueries;
using ICP.Platform.Crud.ObjectOperations;
using ICP.Platform.Crud.Persistence;
using ICP.Platform.Crud.Runtime;
using ICP.Platform.Persistence.Auditing;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace ICP.Platform.Crud.Tests;

public sealed class StandardCrudRegistrationTests
{
    [Fact]
    public void AddStandardCrudKernel_RegistersOneSharedEfRepositoryForAllWriteRoles()
    {
        var services = new ServiceCollection();

        services.AddStandardCrudKernel();

        AssertRegistration<EfStandardCrudRepository>(services, typeof(EfStandardCrudRepository));
        AssertFactoryRegistration<IStandardCrudRepository>(services);
        AssertFactoryRegistration<IEntityAuditValueProvider>(services);
        Assert.DoesNotContain(services, item => item.ServiceType == typeof(IStandardCrudCreationParticipant));
        Assert.DoesNotContain(services, item => item.ServiceType == typeof(IStandardCrudUpdateParticipant));
        AssertFactoryRegistration<IObjectOperationExecutor>(services);
        Assert.Contains(services, item => item.ServiceType == typeof(Microsoft.Extensions.Hosting.IHostedService)
            && item.ImplementationType?.Name == "ObjectOperationAssemblyStartupValidator");
        AssertRegistration<StandardCrudDefaultWriter>(services, typeof(StandardCrudDefaultWriter));
        var linkCatalog = Assert.Single(services, item => item.ServiceType == typeof(LinkQueryCatalog));
        Assert.NotNull(linkCatalog.ImplementationFactory);
        Assert.Equal(ServiceLifetime.Singleton, linkCatalog.Lifetime);
        AssertRegistration<LinkQueryReader>(services, typeof(LinkQueryReader));
        AssertRegistration<DisplayValueProjection>(services, typeof(DisplayValueProjection));
    }

    private static void AssertFactoryRegistration<TService>(IServiceCollection services)
    {
        var descriptor = Assert.Single(services, item => item.ServiceType == typeof(TService));
        Assert.NotNull(descriptor.ImplementationFactory);
        Assert.Equal(ServiceLifetime.Scoped, descriptor.Lifetime);
    }

    private static void AssertRegistration<TImplementation>(IServiceCollection services, Type serviceType)
    {
        var descriptor = Assert.Single(services, item => item.ServiceType == serviceType);
        Assert.Equal(typeof(TImplementation), descriptor.ImplementationType);
        Assert.Equal(ServiceLifetime.Scoped, descriptor.Lifetime);
    }
}
