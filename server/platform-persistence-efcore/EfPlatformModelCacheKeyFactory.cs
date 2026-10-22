using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;

namespace ICP.Platform.Persistence.EfCore;

/// <summary>
/// 让 EF 在不同模型定义集合下生成独立缓存键，避免动态投影模型被错误复用。
/// </summary>
public sealed class EfPlatformModelCacheKeyFactory : IModelCacheKeyFactory
{
    public object Create(DbContext context, bool designTime)
    {
        if (context is EfPlatformDbContext platformDbContext)
        {
            return (context.GetType(), platformDbContext.ModelCacheSignature, designTime);
        }

        return (context.GetType(), designTime);
    }
}
