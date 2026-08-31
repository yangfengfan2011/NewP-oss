using Xunit;

// 每个用例都会 spawn pwsh；并行执行会在 Windows 上争用同名 shim。
// 本程序集内串行，程序集之间仍可并行——这正是把脚本测试单独拆出来的目的。
[assembly: CollectionBehavior(DisableTestParallelization = true)]
