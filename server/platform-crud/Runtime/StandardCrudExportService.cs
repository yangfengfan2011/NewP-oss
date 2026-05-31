using ICP.Platform.Crud.Abstractions;
using ICP.Platform.Crud.Descriptors;
using ICP.Platform.Crud.Exports;
using ICP.Platform.Crud.Models;

namespace ICP.Platform.Crud.Runtime;

/// <summary>
/// 标准 CRUD 导出内核；这里统一处理模式、字段白名单和文件生成，避免各模块复制导出流程。
/// </summary>
public sealed class StandardCrudExportService
{
    public const int ExportAllPageSize = 100_000;
    public const string XlsxContentType = "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet";

    private readonly IStandardCrudService _service;
    private readonly StandardCrudXlsxWriter _writer;

    public StandardCrudExportService(IStandardCrudService service, StandardCrudXlsxWriter writer)
    {
        _service = service;
        _writer = writer;
    }

    public async Task<StandardCrudExportFile> ExportAsync(
        StandardCrudDescriptor descriptor,
        StandardCrudExportRequest request,
        CancellationToken cancellationToken = default)
    {
        var mode = NormalizeMode(request.Mode);
        var columns = NormalizeColumns(descriptor, request.Columns);
        var query = BuildQuery(request.Query, mode);
        var result = await _service.ListAsync(descriptor, query, cancellationToken);
        var content = _writer.Write("Export", columns, result.Items);

        return new StandardCrudExportFile(
            $"{descriptor.ResourceKey}-{mode}-{DateTimeOffset.UtcNow:yyyyMMddHHmmss}.xlsx",
            XlsxContentType,
            content);
    }

    private static string NormalizeMode(string mode) =>
        mode switch
        {
            "current" => "current",
            "all" => "all",
            _ => throw new ArgumentException("不支持的标准 CRUD 导出模式。", nameof(mode))
        };

    private static IReadOnlyList<StandardCrudExportColumn> NormalizeColumns(
        StandardCrudDescriptor descriptor,
        IReadOnlyList<StandardCrudExportColumn> columns)
    {
        var allowedFields = descriptor.FieldSet.ListFields.ToHashSet(StringComparer.Ordinal);
        var normalized = columns
            .Where(column =>
                !string.IsNullOrWhiteSpace(column.Field)
                && !string.IsNullOrWhiteSpace(column.Title)
                && allowedFields.Contains(column.Field))
            .DistinctBy(column => column.Field, StringComparer.Ordinal)
            .ToList();

        if (normalized.Count == 0)
        {
            throw new ArgumentException("至少需要一个可导出的标准 CRUD 列。", nameof(columns));
        }

        return normalized;
    }

    private static StandardCrudListQuery BuildQuery(StandardCrudExportListQuery? query, string mode)
    {
        var pageIndex = query?.PageIndex > 0 ? query.PageIndex : 1;
        var pageSize = query?.PageSize > 0 ? query.PageSize : 20;

        if (mode == "all")
        {
            pageIndex = 1;
            pageSize = ExportAllPageSize;
        }

        return new StandardCrudListQuery(
            pageIndex,
            pageSize,
            query?.SortBy,
            query?.SortDirection,
            query?.Filters);
    }
}
