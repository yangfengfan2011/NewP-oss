using ICP.Platform.Modules.Organization.DTOs;
using ICP.Platform.Modules.Organization.DTOs.Requests;
using ICP.Platform.Persistence.Abstractions;

namespace ICP.Platform.Modules.Organization.Services;

/// <summary>拥有人员账号绑定用例，查询与写入复用同一业务核心。</summary>
public sealed class PersonUserBindingService : IPersonUserBindingService
{
    private readonly PersonUserBindingActionCore _core;
    private readonly IPlatformTransactionExecutor _transactionExecutor;

    // 服务入口负责事务协调，核心保持规则与持久化实现的唯一来源。
    internal PersonUserBindingService(
        PersonUserBindingActionCore core,
        IPlatformTransactionExecutor transactionExecutor)
    {
        _core = core;
        _transactionExecutor = transactionExecutor;
    }

    // 候选查询复用内部绑定核心的人员及账号规则。
    public ValueTask<IReadOnlyList<PersonUserBindingCandidateDto>> SearchCandidatesAsync(
        string personEid,
        string? query,
        CancellationToken cancellationToken = default) =>
        _core.SearchCandidatesAsync(personEid, query, cancellationToken);

    /// <summary>在组织写协调和共享事务内永久绑定人员及账号，提交后统一发布完整人员结果。</summary>
    public async ValueTask<PersonUserBindingDto> BindAsync(
        string personEid,
        BindPersonUserRequest request,
        PersonUserBindingActor actor,
        CancellationToken cancellationToken = default)
    {
        return await _transactionExecutor.ExecuteAsync(
            ["Person"],
            async token => (await _core.ExecuteAsync(personEid, request, actor, token)).Dto,
            cancellationToken);
    }
}
