using ICP.Platform.Crud.Abstractions;
using ICP.Platform.Crud.Descriptors;
using ICP.Platform.Crud.Exports;
using ICP.Platform.Crud.Models;
using Microsoft.Extensions.Configuration;

namespace ICP.Platform.Crud.Runtime;

/// <summary>
/// 标准 CRUD 导出内核；这里统一处理模式、字段白名单和文件生成，避免各模块复制导出流程。
/// </summary>
public sealed class StandardCrudExportService
{
    public const int DefaultMaxRows = 10_000;
    public const string XlsxContentType = "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet";

    private readonly IStandardCrudService _service;
    private readonly StandardCrudXlsxWriter _writer;
    private readonly int _maxRows;

    /// <summary>
    /// 创建导出服务，并从平台配置读取同步导出上限。
    /// </summary>
    public StandardCrudExportService(
        IStandardCrudService service,
        StandardCrudXlsxWriter writer,
        IConfiguration configuration)
    {
        _service = service;
        _writer = writer;
        _maxRows = configuration.GetValue<int?>("Platform:StandardCrud:Export:MaxRows") is > 0 and var configured
            ? configured
            : DefaultMaxRows;
    }

    /// <summary>
    /// 按请求中的已提交查询和可见业务列生成同步 XLSX 文件。
    /// </summary>
    public async Task<StandardCrudExportFile> ExportAsync(
        StandardCrudDescriptor descriptor,
        StandardCrudExportRequest request,
        CancellationToken cancellationToken = default)
    {
        var mode = NormalizeMode(request.Mode);
        var columns = NormalizeColumns(descriptor, request.Columns);
        var query = BuildQuery(request.Query, mode, columns, _maxRows);
        var result = await _service.ListAsync(descriptor, query, cancellationToken);
        if (mode == "all" && result.Total > _maxRows)
        {
            throw new StandardCrudExportLimitExceededException(_maxRows);
        }
        var content = _writer.Write("Export", columns, result.Items);

        return new StandardCrudExportFile(
            $"{descriptor.ResourceKey}-{mode}-{DateTimeOffset.UtcNow:yyyyMMddHHmmss}.xlsx",
            XlsxContentType,
            content);
    }

    // 导出范围只接受公开契约中的两个稳定值。
    private static string NormalizeMode(string mode) =>
        mode switch
        {
            "current" => "current",
            "all" => "all",
            _ => throw new ArgumentException("不支持的标准 CRUD 导出模式。", nameof(mode))
        };

    // 列投影按 descriptor 白名单收口，并保留调用方提交的可见顺序。
    private static IReadOnlyList<StandardCrudExportColumn> NormalizeColumns(
        StandardCrudDescriptor descriptor,
        IReadOnlyList<StandardCrudExportColumn> columns)
    {
        var allowedFields = descriptor.FieldSet.ListFields
            .Concat(descriptor.DisplayValueFields.Select(static field => field.Field))
            .ToHashSet(StringComparer.Ordinal);
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

    // current 继承当前分页；all 从第一页读取上限加一行，以便可靠判定超限。
    private static StandardCrudListQuery BuildQuery(
        StandardCrudExportListQuery? query,
        string mode,
        IReadOnlyList<StandardCrudExportColumn> columns,
        int maxRows)
    {
        var pageIndex = query?.PageIndex > 0 ? query.PageIndex : 1;
        var pageSize = query?.PageSize > 0 ? query.PageSize : 20;

        if (mode == "all")
        {
            pageIndex = 1;
            pageSize = checked(maxRows + 1);
        }
        else if (pageSize > maxRows)
        {
            throw new StandardCrudExportLimitExceededException(maxRows);
        }

        return new StandardCrudListQuery(
            pageIndex,
            pageSize,
            query?.SortBy,
            query?.SortDirection,
            query?.Filters,
            columns.Select(static column => column.Field).Distinct(StringComparer.Ordinal).ToList());
    }
}
