using ICP.Platform.Modules.Files.Storage;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using static ICP.Platform.Modules.Files.Services.FileStateStore;

namespace ICP.Platform.Modules.Files.Services;

/// <summary>有界回收独立于业务图；每个 DB 事务退出后才 abort 或等待 I/O gate。</summary>
public sealed class FileCleanupService(IServiceScopeFactory scopes, FileStateStore state, FileIoGate gate,
    IFileContentStore storage, IOptions<FilesOptions> options, ILogger<FileCleanupService> logger)
{
    /// <summary>启动、周期与测试共享一次有界扫描；只清理数据库已登记的键。</summary>
    public async Task<int> RunOnceAsync(CancellationToken token = default)
    {
        var candidates = await state.ReadCleanupCandidatesAsync(options.Value.CleanupBatchSize, token);
        var cleaned = 0;
        foreach (var candidate in candidates)
        {
            token.ThrowIfCancellationRequested();
            var fileId = Text(candidate, "EID");
            var key = Text(candidate, "StorageKey");
            try
            {
                var pending = await InScopeAsync(store => store.TryMarkCleanupPendingAsync(fileId, gate.HasWriter(key), token));
                if (!pending) continue;
                gate.Abort(key);
                using (await gate.EnterAsync(key, token))
                {
                    if (!await InScopeAsync(store => store.MayDeleteAsync(fileId, token))) continue;
                    await storage.DeleteContentAsync(key, token);
                    await InScopeAsync(async store => { await store.MarkDeletedAsync(fileId, token); return true; });
                    cleaned++;
                }
            }
            catch (OperationCanceledException) when (token.IsCancellationRequested) { throw; }
            catch (Exception exception)
            {
                // IO 或提交失败保留 pending；后续运行按持久事实重读，绝不猜测删除状态。
                logger.LogWarning(exception, "Files cleanup deferred for {FileId}.", fileId);
            }
        }
        return cleaned;
    }

    // 每次状态推进都使用新的 tracker，未知提交和重试不会读旧实体快照。
    private async Task<T> InScopeAsync<T>(Func<FileStateStore, Task<T>> action)
    {
        using var scope = scopes.CreateScope();
        return await action(scope.ServiceProvider.GetRequiredService<FileStateStore>());
    }
}
