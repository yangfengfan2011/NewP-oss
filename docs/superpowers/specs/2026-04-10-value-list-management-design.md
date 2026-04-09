# 值列表管理设计

## 背景

当前 `ConfigCenter` 已有完整的后端模块、前端页面和测试基础设施，适合作为“值列表管理”功能的承载位置。用户需要一个简单可用的管理界面，具备新建、编辑、删除、刷新按钮，并在表格中展示数据库中的值列表数据。

## 目标

- 在 `ConfigCenter` 模块内新增值列表管理能力。
- 后端提供值列表数据的增删改查接口，并接入现有 EF Core 持久化。
- 前端提供一个单页管理界面，支持刷新、表格展示、新建、编辑、删除。
- 保持实现最小化，字段先固定为 `Code`、`Name`、`Description`。

## 非目标

- 不在本轮引入分类树、批量导入导出、分页筛选、审计联动。
- 不拆分独立模块，不单独设计复杂权限体系。

## 方案

### 落点

- 后端继续放在 `server/modules/ConfigCenter`
- 前端继续放在 `frontend/modules/config-center`
- 菜单作为 `配置中心` 下的新子菜单

### 数据模型

新增 `ValueListItem`：

- `Id`
- `Code`
- `Name`
- `Description`
- `CreatedAt`
- `UpdatedAt`
- `UpdatedBy`

### 后端能力

- `GET /api/value-lists`
- `POST /api/value-lists`
- `PUT /api/value-lists/{id}`
- `DELETE /api/value-lists/{id}`

服务层负责最小校验：

- `Code` 必填
- `Name` 必填
- `Code` 唯一

### 前端能力

新增“值列表管理”页面：

- 顶部按钮：新建、编辑、删除、刷新
- 中间表格：展示值列表数据
- 单选当前行后允许编辑/删除
- 新建和编辑复用同一表单区域

## 验证

- `tests/ConfigCenter.Tests` 新增值列表服务与仓储测试
- `dotnet test tests/ConfigCenter.Tests/ConfigCenter.Tests.csproj`
- `pnpm --filter @icp-platform/module-config-center build`

## 风险与取舍

- 为追求快速交付，首版不做分页和搜索，数据量大时页面体验会一般。
- 复用 `ConfigCenter` 现有权限分组，避免为简单功能增加额外系统复杂度。
