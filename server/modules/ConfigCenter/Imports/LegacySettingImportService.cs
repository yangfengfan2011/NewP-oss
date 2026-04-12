using ICP.Platform.Modules.ConfigCenter.Models;
using ICP.Platform.Modules.ConfigCenter.Repositories;

namespace ICP.Platform.Modules.ConfigCenter.Imports;

public class LegacySettingImportService : ILegacySettingImportService
{
    private readonly ILegacySettingParser _parser;
    private readonly IConfigRepository _repository;

    public LegacySettingImportService(ILegacySettingParser parser, IConfigRepository repository)
    {
        _parser = parser;
        _repository = repository;
    }

    public async Task<LegacySettingImportResult> ImportAsync(
        LegacySettingImportRequest request, 
        string? importedBy = null, 
        CancellationToken cancellationToken = default)
    {
        var result = new LegacySettingImportResult();

        try
        {
            var items = _parser.Parse(request.XmlContent, request.SourceFileName).ToList();
            result.TotalCount = items.Count;

            foreach (var item in items)
            {
                try
                {
                    var existing = await _repository.GetByKeyAsync(
                        item.Key, 
                        request.Environment, 
                        request.TenantId, 
                        cancellationToken);

                    if (existing == null)
                    {
                        var newConfig = new ConfigItem
                        {
                            Key = item.Key,
                            Value = item.Value,
                            Type = InferType(item.Value),
                            ModuleId = request.ModuleId,
                            Group = item.Group,
                            Environment = request.Environment,
                            TenantId = request.TenantId,
                            DisplayName = item.Key,
                            Description = item.Description,
                            IsSensitive = false,
                            CreatedAt = DateTime.UtcNow,
                            UpdatedAt = DateTime.UtcNow,
                            UpdatedBy = importedBy
                        };

                        await _repository.CreateAsync(newConfig, cancellationToken);
                        result.CreatedCount++;
                    }
                    else
                    {
                        // 导入只收口到 ConfigItem 值层，保留既有的模块/分组/展示信息。
                        // Definition-layer metadata stays owned by module metadata files.
                        existing.Value = item.Value;
                        existing.UpdatedAt = DateTime.UtcNow;
                        existing.UpdatedBy = importedBy;

                        await _repository.UpdateAsync(existing, cancellationToken);
                        result.UpdatedCount++;
                    }
                }
                catch (Exception ex)
                {
                    result.Errors.Add($"Failed to import key '{item.Key}': {ex.Message}");
                }
            }
        }
        catch (Exception ex)
        {
            result.Errors.Add($"Failed to parse XML: {ex.Message}");
        }

        return result;
    }

    private static string InferType(string value)
    {
        if (bool.TryParse(value, out _))
            return "Bool";
        
        if (int.TryParse(value, out _))
            return "Int";
        
        return "String";
    }
}
