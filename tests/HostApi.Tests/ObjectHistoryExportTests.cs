using System.IO.Compression;
using System.Xml.Linq;
using ICP.Platform.Crud.Exports;
using ICP.Platform.Modules.Audit.Contracts;
using ICP.Platform.Modules.Audit.Services;
using ICP.Platform.Presentation;
using Xunit;

namespace ICP.Platform.HostApi.Tests;

public sealed class ObjectHistoryExportTests
{
    [Theory, Trait("Category", "ModuleMetadataConsumer")]
    [InlineData("unavailable", "名称不可用")]
    [InlineData("unrecorded", "未记录")]
    public async Task XlsxTarget_PreservesSafeNameAvailability(string nameSource, string expected)
    {
        var root = Path.GetDirectoryName(typeof(ObjectHistoryExportTests).Assembly.Location)!;
        var catalog = RuntimeTextCatalog.Parse(new ICP.Platform.Kernel.PluginPackages.LocalizationRuntimeManifestBuilder().Build(root));
        var item = new ObjectHistoryItem("record", "part.created", new DateTime(2026, 10, 18, 0, 0, 0, DateTimeKind.Utc), "Part",
            new(null, "unavailable"), new(null, null, nameSource), new("Part.HistoryCreated", new Dictionary<string, string>()), null);
        var exporter = new ObjectHistoryExport(new SafeQuery(item), new StandardCrudXlsxWriter(), catalog, new ChinesePresentation());
        var file = await exporter.ExportAsync("part.part", "object", new("all", new()));
        Assert.NotNull(file);
        using var archive = new ZipArchive(new MemoryStream(file.Content));
        using var stream = archive.GetEntry("xl/worksheets/sheet1.xml")!.Open();
        var xml = XDocument.Load(stream);
        XNamespace spreadsheet = "http://schemas.openxmlformats.org/spreadsheetml/2006/main";
        var targetCell = Assert.Single(xml.Descendants(spreadsheet + "c"), cell => cell.Attribute("r")?.Value == "E2");
        Assert.Equal(expected, string.Concat(targetCell.Descendants(spreadsheet + "t").Select(text => text.Value)));
    }

    private sealed class ChinesePresentation : IRequestPresentationContextAccessor
    {
        public RequestPresentationContext Current => new("zh-CN", "UTC");
    }

    private sealed class SafeQuery(ObjectHistoryItem item) : IObjectHistoryQuery
    {
        public Task<ObjectHistoryPage?> ListAsync(string resourceKey, string objectId, ObjectHistoryFilter filter, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();
        public Task<ObjectHistoryItem?> GetAsync(string resourceKey, string objectId, string recordId, ObjectHistoryFilter filter, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();
        public Task<IReadOnlyList<ObjectHistoryItem>?> OutputAsync(string resourceKey, string objectId, ObjectHistoryOutputRequest request, CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyList<ObjectHistoryItem>?>([item]);
    }
}
