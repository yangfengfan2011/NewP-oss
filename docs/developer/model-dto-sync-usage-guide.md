# 模型 DTO 同步

`ModelDtoSync` 从 `modules/<ModuleId>/metadata/models/*.model.json` 读取当前模型，经 SQL Server 类型投影生成：

- `server/modules/<ModuleId>/DTOs/Generated/<Model>Dto.g.cs`
- 同目录的受管 DTO 签名 provider

这些文件是生成物，不接受手工修改。

## 生成

在仓库根目录运行：

```powershell
dotnet run --project .\server\tools\model-dto-sync\ModelDtoSync.csproj -- .
```

修改模型字段、`contract.clrType` 或受管模块范围后，提交模型真源和对应生成物。

## 只读检查

```powershell
dotnet run --project .\server\tools\model-dto-sync\ModelDtoSync.csproj -- --check .
```

`--check` 在临时目录生成期望结果并逐字节比较，不修改仓库；`scripts/verify.ps1` 已包含这一阶段。

## 失败处理

- 元数据校验失败：修正模型 authoring 或 storage type map，不修改生成器输出。
- 生成物漂移：先运行生成命令，检查 diff 是否只反映已确认模型变化。
- 枚举退化为字符串：检查字段 `contract.valueType` 与 `contract.clrType`。
- 出现多余生成文件：确认对应模型是否已删除，再由生成器统一收口。

DTO 对外语义仍由模块 Contracts/API 负责；生成器的字段别名不是修改领域术语或存储协议的入口。
