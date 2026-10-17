using ICP.Platform.Authorization.Contracts;
using Microsoft.AspNetCore.Http;

namespace ICP.Platform.Authorization.Http;

/// <summary>HTTP 是旧 native 及显式服务的共同默认观察边界；提交后的对外读由消费者开启新观察。</summary>
public sealed class DataScopeEvaluationMiddleware(RequestDelegate next)
{
    /// <summary>请求结束或失败均释放观察；不会修改认证主体或吞掉执行异常。</summary>
    public async Task InvokeAsync(HttpContext context, IDataScopeEvaluationScope evaluation)
    {
        using var operation = evaluation.BeginOperation();
        await next(context);
    }
}
