# SDK

服务端 SDK，为模块开发提供便利。

## 包含

- `ModuleBase` - 模块基类
- 扩展方法
- 辅助工具

## 使用方式

```csharp
using ICP.Platform.Sdk;

public class MyModule : ModuleBase
{
    public override string ModuleId => "MyModule";
    public override string DisplayName => "我的模块";

    public override void ConfigureServices(IServiceCollection services)
    {
        // 注册服务
    }
}
```
