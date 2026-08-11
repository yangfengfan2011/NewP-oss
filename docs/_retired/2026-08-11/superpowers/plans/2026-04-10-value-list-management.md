# Value List Management Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** 在 `ConfigCenter` 模块内新增一个可用的值列表管理页面和后端 CRUD 接口。

**Architecture:** 后端在 `ConfigCenter` 模块内增加新的值列表实体、仓储、服务和控制器，并接入现有 EF Core 动态模型定义。前端在现有 `config-center` 模块内新增一个子页面，通过简单表格和表单完成管理操作。

**Tech Stack:** .NET 10, ASP.NET Core, EF Core, xUnit, React, TypeScript

---

### Task 1: 文档与模型边界

**Files:**
- Create: `docs/superpowers/specs/2026-04-10-value-list-management-design.md`
- Create: `docs/superpowers/plans/2026-04-10-value-list-management.md`

- [ ] **Step 1: 固化设计说明**

写清楚落点、字段、接口、前端交互和验证命令。

- [ ] **Step 2: 固化实现顺序**

先后端测试，再后端实现，再前端页面，最后统一验证。

### Task 2: 后端值列表测试

**Files:**
- Modify: `tests/ConfigCenter.Tests/ConfigServiceTests.cs`
- Modify: `tests/ConfigCenter.Tests/EfConfigRepositoryTests.cs`
- Modify: `tests/ConfigCenter.Tests/ConfigCenterModelDefinitionTests.cs`

- [ ] **Step 1: 写服务层 failing tests**
- [ ] **Step 2: 运行 `dotnet test tests/ConfigCenter.Tests/ConfigCenter.Tests.csproj --filter ValueList` 验证失败**
- [ ] **Step 3: 写仓储与模型定义 failing tests**
- [ ] **Step 4: 再次运行同一命令验证失败原因正确**

### Task 3: 后端值列表实现

**Files:**
- Create: `server/modules/ConfigCenter/Models/ValueListItem.cs`
- Create: `server/modules/ConfigCenter/DTOs/ValueListItemDto.cs`
- Create: `server/modules/ConfigCenter/Repositories/IValueListRepository.cs`
- Create: `server/modules/ConfigCenter/Repositories/ValueListRepository.cs`
- Create: `server/modules/ConfigCenter/Persistence/Ef/EfValueListRepository.cs`
- Create: `server/modules/ConfigCenter/Services/IValueListService.cs`
- Create: `server/modules/ConfigCenter/Services/ValueListService.cs`
- Create: `server/modules/ConfigCenter/Controllers/ValueListController.cs`
- Modify: `server/modules/ConfigCenter/Persistence/Descriptors/ConfigCenterModelDefinitions.cs`
- Modify: `server/modules/ConfigCenter/ConfigCenterModule.cs`

- [ ] **Step 1: 写最小实现让测试通过**
- [ ] **Step 2: 注册服务和 EF 仓储**
- [ ] **Step 3: 暴露 CRUD API**
- [ ] **Step 4: 运行 `dotnet test tests/ConfigCenter.Tests/ConfigCenter.Tests.csproj`**

### Task 4: 前端值列表页面

**Files:**
- Create: `frontend/modules/config-center/src/pages/ValueListManagement.tsx`
- Modify: `frontend/modules/config-center/src/index.ts`

- [ ] **Step 1: 实现刷新与列表加载**
- [ ] **Step 2: 实现新建和编辑表单**
- [ ] **Step 3: 实现删除操作与行选择**
- [ ] **Step 4: 运行 `pnpm --filter @icp-platform/module-config-center build`**

### Task 5: 完整验证与交付说明

**Files:**
- Modify: `docs/superpowers/specs/2026-04-10-value-list-management-design.md`

- [ ] **Step 1: 运行后端测试**
- [ ] **Step 2: 运行前端构建**
- [ ] **Step 3: 整理开发过程与风险说明**
