namespace ICP.Platform.Modules.MetadataConfigCenter.Models;

public class MetadataTypeDescriptor
{
    public bool Enabled { get; set; }

    public string SourcePath { get; set; } = string.Empty;

    public string? Adapter { get; set; }

    public MetadataAuthoringOptions? Authoring { get; set; }
}

public class MetadataAuthoringOptions
{
    public bool Editable { get; set; }
}
