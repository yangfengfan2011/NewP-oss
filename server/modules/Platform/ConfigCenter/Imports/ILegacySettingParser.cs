namespace ICP.Platform.Modules.ConfigCenter.Imports;

public interface ILegacySettingParser
{
    IEnumerable<LegacySettingImportItem> Parse(string xmlContent, string sourceFileName);
}
