using System.Data.Common;
using System.Net;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Xml.Linq;
using ICP.Platform.Kernel.ModulePlanning;
using ICP.Platform.Modules.Authorization.DTOs;
using ICP.Platform.Persistence.EfCore;
using ICP.Platform.Testing;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace ICP.Platform.HostApi.Tests;

public sealed partial class ExampleExecutionSqlPlanTests
{
    // SQL Server可把空集合折叠为Constant Scan；有实际计划但没有表IO不是观察器失效。
    [EnvironmentGatedFact("NEWP_RUN_ENTITY_PERMISSION_CAPACITY", "核实constant-false查询的零行无表IO计划")]
    public async Task PermissionPlanObservation_RecordsConstantScanWithoutInventingIo()
    {
        await using var database = await ReusableSqlServerDatabase.AcquireAsync();
        var sample = await ExplainAsync(database.ConnectionString, new("SELECT object_id FROM sys.objects WHERE 0 = 1", []), requireIo: false);
        Assert.Equal("constant-scan:rows=0:no-table-io", ReadIo(sample));
    }

    // SQL Server安装语言不同，保留原始IO并只比较表、扫描和逻辑读取，拒绝空解析假通过。
    private static string ReadIo(Measurement sample)
    {
        var rows = sample.Statistics.SelectMany(line => Regex.Matches(line,
            "(?:表“(?<table>[^”]+)”|Table '(?<table>[^']+)').*?(?:扫描计数|Scan count) (?<scan>\\d+).*?(?:逻辑读取次数|logical reads) (?<reads>\\d+)", RegexOptions.IgnoreCase)
            .Cast<Match>()).Select(match => match.Groups["table"].Value + ":" + match.Groups["scan"].Value + ":" + match.Groups["reads"].Value).ToArray();
        if (rows.Length == 0)
        {
            Assert.Empty(sample.Statistics);
            Assert.Equal(0, sample.ReturnedRows);
            var nodes = sample.Plans.SelectMany(plan => XDocument.Parse(plan).Descendants()).ToArray();
            Assert.DoesNotContain(nodes, node => node.Name.LocalName == "Object");
            Assert.Contains(nodes, node => node.Name.LocalName == "RelOp" && node.Attribute("PhysicalOp")?.Value == "Constant Scan");
            return "constant-scan:rows=0:no-table-io";
        }
        return string.Join(";", rows);
    }

    // 排除时长和缓存噪声，比较实际物理算子、对象/索引和运行时读取规模。
    private static string PlanShape(Measurement sample) => string.Join(";", sample.Plans.SelectMany(plan => XDocument.Parse(plan).Descendants())
        .Where(node => node.Name.LocalName is "RelOp" or "Object" or "RunTimeCountersPerThread")
        .Select(node => node.Name.LocalName + ":" + string.Join(",", node.Attributes().Where(attribute => attribute.Name.LocalName is
            "PhysicalOp" or "Schema" or "Table" or "Index" or "ActualRows" or "ActualRowsRead" or "ActualExecutions")
            .OrderBy(attribute => attribute.Name.LocalName, StringComparer.Ordinal).Select(attribute => attribute.Name.LocalName + "=" + attribute.Value))));
}
