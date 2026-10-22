using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.Json;

if (!OperatingSystem.IsWindows()
    || args.Length < 4
    || !string.Equals(args[0], "--result", StringComparison.Ordinal)
    || !string.Equals(args[2], "--", StringComparison.Ordinal))
{
    return 64;
}

var resultPath = Path.GetFullPath(args[1]);
var command = args[3];
var commandArguments = args.Skip(4).ToArray();
var interactive = Environment.UserInteractive
    && !Console.IsInputRedirected
    && !Console.IsOutputRedirected;
if (!interactive)
{
    await WriteResultAsync(resultPath, 65, string.Empty, false);
    return 65;
}

TryExpandBuffer();
QueueConfirmation();
using var process = new Process
{
    StartInfo = new ProcessStartInfo
    {
        FileName = command,
        UseShellExecute = false,
        WorkingDirectory = Environment.CurrentDirectory
    }
};
foreach (var argument in commandArguments)
{
    process.StartInfo.ArgumentList.Add(argument);
}

if (!process.Start())
{
    await WriteResultAsync(resultPath, 66, string.Empty, true);
    return 66;
}
await process.WaitForExitAsync();
await Task.Delay(100);
var output = ReadConsoleBuffer();
await WriteResultAsync(resultPath, process.ExitCode, output, true);
return process.ExitCode;

static void TryExpandBuffer()
{
    try
    {
        Console.SetBufferSize(Math.Max(Console.BufferWidth, 160), Math.Max(Console.BufferHeight, 2000));
    }
    catch
    {
        // 新控制台在完全初始化前可能暂时拒绝调大；默认缓冲区足够承载维护命令输出。
    }
}

static void QueueConfirmation()
{
    var input = NativeMethods.GetStdHandle(-10);
    var records = new[]
    {
        InputRecord.Key('y', 0x59, true),
        InputRecord.Key('y', 0x59, false),
        InputRecord.Key('\r', 0x0d, true),
        InputRecord.Key('\r', 0x0d, false)
    };
    if (!NativeMethods.WriteConsoleInputW(input, records, (uint)records.Length, out var written)
        || written != records.Length)
    {
        throw new InvalidOperationException("无法向真实控制台输入缓冲区写入确认键。");
    }
}

static string ReadConsoleBuffer()
{
    var output = NativeMethods.GetStdHandle(-11);
    if (!NativeMethods.GetConsoleScreenBufferInfo(output, out var info))
    {
        throw new InvalidOperationException("无法读取真实控制台屏幕缓冲区状态。");
    }
    var lines = new List<string>();
    var buffer = new StringBuilder(info.Size.X);
    for (short row = 0; row <= info.CursorPosition.Y; row++)
    {
        buffer.Clear();
        buffer.EnsureCapacity(info.Size.X);
        if (!NativeMethods.ReadConsoleOutputCharacterW(output, buffer, (uint)info.Size.X, new Coord(0, row), out var read))
        {
            throw new InvalidOperationException("无法读取真实控制台屏幕缓冲区。");
        }
        lines.Add(buffer.ToString(0, checked((int)read)).TrimEnd());
    }
    return string.Join('\n', lines).Trim();
}

static Task WriteResultAsync(string path, int exitCode, string output, bool interactive) =>
    File.WriteAllTextAsync(path, JsonSerializer.Serialize(new
    {
        exitCode,
        outputBase64 = Convert.ToBase64String(Encoding.UTF8.GetBytes(output)),
        interactive
    }));

[StructLayout(LayoutKind.Sequential)]
internal readonly struct Coord(short x, short y)
{
    public readonly short X = x;
    public readonly short Y = y;
}

[StructLayout(LayoutKind.Sequential)]
internal struct ConsoleScreenBufferInfo
{
    public Coord Size;
    public Coord CursorPosition;
    public ushort Attributes;
    public SmallRect Window;
    public Coord MaximumWindowSize;
}

[StructLayout(LayoutKind.Sequential)]
internal struct SmallRect
{
    public short Left;
    public short Top;
    public short Right;
    public short Bottom;
}

[StructLayout(LayoutKind.Explicit, CharSet = CharSet.Unicode)]
internal struct InputRecord
{
    [FieldOffset(0)] public ushort EventType;
    [FieldOffset(4)] public KeyEventRecord KeyEvent;

    public static InputRecord Key(char character, ushort virtualKey, bool down) => new()
    {
        EventType = 0x0001,
        KeyEvent = new KeyEventRecord
        {
            KeyDown = down,
            RepeatCount = 1,
            VirtualKeyCode = virtualKey,
            UnicodeChar = character
        }
    };
}

[StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
internal struct KeyEventRecord
{
    [MarshalAs(UnmanagedType.Bool)] public bool KeyDown;
    public ushort RepeatCount;
    public ushort VirtualKeyCode;
    public ushort VirtualScanCode;
    public char UnicodeChar;
    public uint ControlKeyState;
}

internal static partial class NativeMethods
{
    [DllImport("kernel32.dll")]
    public static extern IntPtr GetStdHandle(int standardHandle);

    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static extern bool WriteConsoleInputW(IntPtr input, InputRecord[] buffer, uint length, out uint written);

    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static extern bool GetConsoleScreenBufferInfo(IntPtr output, out ConsoleScreenBufferInfo info);

    [DllImport("kernel32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static extern bool ReadConsoleOutputCharacterW(IntPtr output, StringBuilder characters, uint length, Coord coordinate, out uint read);
}
