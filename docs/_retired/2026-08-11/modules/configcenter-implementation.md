# ConfigCenter 模块实现概览

## 当前状态

状态：已完成持久化与 DTO 收口，验证通过

当前仓库中，ConfigCenter 已具备后端模块、前端模块、测试项目与基础说明文档，不再是纯占位目录。

## 已有内容

### 后端

- 模块入口：`server/modules/ConfigCenter/ConfigCenterModule.cs`
  - 实现 `IConfigProvider`、`IMenuProvider`、`IPermissionProvider`
  - 声明 3 个配置项（缓存开关、缓存时长、审计开关）
  - 声明 2 个菜单项和 4 个权限
- 控制器：`ConfigController`
  - `GET /api/config` - 获取所有配置
  - `GET /api/config/grouped` - 按模块和分组聚合配置
  - `GET /api/config/{key}` - 按键获取配置
  - `GET /api/config/module/{moduleId}` - 按模块获取配置
  - `POST /api/config` - 创建配置
  - `PUT /api/config/{id}` - 更新配置
  - `DELETE /api/config/{id}` - 删除配置
- 服务层：`IConfigService`、`ConfigService`
  - 配置 CRUD 操作
  - 环境和租户过滤（优先级：租户+环境 > 环境 > 租户 > 全局）
  - 敏感配置脱敏（返回 `******`）
  - 配置 schema 同步（`SyncConfigSchemaAsync`）
- 仓储层：`IConfigRepository`、`IValueListRepository`
  - 当前默认使用 `EfConfigRepository`、`EfValueListRepository`
  - 已接入统一持久化内核与三套 provider migration
  - 支持按 ID、Key、Module、Keys 批量查询，以及环境和租户维度过滤
- 数据模型：`ConfigItem`
  - 包含 Id、Key、Value、Type、ModuleId、Group、Environment、TenantId
  - 包含 DisplayName、Description、IsSensitive
  - 包含 CreatedAt、UpdatedAt、UpdatedBy
- DTO：`ConfigItemDto`、`ValueListItemDto`、`ConfigGroupDto`
  - `ConfigItem` 与 `ValueListItem` 已切到单 DTO 读写接口，不再拆分 `Create / Update DTO`
  - `ConfigGroupDto` 继续作为聚合读取模型保留

### 前端

- 前端模块：`frontend/modules/config-center`
  - 模块元数据：moduleId、displayName、version、description
  - 路由：`/config/items` 映射到 `ConfigManagement` 组件
  - 菜单：配置中心主菜单及配置管理子菜单
  - 权限：View、Create、Edit、Delete
- 配置管理页面：`ConfigManagement.tsx`
  - 按模块和分组展示配置项
  - 支持内联编辑配置值
  - 权限控制（查看、创建、编辑、删除）
  - 使用共享页面样式（sh-page、sh-table、sh-btn 等）

### 测试

- `tests/ConfigCenter.Tests/ConfigServiceTests.cs`
  - 9 个单元测试，全部通过
  - 覆盖全局配置、环境配置、租户配置、优先级解析
  - 覆盖 CRUD 操作、敏感配置脱敏、分组查询

## 能力边界

当前实现主要覆盖：

- 配置项查询（全局、按模块、按键、分组）
- 配置项 CRUD（创建、读取、更新、删除）
- 按模块、环境、租户维度组织配置
- 敏感配置脱敏（查询时自动返回 `******`）
- 配置 schema 同步（从模块声明的 `ConfigDescriptor` 同步默认值）
- 配置优先级解析（租户+环境 > 环境 > 租户 > 全局）

边界说明见 [configcenter-module-boundary-v1.md](configcenter-module-boundary-v1.md)。

## 当前实现特征

- 以最小可运行能力为目标
- 已有 API 设计与基础服务实现
- 已切到 EF Core 持久化实现，支持 `SqlServer / MySql / Oracle`
- `ConfigItem` 已由 metadata provider 驱动并投影到 `PascalCase` 表列命名
- 服务和 DTO 层已收口为当前稳定接口，不再额外维护 `Create / Update DTO`
- 适合作为后续接入持久化、导入能力和更复杂配置治理的基础

## 技术实现细节

### 命名与 DTO 规则

- `ConfigItem` / `ValueListItem` 当前统一使用 `PascalCase` 表名与普通列名
- `ConfigItem` metadata 试点继续保留 `EID / ENAME` 协议字段，与普通字段命名边界分离
- `ConfigController` / `ValueListController` 已直接使用单一 DTO 作为创建、更新、读取的主模型

### 配置优先级解析

`ConfigRepository.GetByKeyAsync` 实现了配置优先级解析逻辑：

1. 优先查找租户+环境精确匹配的配置
2. 其次查找环境匹配的配置
3. 再次查找租户匹配的配置
4. 最后兜底到全局配置

### 敏感配置脱敏

`ConfigService.MapToDto` 在映射时检查 `IsSensitive` 标志，若为 true 则返回 `******`，原始值仍保存在仓储中。

### 配置 schema 同步

`ConfigService.SyncConfigSchemaAsync` 接收 `ConfigDescriptor` 列表，对于不存在的基础配置项（Environment 和 TenantId 均为 null），自动创建默认配置项。

## 已知后续工作

- 将 `ValueListItem` 继续迁入 metadata model 链路
- 与迁移跑道的配置导入对接
- 与审计模块的变更记录联动
- 配置类型校验（当前 Type 仅为描述信息）
- 配置变更推送和缓存失效机制
