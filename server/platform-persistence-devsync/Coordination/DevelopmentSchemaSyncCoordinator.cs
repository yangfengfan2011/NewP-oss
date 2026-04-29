using ICP.Platform.Persistence.DevSync.Database;
using ICP.Platform.Persistence.DevSync.Execution;
using ICP.Platform.Persistence.DevSync.Models;
using ICP.Platform.Persistence.DevSync.Planning;
using ICP.Platform.Persistence.DevSync.Schema;

namespace ICP.Platform.Persistence.DevSync.Coordination;

/// <summary>
/// 共享开发态结构同步的执行结果。
/// Host 应以 <see cref="StartupAllowed"/> 作为是否继续启动的唯一放行信号，并用 <see cref="FailureReason"/> 定位阻断原因。
/// </summary>
/// <param name="StartupAllowed">开发态启动是否允许继续。</param>
/// <param name="FailureReason">阻断原因代码；为空表示检查通过。</param>
/// <param name="ExitCode">CLI 可直接使用的退出码。</param>
/// <param name="Inventory">模型真源清单检查结果；预清单失败时用于输出缺失模型。</param>
/// <param name="SignatureMismatches">基础 DTO 签名不一致的对象名称。</param>
/// <param name="Plan">结构同步计划；预计划阶段失败时可能为空，JSON 输出层会转换为稳定空计划。</param>
/// <param name="DatabaseTableCount">读取到的数据库表数量。</param>
/// <param name="ApplyResult">执行结果；未进入执行阶段时为空。</param>
/// <param name="Diagnostics">给 Host/CLI 输出的失败诊断，例如异常类型和消息。</param>
public sealed record DevelopmentSchemaSyncResult(
    bool StartupAllowed,
    string? FailureReason,
    int ExitCode,
    ModelSourceInventoryResult? Inventory,
    IReadOnlyList<string> SignatureMismatches,
    SchemaPlan? Plan,
    int DatabaseTableCount,
    ApplyResult? ApplyResult,
    IReadOnlyList<string> Diagnostics)
{
    /// <summary>
    /// 构造面向 Host/CLI 的失败说明；调用方负责选择抛异常、退出码或文本输出。
    /// </summary>
    public string BuildMessage()
    {
        if (StartupAllowed)
        {
            return "开发态结构同步检查通过。";
        }

        return FailureReason switch
        {
            "ModelSourcesMissing" => $"模型真源缺失: {string.Join(", ", Inventory?.MissingModels ?? [])}",
            "BaseDtoSignaturesMismatch" => $"基础 DTO 签名不一致: {string.Join(", ", SignatureMismatches)}",
            "ConfirmOperationsDetected" => "结构同步计划包含 Confirm 级高风险操作，已阻止自动执行。",
            "ApplyCancelled" => "结构同步执行已取消，已阻止启动。",
            "ApplyFailed" => Diagnostics.Count == 0
                ? "结构同步执行失败，已阻止启动。"
                : $"结构同步执行失败，已阻止启动。诊断: {string.Join("; ", Diagnostics)}",
            _ => $"开发态结构同步失败: {FailureReason}"
        };
    }
}

/// <summary>
/// 协调模型真源清单、基础 DTO 签名校验、schema diff 与可选 SQL 执行。
/// 该协调器不依赖 Console 或 CLI 参数解析，供 CLI 与后续 Host 开发态启动检查复用。
/// </summary>
public sealed class DevelopmentSchemaSyncCoordinator
{
    private readonly ModelSourceInventory _inventory;
    private readonly Func<CancellationToken, Task<BaseDtoSignatureVerificationResult>> _verifySignaturesAsync;
    private readonly Func<CancellationToken, Task<SchemaPlanningResult>> _buildPlanAsync;
    private readonly Func<SchemaPlan, CancellationToken, Task<ApplyResult>> _applyAsync;
    private readonly Func<SchemaPlan, bool> _confirmApply;
    private readonly bool _applyChanges;

