# 模型 DTO 生成工具使用指南

本文档面向仓库内部开发者，说明 `model-dto-sync` 的用途、入口、生成物边界和验证方式。

## 1. 这个工具解决什么问题

`model-dto-sync` 用于把当前平台模型真源投影为模块基础 DTO。

当前模型真源来自：

- `modules/**/metadata/models/*.model.json`
- `PlatformPersistenceModelCatalog` 当前纳入的模型定义

工具会生成：

- 模块基础 DTO：`server/modules/<ModuleId>/DTOs/Generated/*Dto.g.cs`
- DTO 签名 provider：`server/modules/<ModuleId>/DTOs/Generated/*BaseDtoSignatureProvider.g.cs`

这些生成物用于让标准 CRUD、服务层和开发态校验共享同一套模型结构结果，避免模型字段变更后继续手工维护一份基础 DTO。

## 2. 什么时候需要运行

出现以下情况时应运行：

- 新增或修改 `*.model.json` 字段。
- 调整字段的 `required`、`storeType`、`kind`、`contract` 等会影响 DTO 形状的属性。
- 调整枚举字段的 `contract.clrType`。
- 新模型纳入 `PlatformPersistenceModelCatalog` 后，需要生成对应基础 DTO。
- 开发态启动或测试提示 DTO 签名与模型真源不一致。

只改业务服务逻辑、仓储查询逻辑或前端页面时，通常不需要运行。

## 3. 如何运行

在仓库根目录执行：

```powershell
dotnet run --project .\server\tools\model-dto-sync\ModelDtoSync.csproj --
```

也可以先构建，再执行 exe：

```powershell
dotnet build .\server\tools\model-dto-sync\ModelDtoSync.csproj -v minimal
.\server\tools\model-dto-sync\bin\Debug\net10.0\ModelDtoSync.exe
```

如果当前目录不在仓库内，也可以显式传入仓库根目录：

```powershell
dotnet run --project D:\Github\ICP-X\NewP\server\tools\model-dto-sync\ModelDtoSync.csproj -- D:\Github\ICP-X\NewP
```

执行成功后，工具会输出本次写入的文件路径。

## 4. 生成到哪里

当前已受管模块包括：

- `Authorization`
- `ConfigCenter`
- `Organization`

生成位置示例：

```text
server/modules/Authorization/DTOs/Generated/RoleDto.g.cs
server/modules/ConfigCenter/DTOs/Generated/ConfigItemDto.g.cs
server/modules/Organization/DTOs/Generated/PersonDto.g.cs
server/modules/Organization/DTOs/Generated/OrganizationBaseDtoSignatureProvider.g.cs
```

工具当前只为已纳入 catalog 且已配置模块归属的模型生成 DTO。若新增模型后工具提示“尚未配置 DTO 输出模块”，说明还需要在 DTO 同步内核中补充受控模块映射。

## 5. 字段映射规则

生成器会保留模型字段的业务语义，同时把平台协议字段映射为对外 DTO 字段：

| 模型字段 | DTO 字段 |
| --- | --- |
| `EID` | `Id` |
| `ENAME` | `DisplayName` |
| `ECODE` | `Code` |

为降低协议字段切换造成的一次性 API 断裂，`ENAME` 当前还会额外生成 `Name` 兼容别名：

```csharp
public string Name { get => DisplayName; init => DisplayName = value; }
```

这个 `Name` 只是 DTO 兼容层，不代表元数据或数据库中仍存在 `Name` 字段。

引用字段和枚举字段会生成展示值属性：

```csharp
public string? CompanyId { get; init; }
public string? CompanyId_DisplayValue { get; init; }
public EmploymentStatus? EmploymentStatus { get; init; }
public string? EmploymentStatus_DisplayValue { get; init; }
```

枚举字段如在 `model.json` 中声明了 `contract.clrType`，生成 DTO 会优先使用对应 CLR 枚举类型；否则退回 `string`。

## 6. 哪些 DTO 自动生成，哪些仍然手写

应自动生成：

- 与单个模型结构一一对应的基础 DTO。
- 标准 CRUD 或基础服务直接返回的模型 DTO。
- 需要随 `model.json` 字段变更同步变化的 DTO。

可以手写：

- 跨模型聚合查询 DTO。
- 页面或用例专属 DTO。
- 命令入参、批量操作、导入导出等不等同于模型结构的 DTO。
- 对外 API 需要长期稳定且不应直接暴露模型结构变化的契约 DTO。

手写 DTO 不应放在 `DTOs/Generated` 目录。

## 7. 生成物能不能手改

不要手改 `DTOs/Generated/*.g.cs`。

原因：

- 下次运行 `model-dto-sync` 会覆盖这些文件。
- 签名 provider 会基于生成源码计算 SHA-256 签名。
- 开发态校验依赖签名判断生成物是否仍匹配模型真源。

如果生成结果不符合预期，应优先修改：

- `*.model.json`
- `ModelDtoGenerator`
- `ModuleDtoSyncRunner` 的受控模块映射
- 相关测试

不要直接修生成文件来绕过问题。

## 8. 推荐工作流

修改模型字段时，建议按以下顺序：

1. 修改 `modules/<ModuleId>/metadata/models/*.model.json`。
2. 运行 `model-dto-sync` 生成 DTO。
3. 检查生成 DTO diff，确认字段名、类型、可空性和展示值属性符合预期。
4. 更新服务层、仓储或测试中引用 DTO 字段的位置。
5. 如涉及数据库结构，运行 `dev-schema-sync --sql` 预览结构计划。
6. 运行相关后端测试。

常用验证命令示例：

```powershell
dotnet test .\tests\Modeling.Tests\Modeling.Tests.csproj -v minimal
dotnet test .\tests\Authorization.Tests\Authorization.Tests.csproj -v minimal
dotnet test .\tests\ConfigCenter.Tests\ConfigCenter.Tests.csproj -v minimal
dotnet test .\tests\Organization.Tests\Organization.Tests.csproj -v minimal
```

## 9. 失败信号

常见失败信号：

- 工具提示无法定位仓库根目录。
- 工具提示模型尚未配置 DTO 输出模块。
- 生成 DTO 缺少预期字段、类型或展示值属性。
- 开发态校验提示 DTO 签名不一致。
- 服务层或测试仍引用旧字段，例如把协议字段切换后的 `DisplayName` 继续当作 `Name` 的唯一来源。

## 10. 回滚思路

如果 DTO 生成结果需要回退：

- 回退对应 `*.model.json` 改动。
- 重新运行 `model-dto-sync`。
- 回退服务层、仓储和测试中配套的字段引用修改。

如果是生成器规则本身引入问题，应回退 `ModelDtoGenerator` 或 `ModuleDtoSyncRunner` 的修改，然后重新生成 DTO。
