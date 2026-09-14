using System.Text.Json;
using ICP.Platform.Acceptance;
using Xunit;

namespace ICP.Platform.Acceptance.Tests;

public sealed class FeatureScaleBrowserObserverTests
{
    private const string Input = """
        {"hostUrl":"http://127.0.0.1:5000","shellUrl":"http://localhost:3000","outputDirectory":"output",
        "administratorECode":"admin","administratorPassword":"private-test-secret",
        "roles":[{"id":"one","code":"scale-one","name":"Scale One"},{"id":"two","code":"scale-two","name":"Scale Two"}],
        "sample":{"pointCount":50,"roleCount":25,"grantCount":1000}}
        """;

    [Fact]
    public void Input_AllowsSmallQualificationSampleWithoutInventingProductionBudget()
    {
        var request = FeatureScaleBrowserObserver.Parse(Input);
        Assert.Equal(50, request.Sample.PointCount);
        Assert.Equal(2, request.Roles.Length);
        Assert.DoesNotContain("private-test-secret", JsonSerializer.Serialize(request));
    }

    [Theory]
    [InlineData("http://127.0.0.1:5000", "https://example.com")]
    [InlineData("http://127.0.0.1:5000", "http://admin:password@localhost:5000")]
    [InlineData("\"id\":\"two\"", "\"id\":\"one\"")]
    public void InvalidOwnershipOrDuplicateRolesFailBeforeOpeningBrowser(string before, string after) =>
        Assert.Throws<ArgumentException>(() => FeatureScaleBrowserObserver.Parse(Input.Replace(before, after)));

    [Fact]
    public void ResponseObservationRecordsActualPageCountsWithoutSerializingGrants()
    {
        const string body = """{"items":[{"secret":"not-an-output"},{"id":"two"}],"total":101}""";
        var observed = JsonSerializer.SerializeToElement(FeatureScaleBrowserObserver.DescribeResponse("/roles?pageIndex=2&pageSize=20", 200, body));
        Assert.Equal(2, observed.GetProperty("itemCount").GetInt32());
        Assert.Equal(101, observed.GetProperty("total").GetInt32());
        Assert.DoesNotContain("not-an-output", observed.ToString());
    }

    [Fact]
    public void SanitizedDiagnosticDoesNotEchoCredentialOrInputBody()
    {
        var request = FeatureScaleBrowserObserver.Parse(Input);
        var text = FeatureScaleBrowserObserver.Redact(request, "failure private-test-secret");
        Assert.Equal("failure [REDACTED]", text);
        Assert.Throws<ArgumentException>(() => FeatureScaleBrowserObserver.Parse("{private-test-secret"));
    }
}
