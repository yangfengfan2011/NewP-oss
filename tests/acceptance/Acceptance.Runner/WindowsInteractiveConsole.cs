using System.ComponentModel;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.Json;

namespace ICP.Platform.Acceptance;

internal sealed record InteractiveConsoleResult(int ExitCode, string Output, int ProcessId);

/// <summary>
/// 在隐藏的真实 Windows console 中运行拒绝重定向输入输出的正式维护命令。
/// console fixture 只负责键盘确认和屏幕读取，不替换或绕过生产命令的交互门禁。
/// </summary>
internal static class WindowsInteractiveConsole
{
    private const uint CreateNewConsole = 0x00000010;
    private const uint CreateSuspended = 0x00000004;
    private const uint StartfUseShowWindow = 0x00000001;
    private const uint WaitObject0 = 0;
    private const uint WaitTimeout = 258;
    private const uint JobObjectLimitKillOnJobClose = 0x00002000;
    private static readonly TimeSpan ProcessTimeout = TimeSpan.FromMinutes(3);
    private static readonly object EnvironmentLock = new();

    public static async Task<InteractiveConsoleResult> RunAsync(
        string fileName,
        IReadOnlyList<string> arguments,
        string workingDirectory,
        string resultDirectory,
        IReadOnlyDictionary<string, string?> environment,
        CancellationToken cancellationToken)
    {
        if (!OperatingSystem.IsWindows())
        {
            throw new PlatformNotSupportedException("Identity Foundation 的交互式离线命令验收需要 Windows console。");
        }

        var configuration = new DirectoryInfo(AppContext.BaseDirectory).Parent?.Name
            ?? throw new InvalidOperationException("无法解析 acceptance runner 构建配置。");
        var helperDll = Path.Combine(
            workingDirectory,
            "tests",
            "acceptance",
            "Acceptance.InteractiveTerminal",
            "bin",
            configuration,
            "net10.0",
            "Acceptance.InteractiveTerminal.dll");
        if (!File.Exists(helperDll))
        {
            throw new InvalidOperationException("真实控制台 fixture 尚未构建。");
        }

        Directory.CreateDirectory(resultDirectory);
        var resultPath = Path.Combine(resultDirectory, "interactive-console-" + Guid.NewGuid().ToString("N") + ".json");
        var commandLine = new StringBuilder(BuildCommandLine(
            "dotnet.exe",
            new[] { helperDll, "--result", resultPath, "--", fileName }.Concat(arguments).ToArray()));
        var startup = new StartupInfo
        {
            Cb = (uint)Marshal.SizeOf<StartupInfo>(),
            Flags = StartfUseShowWindow,
            ShowWindow = 0
        };
        ProcessInformation process = default;
        var job = IntPtr.Zero;
        var assignedToJob = false;
        var processExited = false;
        try
        {
            job = CreateJobObjectW(IntPtr.Zero, null);
            if (job == IntPtr.Zero)
            {
                throw new Win32Exception(Marshal.GetLastWin32Error(), "无法创建真实控制台验收 Job。");
            }
            var limits = new JobObjectExtendedLimitInformation
            {
                BasicLimitInformation = new JobObjectBasicLimitInformation
                {
                    LimitFlags = JobObjectLimitKillOnJobClose
                }
            };
            ThrowIfFalse(SetInformationJobObject(
                job,
                9,
                ref limits,
                (uint)Marshal.SizeOf<JobObjectExtendedLimitInformation>()));

            lock (EnvironmentLock)
            {
                var previous = environment.ToDictionary(
                    static pair => pair.Key,
                    pair => Environment.GetEnvironmentVariable(pair.Key),
                    StringComparer.OrdinalIgnoreCase);
                try
                {
                    foreach (var pair in environment)
                    {
                        Environment.SetEnvironmentVariable(pair.Key, pair.Value);
                    }
                    ThrowIfFalse(CreateProcessW(
                        null,
                        commandLine,
                        IntPtr.Zero,
                        IntPtr.Zero,
                        false,
                        CreateNewConsole | CreateSuspended,
                        IntPtr.Zero,
                        workingDirectory,
                        startup,
                        out process));
                }
                finally
                {
                    foreach (var pair in previous)
                    {
                        Environment.SetEnvironmentVariable(pair.Key, pair.Value);
                    }
                }
            }

            ThrowIfFalse(AssignProcessToJobObject(job, process.Process));
            assignedToJob = true;
            if (ResumeThread(process.Thread) == uint.MaxValue)
            {
                throw new Win32Exception(Marshal.GetLastWin32Error(), "无法恢复真实控制台验收进程。");
            }
            CloseHandle(process.Thread);
            process.Thread = IntPtr.Zero;

            var deadline = DateTime.UtcNow + ProcessTimeout;
            while (true)
            {
                var wait = WaitForSingleObject(process.Process, 250);
                if (wait == WaitObject0)
                {
                    processExited = true;
                    break;
                }
                if (wait != WaitTimeout)
                {
                    throw new Win32Exception(Marshal.GetLastWin32Error(), "等待真实控制台子进程失败。");
                }
                if (cancellationToken.IsCancellationRequested || DateTime.UtcNow >= deadline)
                {
                    TerminateJobObject(job, cancellationToken.IsCancellationRequested ? 1223u : 124u);
                    cancellationToken.ThrowIfCancellationRequested();
                    throw new TimeoutException($"真实控制台子进程 {process.ProcessId} 超过 {ProcessTimeout.TotalSeconds:0} 秒未退出。");
                }
            }

            ThrowIfFalse(GetExitCodeProcess(process.Process, out _));
            if (!File.Exists(resultPath))
            {
                throw new InvalidOperationException($"真实控制台 fixture {process.ProcessId} 未写入结果。");
            }
            using var result = JsonDocument.Parse(await File.ReadAllTextAsync(resultPath, cancellationToken));
            var root = result.RootElement;
            var interactive = root.GetProperty("interactive").GetBoolean();
            var exitCode = root.GetProperty("exitCode").GetInt32();
            var output = Encoding.UTF8.GetString(Convert.FromBase64String(root.GetProperty("outputBase64").GetString()!));
            File.Delete(resultPath);
            if (!interactive)
            {
                throw new InvalidOperationException("真实控制台 fixture 报告输入或输出仍被重定向。");
            }
            return new InteractiveConsoleResult(exitCode, output, process.ProcessId);
        }
        finally
        {
            if (File.Exists(resultPath)) File.Delete(resultPath);
            if (process.Process != IntPtr.Zero && !assignedToJob && !processExited)
            {
                TerminateProcess(process.Process, 1);
            }
            if (job != IntPtr.Zero) CloseHandle(job);
            if (process.Thread != IntPtr.Zero) CloseHandle(process.Thread);
            if (process.Process != IntPtr.Zero) CloseHandle(process.Process);
        }
    }