    private DevelopmentSchemaSyncCoordinator(
        ModelSourceInventory inventory,
        Func<CancellationToken, Task<BaseDtoSignatureVerificationResult>> verifySignaturesAsync,
        Func<CancellationToken, Task<SchemaPlanningResult>> buildPlanAsync,
        Func<SchemaPlan, CancellationToken, Task<ApplyResult>> applyAsync,
        Func<SchemaPlan, bool> confirmApply,
        bool applyChanges)
    {
        _inventory = inventory;
        _verifySignaturesAsync = verifySignaturesAsync;
        _buildPlanAsync = buildPlanAsync;
        _applyAsync = applyAsync;
        _confirmApply = confirmApply ?? throw new ArgumentNullException(nameof(confirmApply));
        _applyChanges = applyChanges;
    }

    /// <summary>
    /// 生产入口：按“真源清单 -> DTO 签名 -> schema diff -> 高风险阻断 -> 可选执行”的顺序运行。
    /// </summary>
    public static DevelopmentSchemaSyncCoordinator Create(DevelopmentSchemaSyncOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);
        options.Validate();

        return new DevelopmentSchemaSyncCoordinator(
            ModelSourceInventory.Create(options),
            _ => Task.FromResult(new BaseDtoSignatureVerifier().VerifyRepository(options.RepositoryRoot)),
            cancellationToken => BuildPlanAsync(options, cancellationToken),
            (plan, cancellationToken) => ApplySafePlanAsync(options, plan, cancellationToken),
            options.ConfirmApply ?? (static _ => false),
            options.ApplyChanges);
    }

    /// <summary>
    /// 测试入口只替换外部依赖，不改变协调器内部的阻断顺序和状态判断。
    /// </summary>
    public static DevelopmentSchemaSyncCoordinator CreateForTests(
        ModelSourceInventory inventory,
        bool signaturesMatch,
        SchemaPlan plan,
        ApplyResult? applyResult = null,
        Exception? applyException = null,
        Func<SchemaPlan, bool>? applyConfirmation = null,
        Func<SchemaPlan, CancellationToken, Task<ApplyResult>>? applyAsync = null)
    {
        ArgumentNullException.ThrowIfNull(inventory);
        ArgumentNullException.ThrowIfNull(plan);

        var applyChanges = applyResult is not null
            || applyException is not null
            || applyConfirmation is not null
            || applyAsync is not null;

        return new DevelopmentSchemaSyncCoordinator(
            inventory,
            _ => Task.FromResult(new BaseDtoSignatureVerificationResult(
                signaturesMatch,
                signaturesMatch ? [] : ["BaseDto"])),
            _ => Task.FromResult(new SchemaPlanningResult(plan, 0)),
            applyAsync ?? ((_, _) => applyException is not null
                ? Task.FromException<ApplyResult>(applyException)
                : Task.FromResult(applyResult ?? new ApplyResult(true, false, 0, 0))),
            applyConfirmation ?? (static _ => false),
            applyChanges);
    }

    /// <summary>
    /// 仅准备共享同步计划，不触发确认委托或数据库执行。
    /// CLI 使用该阶段先输出计划和 SQL preview，Host 也可在启动前先取得完整失败信号。
    /// </summary>
    public async Task<DevelopmentSchemaSyncResult> PrepareAsync(CancellationToken cancellationToken = default)
    {
        var inventory = await _inventory.CheckAsync(cancellationToken);
        if (!inventory.Success)
        {
            return Blocked("ModelSourcesMissing", inventory, [], null, 0, null);
        }

        var signatures = await _verifySignaturesAsync(cancellationToken);
        if (!signatures.Success)
        {
            return Blocked("BaseDtoSignaturesMismatch", inventory, signatures.Mismatches, null, 0, null);
        }

        var planning = await _buildPlanAsync(cancellationToken);
        var confirmOperations = planning.Plan.Operations
            .Where(static operation => string.Equals(operation.RiskLevel, RiskLevel.Confirm, StringComparison.Ordinal))
            .ToArray();

        if (confirmOperations.Length > 0)
        {
            // 任务 7 阶段统一禁止 Confirm 风险进入执行层；任务 8 若要放开，必须重新设计显式安全策略。
            return Blocked("ConfirmOperationsDetected", inventory, [], planning.Plan, planning.DatabaseTableCount, null);
        }

        return new DevelopmentSchemaSyncResult(
            true,
            null,
            0,
            inventory,
            [],
            planning.Plan,
            planning.DatabaseTableCount,
            null,
            []);
    }

    /// <summary>
    /// 对已准备且未阻断的计划执行可选 apply；调用方必须先完成计划/SQL 输出，再进入本方法触发确认。
    /// </summary>
    public async Task<DevelopmentSchemaSyncResult> ExecutePreparedAsync(
        DevelopmentSchemaSyncResult prepared,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(prepared);

        if (!prepared.StartupAllowed)
        {
            return prepared;
        }

        var plan = prepared.Plan
            ?? throw new InvalidOperationException("开发态结构同步未返回结构计划。");

        ApplyResult? applyResult = null;
        if (_applyChanges)
        {
            // CLI 可以在这里注入交互确认；Host/非交互路径必须显式注入确认策略，否则默认拒绝执行。
            if (!_confirmApply(plan))
            {
                applyResult = new ApplyResult(false, true, 0, 0);
                return Blocked("ApplyCancelled", prepared.Inventory, [], plan, prepared.DatabaseTableCount, applyResult, 4);
            }

            try
            {
                applyResult = await _applyAsync(plan, cancellationToken);
            }
            catch (Exception exception)
            {
                return Blocked(
                    "ApplyFailed",
                    prepared.Inventory,
                    [],
                    plan,
                    prepared.DatabaseTableCount,
                    null,
                    4,
                    [$"{exception.GetType().Name}: {exception.Message}"]);
            }

            if (applyResult.Cancelled)
            {
                return Blocked("ApplyCancelled", prepared.Inventory, [], plan, prepared.DatabaseTableCount, applyResult, 4);
            }

            if (!applyResult.Applied)
            {
                return Blocked("ApplyFailed", prepared.Inventory, [], plan, prepared.DatabaseTableCount, applyResult, 4);
            }
        }

        return prepared with { ApplyResult = applyResult };
    }

    /// <summary>
    /// 执行共享同步内核；任何不一致或 Confirm 风险都会返回阻断结果，不向下游执行器放行。
    /// </summary>
    public async Task<DevelopmentSchemaSyncResult> ExecuteAsync(CancellationToken cancellationToken = default)
    {
        var prepared = await PrepareAsync(cancellationToken);
        return await ExecutePreparedAsync(prepared, cancellationToken);
    }

    private static DevelopmentSchemaSyncResult Blocked(
        string reason,
        ModelSourceInventoryResult? inventory,
        IReadOnlyList<string> signatureMismatches,
        SchemaPlan? plan,
        int databaseTableCount,
        ApplyResult? applyResult,
        int exitCode = 3,
        IReadOnlyList<string>? diagnostics = null)
    {
        return new DevelopmentSchemaSyncResult(
            false,
            reason,
            exitCode,
            inventory,
            signatureMismatches,
            plan,
            databaseTableCount,
            applyResult,
            diagnostics ?? []);
    }

    private static async Task<SchemaPlanningResult> BuildPlanAsync(
        DevelopmentSchemaSyncOptions options,
        CancellationToken cancellationToken)
    {
        var discoveredModels = new ModelDiscoveryService(new SingleRootModelDocumentLoader())
            .DiscoverAll(options.RepositoryRoot);
        var targetSchema = new TargetSchemaBuilder(options.RepositoryRoot).Build(discoveredModels);
        var databaseSchema = await new SqlServerSchemaReader().ReadAsync(options.ConnectionString, cancellationToken);
        var plan = new SchemaDiffEngine(new RiskClassifier()).BuildPlan(targetSchema, databaseSchema.Tables);

        if (options.GenerateSqlPreview || options.ApplyChanges)
        {
            plan = new SchemaSqlGenerator().PopulateSqlPreview(plan, targetSchema, databaseSchema.Tables);
        }

        return new SchemaPlanningResult(plan, databaseSchema.Tables.Count);
    }

    private static Task<ApplyResult> ApplySafePlanAsync(
        DevelopmentSchemaSyncOptions options,
        SchemaPlan plan,
        CancellationToken cancellationToken)
    {
        var executor = SchemaApplyExecutor.CreateForSqlServer(
            options.ConnectionString,
            // 整体确认已由 coordinator 的 ConfirmApply 委托完成，执行器这里只保留 SQL 执行职责。
            _ => true,
            _ => false);

        return executor.ApplyAsync(plan, cancellationToken);
    }

    private sealed record SchemaPlanningResult(SchemaPlan Plan, int DatabaseTableCount);
}
