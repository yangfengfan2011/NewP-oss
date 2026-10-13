namespace ICP.Platform.Modules.ConfigCenter.Imports;

public interface ILegacySettingImportService
{
    Task<LegacySettingImportResult> ImportAsync(LegacySettingImportRequest request, string? importedBy = null, CancellationToken cancellationToken = default);
}
