using ICP.Platform.Authorization.Contracts;
using ICP.Platform.Authorization.Extensions;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace ICP.Platform.Authorization.Tests;

public sealed class DataScopeEvaluationScopeTests
{
    [Fact]
    public void NestedOperationSharesTime_AndCommittedReadUsesNewTimeWithSafeRestoration()
    {
        var clock = new MutableClock();
        var services = new ServiceCollection().AddLogging().AddPlatformAuthorization();
        services.AddSingleton<TimeProvider>(clock);
        using var provider = services.BuildServiceProvider();
        using var request = provider.CreateScope();
        var observation = request.ServiceProvider.GetService<IDataScopeEvaluationScope>();
        Assert.NotNull(observation);
        Assert.Throws<InvalidOperationException>(() => observation.ObservedAt);
        var initial = clock.GetUtcNow();
        using (observation.BeginOperation())
        {
            clock.Now = initial.AddSeconds(1);
            using (observation.BeginOperation()) Assert.Equal(initial, observation.ObservedAt);
            Assert.Throws<InvalidOperationException>((Action)(() =>
            {
                using var committedRead = observation.BeginReadAfterWrite();
                Assert.Equal(initial.AddSeconds(1), observation.ObservedAt);
                clock.Now = initial.AddSeconds(2);
                using var nested = observation.BeginOperation();
                Assert.Equal(initial.AddSeconds(1), observation.ObservedAt);
                throw new InvalidOperationException("read failed");
            }));
            Assert.Equal(initial, observation.ObservedAt);
        }
        Assert.Throws<InvalidOperationException>(() => observation.ObservedAt);
        using (observation.BeginOperation()) Assert.Equal(initial.AddSeconds(2), observation.ObservedAt);
    }

    private sealed class MutableClock : TimeProvider
    {
        public DateTimeOffset Now { get; set; } = new(2026, 10, 17, 12, 0, 0, TimeSpan.Zero);
        public override DateTimeOffset GetUtcNow() => Now;
    }
}
