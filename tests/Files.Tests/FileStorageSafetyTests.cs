using ICP.Platform.Modules.Files.Storage;
using ICP.Platform.Modules.Files;
using Microsoft.Extensions.Configuration;
using Xunit;

namespace ICP.Platform.Tests.Files;

public sealed class FileStorageSafetyTests
{
    [Fact]
    public void OptionsRejectHostStaticRootLinkToPrivateStorageWithoutModifyingDirectories()
    {
        var root = Path.Combine(Path.GetTempPath(), "NewP-Files-Public-Link", Guid.NewGuid().ToString("N"));
        var storage = Path.Combine(root, "private");
        var install = Path.Combine(root, "install");
        var publicRoot = Path.Combine(install, "public");
        Directory.CreateDirectory(storage);
        Directory.CreateDirectory(install);
        var bytes = Path.Combine(storage, "owned.bin");
        File.WriteAllBytes(bytes, [5, 4, 3]);
        try
        {
            CreateDirectoryLink(publicRoot, storage);
            var configuration = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?> { ["Platform:PluginInstallRoot"] = install }).Build();
            Assert.True(new FilesOptionsValidator(configuration: configuration).Validate(null, new() { StorageRoot = storage }).Failed);
            Assert.True(new FilesOptionsValidator(configuration: configuration).Validate(null, new() { StorageRoot = Path.Combine(storage, "child") }).Failed);
            Assert.Equal(new byte[] { 5, 4, 3 }, File.ReadAllBytes(bytes));
            Assert.False(Directory.Exists(Path.Combine(storage, "child")));
        }
        finally { if (Directory.Exists(publicRoot)) Directory.Delete(publicRoot); Directory.Delete(root, true); }
    }
    [Fact]
    public void ConstructorRejectsLinkedRootAndKeepsOutsideContent()
    {
        var root = Path.Combine(Path.GetTempPath(), "NewP-Files-Link", Guid.NewGuid().ToString("N"));
        var outside = Path.Combine(root, "outside");
        var link = Path.Combine(root, "link");
        Directory.CreateDirectory(outside);
        var content = Path.Combine(outside, "owned.bin");
        File.WriteAllBytes(content, [3, 2, 1]);
        try
        {
            CreateDirectoryLink(link, outside);
            Assert.Throws<IOException>(() => new LocalStorageProvider(Path.Combine(link, "private")));
            Assert.True(new FilesOptionsValidator().Validate(null, new() { StorageRoot = link }).Failed);
            Assert.True(new FilesOptionsValidator().Validate(null, new() { StorageRoot = Path.Combine(link, "private") }).Failed);
            Assert.False(Directory.Exists(Path.Combine(outside, "private")));
            Assert.Equal(new byte[] { 3, 2, 1 }, File.ReadAllBytes(content));
        }
        finally
        {
            if (Directory.Exists(link)) Directory.Delete(link);
            Directory.Delete(root, true);
        }
    }

    [Fact]
    public async Task DeleteAsync_RejectsTraversalAndPreservesOutsideFile()
    {
        var root = Path.Combine(Path.GetTempPath(), "NewP-Files-Safety", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        var outside = Path.Combine(root, "outside.bin");
        await File.WriteAllBytesAsync(outside, [1, 2, 3]);
        try
        {
            var provider = new LocalStorageProvider(Path.Combine(root, "private"));
            await Assert.ThrowsAsync<ArgumentException>(() => provider.DeleteAsync("../outside.bin"));
            Assert.Equal(new byte[] { 1, 2, 3 }, await File.ReadAllBytesAsync(outside));
        }
        finally { Directory.Delete(root, true); }
    }

    private static void CreateDirectoryLink(string link, string target)
    {
        if (!OperatingSystem.IsWindows()) { Directory.CreateSymbolicLink(link, target); return; }
        var start = new System.Diagnostics.ProcessStartInfo("pwsh.exe") { UseShellExecute = false, CreateNoWindow = true,
            RedirectStandardOutput = true, RedirectStandardError = true };
        start.ArgumentList.Add("-NoProfile");
        start.ArgumentList.Add("-Command");
        start.ArgumentList.Add("New-Item -ItemType Junction -Path '" + link.Replace("'", "''") + "' -Value '" + target.Replace("'", "''") + "' | Out-Null");
        using var process = System.Diagnostics.Process.Start(start)!;
        process.WaitForExit();
        Assert.Equal(0, process.ExitCode);
    }
}