    private static string BuildCommandLine(string fileName, IReadOnlyList<string> arguments) =>
        string.Join(' ', new[] { fileName }.Concat(arguments).Select(QuoteArgument));

    private static string QuoteArgument(string value)
    {
        if (value.Length > 0 && value.All(static character => !char.IsWhiteSpace(character) && character != '"'))
        {
            return value;
        }
        var result = new StringBuilder("\"");
        var slashes = 0;
        foreach (var character in value)
        {
            if (character == '\\') { slashes++; continue; }
            if (character == '"')
            {
                result.Append('\\', slashes * 2 + 1).Append(character);
                slashes = 0;
                continue;
            }
            result.Append('\\', slashes).Append(character);
            slashes = 0;
        }
        return result.Append('\\', slashes * 2).Append('"').ToString();
    }

    private static void ThrowIfFalse(bool succeeded)
    {
        if (!succeeded) throw new Win32Exception(Marshal.GetLastWin32Error());
    }

    [StructLayout(LayoutKind.Sequential)]
    private sealed class StartupInfo
    {
        public uint Cb;
        public IntPtr Reserved;
        public IntPtr Desktop;
        public IntPtr Title;
        public int X;
        public int Y;
        public int XSize;
        public int YSize;
        public int XCountChars;
        public int YCountChars;
        public uint FillAttribute;
        public uint Flags;
        public ushort ShowWindow;
        public ushort Reserved2;
        public IntPtr Reserved2Pointer;
        public IntPtr StandardInput;
        public IntPtr StandardOutput;
        public IntPtr StandardError;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct ProcessInformation
    {
        public IntPtr Process;
        public IntPtr Thread;
        public int ProcessId;
        public int ThreadId;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct JobObjectBasicLimitInformation
    {
        public long PerProcessUserTimeLimit;
        public long PerJobUserTimeLimit;
        public uint LimitFlags;
        public UIntPtr MinimumWorkingSetSize;
        public UIntPtr MaximumWorkingSetSize;
        public uint ActiveProcessLimit;
        public UIntPtr Affinity;
        public uint PriorityClass;
        public uint SchedulingClass;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct IoCounters
    {
        public ulong ReadOperationCount;
        public ulong WriteOperationCount;
        public ulong OtherOperationCount;
        public ulong ReadTransferCount;
        public ulong WriteTransferCount;
        public ulong OtherTransferCount;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct JobObjectExtendedLimitInformation
    {
        public JobObjectBasicLimitInformation BasicLimitInformation;
        public IoCounters IoInfo;
        public UIntPtr ProcessMemoryLimit;
        public UIntPtr JobMemoryLimit;
        public UIntPtr PeakProcessMemoryUsed;
        public UIntPtr PeakJobMemoryUsed;
    }

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true, ExactSpelling = true)]
    private static extern bool CreateProcessW(
        string? applicationName,
        StringBuilder commandLine,
        IntPtr processAttributes,
        IntPtr threadAttributes,
        bool inheritHandles,
        uint creationFlags,
        IntPtr environment,
        string currentDirectory,
        StartupInfo startupInfo,
        out ProcessInformation processInformation);

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern IntPtr CreateJobObjectW(IntPtr jobAttributes, string? name);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool SetInformationJobObject(IntPtr job, int informationClass, ref JobObjectExtendedLimitInformation information, uint length);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool AssignProcessToJobObject(IntPtr job, IntPtr process);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool TerminateJobObject(IntPtr job, uint exitCode);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern uint WaitForSingleObject(IntPtr handle, uint milliseconds);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool GetExitCodeProcess(IntPtr process, out uint exitCode);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern uint ResumeThread(IntPtr thread);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool TerminateProcess(IntPtr process, uint exitCode);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool CloseHandle(IntPtr handle);
}
