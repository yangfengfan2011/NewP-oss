---
description: "Use when writing or modifying unit tests, integration tests, or smoke tests. Covers test framework, naming conventions, and testing patterns."
applyTo: "tests/**"
---
# 测试编写规则

## 测试框架

- **xUnit** 2.9.3
- **Moq** 4.20（需要 Mock 时使用）
- **EF Core InMemory**（数据层单元测试）
- **Microsoft.AspNetCore.Mvc.Testing**（集成测试）

## 目标框架

.NET 10.0，与主项目保持一致。

## 测试项目结构

每个模块一个测试项目，只测试本模块职责：

```
tests/<ModuleName>.Tests/
├── <ModuleName>.Tests.csproj
└── <Subject>Tests.cs
```

## 命名约定

- 项目名：`<ModuleName>.Tests`
- 测试类名：`<Subject>Tests`（如 `RoleServiceTests`、`FileServiceTests`）
- 测试方法：描述行为，如 `CreateRole_WithValidInput_ReturnsRole`
- 使用 `[Fact]` 标记单个测试，`[Theory]` + `[InlineData]` 标记参数化测试

## 测试模式

- **AAA 模式**：Arrange → Act → Assert
- **数据层**：使用 EF Core InMemory Provider，每个测试独立的数据库名
- **服务层**：通过 Mock 隔离仓储依赖
- **集成测试**：使用 `WebApplicationFactory` 启动完整 Host

## 测试项目依赖

- 测试项目只引用对应模块项目，不跨模块引用
- 如需测试模块间集成，放在 `Kernel.Tests` 或 `integration/` 下

## 运行测试

```powershell
# 运行全部测试与仓库静态门禁
pwsh ./scripts/test.ps1

# 运行单个模块测试
dotnet test tests/<ModuleName>.Tests/<ModuleName>.Tests.csproj
```

## 新增模块时

为新模块创建测试项目并加入解决方案；仓库测试会自动发现 `tests/**/*.Tests.csproj` 并检查 solution 覆盖，不维护第二份项目清单。
