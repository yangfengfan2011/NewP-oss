using System.Text.Json;
using ICP.Platform.Acceptance;
using Xunit;

namespace ICP.Platform.Acceptance.Tests;

public sealed class AcceptanceJsonReaderTests
{
    private static JsonElement Parse(string json) => JsonDocument.Parse(json).RootElement;

    [Fact]
    public void ReadRequiredString_NamesTheMissingPropertyInsteadOfReturningEmpty()
    {
        var element = Parse("""{"status":"failed"}""");

        var exception = Assert.Throws<InvalidOperationException>(
            () => AcceptanceInfrastructure.ReadRequiredString(element, "diagnostic"));

        Assert.Contains("diagnostic", exception.Message, StringComparison.Ordinal);
        Assert.Contains("missing", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void ReadRequiredString_NamesTheNullPropertySeparatelyFromAMissingOne()
    {
        var element = Parse("""{"failureCode":null}""");

        var exception = Assert.Throws<InvalidOperationException>(
            () => AcceptanceInfrastructure.ReadRequiredString(element, "failureCode"));

        Assert.Contains("null", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void ReadOptionalString_TreatsMissingAndNullAsEmptyForLegitimatelyAbsentFields()
    {
        var element = Parse("""{"status":"passed","failureCode":null}""");

        Assert.Equal(string.Empty, AcceptanceInfrastructure.ReadOptionalString(element, "failureCode"));
        Assert.Equal(string.Empty, AcceptanceInfrastructure.ReadOptionalString(element, "diagnostic"));
        Assert.Equal("passed", AcceptanceInfrastructure.ReadOptionalString(element, "status"));
    }

    [Fact]
    public void Readers_MatchPropertyNamesCaseInsensitively()
    {
        var element = Parse("""{"RunId":"abc","Ports":{"host":5000},"Revision":7}""");

        Assert.Equal("abc", AcceptanceInfrastructure.ReadRequiredString(element, "runId"));
        Assert.Equal(7, AcceptanceInfrastructure.ReadRequiredInt64(element, "revision"));
        Assert.Equal(
            5000,
            AcceptanceInfrastructure.ReadRequiredObject(element, "ports").GetProperty("host").GetInt32());
    }

    [Fact]
    public void ReadRequiredInt64_AcceptsNumbersAndNumericStringsButRejectsOtherKinds()
    {
        var element = Parse("""{"revision":7,"quoted":"8","text":"not-a-number"}""");

        Assert.Equal(7, AcceptanceInfrastructure.ReadRequiredInt64(element, "revision"));
        Assert.Equal(8, AcceptanceInfrastructure.ReadRequiredInt64(element, "quoted"));

        var exception = Assert.Throws<InvalidOperationException>(
            () => AcceptanceInfrastructure.ReadRequiredInt64(element, "text"));

        Assert.Contains("integer", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void ReadRequiredObject_RejectsAPropertyThatIsNotAnObject()
    {
        var element = Parse("""{"ports":"5000"}""");

        var exception = Assert.Throws<InvalidOperationException>(
            () => AcceptanceInfrastructure.ReadRequiredObject(element, "ports"));

        Assert.Contains("object", exception.Message, StringComparison.Ordinal);
    }
}
