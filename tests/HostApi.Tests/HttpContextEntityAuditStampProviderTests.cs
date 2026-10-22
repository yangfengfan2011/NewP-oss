using System.Security.Claims;
using ICP.Platform.HostApi.Persistence;
using ICP.Platform.Persistence.Auditing;
using Microsoft.AspNetCore.Http;
using Xunit;

namespace ICP.Platform.HostApi.Tests;

public sealed class HttpContextEntityAuditStampProviderTests
{
    [Fact]
    public void GetCurrent_WhenRequestIsAuthenticated_UsesTrustedSubjectId()
    {
        var now = new DateTimeOffset(2026, 8, 10, 8, 0, 0, TimeSpan.Zero);
        var context = new DefaultHttpContext
        {
            User = new ClaimsPrincipal(new ClaimsIdentity(
                [new Claim(ClaimTypes.NameIdentifier, "subject-1")],
                "test"))
        };
        var provider = new HttpContextEntityAuditStampProvider(
            new HttpContextAccessor { HttpContext = context },
            new FixedTimeProvider(now));

        var stamp = provider.GetCurrent();

        Assert.Equal("subject-1", stamp.ActorId);
        Assert.Equal(now.UtcDateTime, stamp.OccurredAtUtc);
    }

    [Fact]
    public void GetCurrent_WhenThereIsNoRequest_UsesSystemActor()
    {
        var now = new DateTimeOffset(2026, 8, 10, 8, 0, 0, TimeSpan.Zero);
        var provider = new HttpContextEntityAuditStampProvider(
            new HttpContextAccessor(),
            new FixedTimeProvider(now));

        var stamp = provider.GetCurrent();

        Assert.Equal(SystemEntityAuditStampProvider.SystemActorId, stamp.ActorId);
        Assert.Equal(now.UtcDateTime, stamp.OccurredAtUtc);
    }

    private sealed class FixedTimeProvider : TimeProvider
    {
        private readonly DateTimeOffset _now;

        public FixedTimeProvider(DateTimeOffset now)
        {
            _now = now;
        }

        public override DateTimeOffset GetUtcNow() => _now;
    }
}
