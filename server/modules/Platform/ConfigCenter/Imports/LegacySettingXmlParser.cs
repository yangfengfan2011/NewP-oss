using System.Xml.Linq;

namespace ICP.Platform.Modules.ConfigCenter.Imports;

public class LegacySettingXmlParser : ILegacySettingParser
{
    public IEnumerable<LegacySettingImportItem> Parse(string xmlContent, string sourceFileName)
    {
        var doc = XDocument.Parse(xmlContent);
        var group = Path.GetFileNameWithoutExtension(sourceFileName);

        var appSetNodes = doc.Descendants("AppSet");

        foreach (var node in appSetNodes)
        {
            var key = node.Attribute("Key")?.Value;

            if (string.IsNullOrWhiteSpace(key))
            {
                throw new InvalidOperationException("Key attribute is missing or empty in AppSet node");
            }

            yield return new LegacySettingImportItem
            {
                Key = key,
                Value = node.Attribute("Value")?.Value ?? string.Empty,
                ToClient = bool.TryParse(node.Attribute("ToClient")?.Value, out var toClient) && toClient,
                Description = node.Attribute("Description")?.Value ?? string.Empty,
                Group = group,
                SourceFile = sourceFileName
            };
        }
    }
}
