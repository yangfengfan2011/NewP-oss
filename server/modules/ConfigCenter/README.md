# ConfigCenter Module

配置中心模块 - 平台统一配置管理

## 功能特性

- 配置项统一管理
- 环境级配置隔离（Development、Production 等）
- 租户级配置隔离
- 配置优先级：租户+环境 > 环境 > 租户 > 全局
- 敏感配置保护（密码、密钥等自动脱敏）
- 配置分组管理
- 配置变更审计（待实现）

## 后端 API

### 获取所有配置
```
GET /api/config?environment={env}&tenantId={tenant}
```

### 获取分组配置
```
GET /api/config/grouped?environment={env}&tenantId={tenant}
```

### 获取单个配置
```
GET /api/config/{key}?environment={env}&tenantId={tenant}
```

### 按模块获取配置
```
GET /api/config/module/{moduleId}?environment={env}&tenantId={tenant}
```

### 创建配置
```
POST /api/config
Body: ConfigItemCreateDto
```

### 更新配置
```
PUT /api/config/{id}
Body: ConfigItemUpdateDto
```

### 删除配置
```
DELETE /api/config/{id}
```

## 前端页面

访问路径：`/config/items`

权限要求：`ConfigCenter.Config.View`

## 权限点

- `ConfigCenter.Config.View` - 查看配置
- `ConfigCenter.Config.Create` - 创建配置
- `ConfigCenter.Config.Edit` - 编辑配置
- `ConfigCenter.Config.Delete` - 删除配置

## 配置模型

```csharp
public class ConfigItem
{
    public string Id { get; set; }
    public string Key { get; set; }
    public string? Value { get; set; }
    public string Type { get; set; }
    public string ModuleId { get; set; }
    public string? Group { get; set; }
    public string? Environment { get; set; }
    public string? TenantId { get; set; }
    public string DisplayName { get; set; }
    public string? Description { get; set; }
    public bool IsSensitive { get; set; }
}
```

## 使用示例

### 模块声明配置架构

```csharp
public IReadOnlyList<ConfigDescriptor> GetConfigSchema()
{
    return new[]
    {
        new ConfigDescriptor
        {
            Key = "MyModule.Setting1",
            DisplayName = "设置1",
            Type = "String",
            DefaultValue = "default",
            ModuleId = "MyModule"
        }
    };
}
```

### 读取配置

```csharp
var config = await _configService.GetConfigAsync(
    "MyModule.Setting1", 
    environment: "Production", 
    tenantId: "tenant1"
);
```

## 测试

运行测试：
```bash
dotnet test tests/ConfigCenter.Tests/ConfigCenter.Tests.csproj
```

## 当前实现

- 内存存储（待替换为数据库实现）
- 基础 CRUD 操作
- 环境和租户隔离
- 配置优先级解析
- 敏感配置脱敏

## 参考

参考 `docs/configcenter-module-boundary-v1.md` 了解模块边界定义。
