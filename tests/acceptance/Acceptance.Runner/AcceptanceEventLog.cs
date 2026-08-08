using System.Text;
using System.Text.Json;

namespace ICP.Platform.Acceptance;

public sealed class AcceptanceEventLog(string path)
{
    public async Task AppendAsync(
        AcceptanceObservation observation,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(observation);

        var directory = Path.GetDirectoryName(path)
            ?? throw new InvalidOperationException("事件文件必须具有父目录。");
        Directory.CreateDirectory(directory);

        var json = JsonSerializer.Serialize(observation, AcceptanceJson.CompactOptions);
        await using var stream = new FileStream(
            path,
            FileMode.Append,
            FileAccess.Write,
            FileShare.Read,
            bufferSize: 4096,
            useAsync: true);
        await using var writer = new StreamWriter(stream, new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));
        await writer.WriteLineAsync(json.AsMemory(), cancellationToken);
    }
}
