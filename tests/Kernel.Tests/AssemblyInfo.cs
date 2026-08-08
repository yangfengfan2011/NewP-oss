using Xunit;

// 多组 Kernel 测试会启动 PowerShell、Host 或打包子进程；类级并行会在 Windows 上争用子进程运行时。
[assembly: CollectionBehavior(DisableTestParallelization = true)]
