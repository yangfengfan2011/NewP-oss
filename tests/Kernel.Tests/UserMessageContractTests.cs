using System.Text.Json;
using ICP.Platform.Sdk.UserMessages;
using Xunit;

namespace ICP.Platform.Kernel.Tests;

public class UserMessageContractTests
{
    [Fact]
    public void UserMessageDto_Success_CreatesSuccessState()
    {
        var dto = UserMessageDto.SuccessMessage(
            "Messages.Ready",
            new Dictionary<string, object?>
            {
                ["count"] = 2,
                ["enabled"] = true,
                ["note"] = null,
            });

        Assert.True(dto.Success);
        Assert.Equal(UserMessageLevel.success, dto.Level);
        Assert.Equal("Messages.Ready", dto.MessageKey);
        Assert.Equal(2, dto.MessageArgs?["count"]);
        Assert.Equal(true, dto.MessageArgs?["enabled"]);
        Assert.Null(dto.MessageArgs?["note"]);
    }

    [Fact]
    public void UserMessageDto_Failure_RejectsComplexMessageArgs()
    {
        Assert.Throws<ArgumentException>(() =>
            UserMessageDto.Failure(
                "Errors.InvalidState",
                new Dictionary<string, object?>
                {
                    ["nested"] = new { Name = "Tenant" },
                }));

        Assert.Throws<ArgumentException>(() =>
            UserMessageDto.Failure(
                "Errors.InvalidState",
                new Dictionary<string, object?>
                {
                    ["items"] = new[] { 1, 2, 3 },
                }));
    }

    [Fact]
    public void UserMessageException_RejectsComplexMessageArgs()
    {
        Assert.Throws<ArgumentException>(() =>
            new UserMessageException(
                400,
                "Errors.InvalidState",
                new Dictionary<string, object?>
                {
                    ["nested"] = new { Name = "Tenant" },
                }));

        Assert.Throws<ArgumentException>(() =>
            new UserMessageException(
                400,
                "Errors.InvalidState",
                new Dictionary<string, object?>
                {
                    ["items"] = new[] { 1, 2, 3 },
                }));
    }

    [Fact]
    public void UserMessageDto_RejectsNonFiniteNumericMessageArgs()
    {
        Assert.Throws<ArgumentException>(() =>
            UserMessageDto.Failure(
                "Errors.InvalidState",
                new Dictionary<string, object?>
                {
                    ["ratio"] = double.NaN,
                }));

        Assert.Throws<ArgumentException>(() =>
            new UserMessageException(
                400,
                "Errors.InvalidState",
                new Dictionary<string, object?>
                {
                    ["ratio"] = double.PositiveInfinity,
                }));
    }

    [Fact]
    public void UserMessageDto_Failure_SerializesSharedContractFields()
    {
        var dto = UserMessageDto.Failure(
            "Errors.InvalidState",
            new Dictionary<string, object?>
            {
                ["code"] = "A1",
                ["count"] = 3,
                ["enabled"] = true,
                ["note"] = null,
            },
            fallbackMessage: "Fallback text",
            code: "ERR_001",
            fieldKey: "Fields.Name",
            actionKey: "Actions.Save",
            statusKey: "Status.Ready");

        var json = JsonSerializer.Serialize(dto, new JsonSerializerOptions(JsonSerializerDefaults.Web));
        using var document = JsonDocument.Parse(json);
        var root = document.RootElement;

        Assert.False(root.GetProperty("success").GetBoolean());
        Assert.Equal("error", root.GetProperty("level").GetString());
        Assert.Equal("Errors.InvalidState", root.GetProperty("messageKey").GetString());
        Assert.Equal("A1", root.GetProperty("messageArgs").GetProperty("code").GetString());
        Assert.Equal(3, root.GetProperty("messageArgs").GetProperty("count").GetInt32());
        Assert.True(root.GetProperty("messageArgs").GetProperty("enabled").GetBoolean());
        Assert.True(root.GetProperty("messageArgs").GetProperty("note").ValueKind == JsonValueKind.Null);
        Assert.Equal("Fallback text", root.GetProperty("fallbackMessage").GetString());
        Assert.Equal("ERR_001", root.GetProperty("code").GetString());
        Assert.Equal("Fields.Name", root.GetProperty("fieldKey").GetString());
        Assert.Equal("Actions.Save", root.GetProperty("actionKey").GetString());
        Assert.Equal("Status.Ready", root.GetProperty("statusKey").GetString());
    }

    [Fact]
    public void UserMessageException_PreservesStatusCode_AndMessageFields()
    {
        var exception = new UserMessageException(
            409,
            "Errors.Conflict",
            new Dictionary<string, object?>
            {
                ["name"] = "TenantName",
            },
            fallbackMessage: "Fallback conflict text",
            code: "CONFLICT",
            fieldKey: "Fields.Name",
            actionKey: "Actions.Create",
            statusKey: "Status.Conflict");

        var dto = exception.ToDto();

        Assert.Equal(409, exception.StatusCode);
        Assert.False(dto.Success);
        Assert.Equal(UserMessageLevel.error, dto.Level);
        Assert.Equal("Errors.Conflict", dto.MessageKey);
        Assert.Equal("TenantName", dto.MessageArgs?["name"]);
        Assert.Equal("Fallback conflict text", dto.FallbackMessage);
        Assert.Equal("CONFLICT", dto.Code);
        Assert.Equal("Fields.Name", dto.FieldKey);
        Assert.Equal("Actions.Create", dto.ActionKey);
        Assert.Equal("Status.Conflict", dto.StatusKey);
    }
}
