namespace ICP.Platform.Modules.MetadataConfigCenter.Models;

public class MetadataTypeDescriptor
{
    public bool Enabled { get; set; } = true;

    public string Type { get; set; } = string.Empty;

    public string SourcePath { get; set; } = string.Empty;

    public List<string> SourceFiles { get; set; } = [];

    public string? Adapter { get; set; }

    public string MergeMode { get; set; } = "replace";

    public string SchemaVersion { get; set; } = "1.0";

    public MetadataAuthoringOptions? Authoring { get; set; }
}

public class MetadataAuthoringOptions
{
    public bool Editable { get; set; }
}
