using System.Text.Json;
using ICP.Platform.Modules.Authorization.DTOs;
using Xunit;

namespace ICP.Platform.Authorization.Tests;

public sealed class DataScopeDirectSubjectContractTests
{
    [Fact]
    public void ObjectExpiry_RoundTripsWithoutExpandingRuleSubjects()
    {
        const string subject = """{"id":"entry","type":"user","subjectId":"account","priority":0,"actions":{"read":"allow"},"expiresAt":"2026-10-17T12:00:00Z"}""";
        var options = new JsonSerializerOptions(JsonSerializerDefaults.Web);
        var direct = JsonSerializer.Deserialize<DataScopeObjectDto>(
            "{\"resourceKey\":\"workspace.folder\",\"objectId\":\"folder\",\"subjects\":[" + subject + "]}", options)!;
        var output = JsonSerializer.SerializeToElement(direct, options);
        Assert.Equal("2026-10-17T12:00:00Z", output.GetProperty("subjects")[0].GetProperty("expiresAt").GetString());
        Assert.Throws<JsonException>(() => JsonSerializer.Deserialize<DataScopeSubjectDto>(subject, options));
        var old = JsonSerializer.Deserialize<DataScopeObjectDto>(
            """{"resourceKey":"workspace.folder","objectId":"folder","subjects":[{"id":"entry","type":"all","subjectId":null,"priority":0,"actions":{"read":"allow"}}]}""", options)!;
        var explicitNull = JsonSerializer.Deserialize<DataScopeObjectDto>(
            """{"resourceKey":"workspace.folder","objectId":"folder","subjects":[{"id":"entry","type":"all","subjectId":null,"priority":0,"actions":{"read":"allow"},"expiresAt":null}]}""", options)!;
        Assert.Equal(JsonSerializer.Serialize(old, options), JsonSerializer.Serialize(explicitNull, options));
    }
}
