using ICP.Platform.Modules.Files.Contracts;
using Microsoft.AspNetCore.Http.Features;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Filters;
using Microsoft.AspNetCore.Mvc.ModelBinding;
using Microsoft.Extensions.DependencyInjection;

namespace ICP.Platform.Modules.Workspace;

/// <summary>单个上传 action 在模型绑定前局部放行 100MiB，并禁用全文件表单缓冲。</summary>
[AttributeUsage(AttributeTargets.Method)]
public sealed class WorkspaceDocumentUploadLimitsAttribute : Attribute, IAsyncResourceFilter, IOrderedFilter
{
    public int Order => -4000;

    /// <inheritdoc/>
    public async Task OnResourceExecutionAsync(ResourceExecutingContext context, ResourceExecutionDelegate next)
    {
        var limit = checked(context.HttpContext.RequestServices.GetRequiredService<IFileLifecycleService>().MaxUploadSizeBytes + 131072L);
        var feature = context.HttpContext.Features.Get<IHttpMaxRequestBodySizeFeature>();
        if (feature is { IsReadOnly: false }) feature.MaxRequestBodySize = limit;
        if (context.HttpContext.Request.ContentLength > limit)
        {
            context.Result = new JsonResult(new { code = "Files.UploadTooLarge", reasonKey = "Files.UploadTooLarge" }) { StatusCode = 413 };
            return;
        }
        for (var index = context.ValueProviderFactories.Count - 1; index >= 0; index--)
            if (context.ValueProviderFactories[index] is FormValueProviderFactory or FormFileValueProviderFactory or JQueryFormValueProviderFactory)
                context.ValueProviderFactories.RemoveAt(index);
        await next();
    }
}
