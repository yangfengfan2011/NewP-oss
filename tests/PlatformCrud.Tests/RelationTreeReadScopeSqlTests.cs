using ICP.Platform.Crud.Runtime;
using Xunit;

namespace ICP.Platform.Crud.Tests;

public sealed class RelationTreeReadScopeSqlTests
{
    [Fact]
    public void RenameParameters_ChangesOnlySqlParameterTokens()
    {
        const string sql = "SELECT @__value, '@__value', [@__value], \"@__value\" -- @__value\n"
            + "WHERE [Name]='it''s @__value' AND [Id]=@__value /* @__value */ AND [Other]=@__other";
        var renamed = RelationTreeReader.RenameParameters(sql,
            new Dictionary<string, string> { ["@__value"] = "@treeScope0" });

        Assert.Equal("SELECT @treeScope0, '@__value', [@__value], \"@__value\" -- @__value\n"
            + "WHERE [Name]='it''s @__value' AND [Id]=@treeScope0 /* @__value */ AND [Other]=@__other", renamed);
    }
}
