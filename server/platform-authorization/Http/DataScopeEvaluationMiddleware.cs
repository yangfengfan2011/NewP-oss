using ICP.Platform.Authorization.Contracts;
using ICP.Platform.Persistence.Transactions;
using Microsoft.AspNetCore.Http;

namespace ICP.Platform.Authorization.Http;

/// <summary>HTTP 是旧 native 及显式服务的共同默认观察边界；提交后的对外读由消费者开启新观察。</summary>
public sealed class DataScopeEvaluationMiddleware(RequestDelegate next)
{
    /// <summary>请求结束均释放观察；缓存故障统一表达暂不可用，其他故障和取消继续传播。</summary>
    public async Task InvokeAsync(HttpContext context, IDataScopeEvaluationScope evaluation)
    {
        using var operation = evaluation.BeginOperation();
        try { await next(context); }
        catch (Exception exception) when (!context.Response.HasStarted && CacheFailure(exception) is { } unavailable)
        {
            await Results.Problem(statusCode: StatusCodes.Status503ServiceUnavailable, title: unavailable.Code, detail: unavailable.Message,
                extensions: new Dictionary<string, object?>
                {
                    ["code"] = unavailable.Code, ["message"] = unavailable.Message, ["reasonKey"] = unavailable.ReasonKey,
                    ["traceId"] = context.TraceIdentifier, ["databaseCommitted"] = unavailable.DatabaseCommitted,
                    ["commitOutcomeUnknown"] = unavailable.CommitOutcomeUnknown
                }).ExecuteAsync(context);
        }
    }

    // 批量可读保留专用执行异常供目录消费者拒绝降级，HTTP 仍识别其中的稳定缓存故障。
    private static PlatformCacheUnavailableException? CacheFailure(Exception exception) => exception switch
    {
        PlatformCacheUnavailableException unavailable => unavailable,
        DataScopeReadExecutionException { InnerException: { } inner } => CacheFailure(inner),
        _ => null
    };
}
