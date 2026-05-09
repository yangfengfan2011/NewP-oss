# EID 值协议平台化设计

## 1. 背景

当前仓库已经把 `EID` 固化为平台实体协议主键字段，但没有同步固化“`EID` 的值如何生成”。实际代码中存在多条生成路径：

- 标准 CRUD 创建路径在 `EfStandardCrudRepository.CreateAsync` 中直接生成裸 `Guid` 字符串。
- ConfigCenter / Authorization 的部分模型默认 `Id = Guid.NewGuid().ToString()`。
- Organization 的 `Company / Department / Position / Person` 领域模型仍使用 `Guid Id = Guid.NewGuid()`。
- `PersonUserLink` 当前把 `PersonId` 同时当成业务关联键和实体 `EID` 承接值。
- 测试中混用 `person-1`、裸 GUID 和固定字符串。

这导致当前实际规则是“任意字符串 / 裸 GUID / 业务稳定值”，而不是期望的 `{对象名称}_{GUID}`。

2026-05-10 补充前提：当前开发数据库中的表已被清空 / 删除，本轮可按“空库重建”处理。也就是说，设计不再需要保护现有数据库中的裸 GUID 主键数据，可以优先收口干净的新对象标识协议。

## 2. 目标

- 定义平台标准对象标识值协议：实体对象新建时默认生成 `{ModelName}_{Guid:N}`。
- 将所有当前内建模块中会写入 `EID` 的新建路径收口到统一对象 ID 生成器。
- 让标准 CRUD、模块 service、DTO、API 路由、关系引用和摘要 provider 都把平台对象标识视为字符串，不再默认 `Guid.Parse(EID)`。
- 让 `PersonUserLink` 这类关系对象拥有自己的 `EID`，关联字段只作为引用字段存在，不再复用 `PersonId` 当实体主键。
- 形成可复用的对象 ID 生成器，供后续统一资源 API 和新模块模板直接复用。

## 3. 非目标

- 不做历史裸 GUID `EID` 回填；当前按空库重建处理。
- 不保留旧裸 GUID 主键数据兼容读取承诺；若后续需要导入旧数据，应单独做迁移设计。
- 不重新设计 `EID / LID / PEID / CEID` 字段命名。
- 不把统一资源 API 一并落地；统一 API 后续只消费本设计形成的字符串 ID 口径。
- 不把对象 ID 生成规则写入 metadata；metadata 只声明对象是什么，生成行为留在代码内核。
- 不改变用户账号 ID、权限码、配置 key 等非平台对象主键语义。

## 4. 范围

本轮建议覆盖所有当前会写入平台实体 `EID` 的主路径：

- `server/platform-crud`
  - 新增或接入平台对象 ID 生成器。
  - 标准 CRUD 创建时生成符合协议的 `EID`。
- `server/modules/ConfigCenter`
  - `ConfigItem / ValueListItem` 新建路径使用统一 ID 生成器。
- `server/modules/Authorization`
  - `Role / Permission / RolePermission / UserRole` 新建路径使用统一 ID 生成器。
- `server/modules/Organization`
  - `Company / Department / Position / Person / PersonUserLink` 新建路径使用统一 ID 生成器。
  - 这些对象的领域模型、DTO、service、controller、repository 中的平台对象 ID 和引用字段逐步改为 `string`。
  - 路由参数不再使用 `{id:guid}` 约束。
- `tests/*`
  - 现有测试中代表平台对象 ID 的裸 GUID 样例改为 `{ModelName}_{Guid:N}` 或稳定测试 ID。

本轮不覆盖：

- 生产历史数据迁移。
- 统一资源 API。
- 文件模块中用于文件名或存储路径的 GUID。
- 临时测试目录名等非业务对象标识。

## 5. 设计约束

- `EID` 是平台对象标识，类型语义是字符串，不是 GUID。
- 新 ID 必须可读、稳定、全局低碰撞，并能从前缀快速判断对象类型。
- 所有平台对象引用字段也应按字符串 ID 处理；不要把 `CompanyId / PersonId` 等引用字段继续建模成 `Guid`。
- 生成行为必须收口在代码内核，不能依赖 metadata 解释器、复制顺序或模块私有约定。
- 新增公共生成器应有 XML 注释，说明值格式、边界、失败信号和非目标。
- 本轮按空库重建，不新增数据库回填 migration；如模型列类型已是字符串，优先避免无意义迁移。

## 6. 总体方案

采用“平台对象 ID 生成器 + 全链路字符串 ID 契约”的方案。

标准格式：

```text
{ModelName}_{Guid:N}
```

示例：

```text
Person_7f3c0f0c2d7d4f5b9a2c8e7f1a0b3c4d
Company_0a5f7a9f0a68425ab2fdd5d5fd4c5f2d
PersonUserLink_b04c66d9984748e8b8bd9637b47e9156
```

选择 `Guid:N` 而不是 `Guid:D` 的原因：

- 无连字符，更适合作为 URL path segment。
- 长度稳定，测试和校验简单。
- 与 `{ModelName}_` 拼接后仍保持人眼可读。

不选择裸 GUID 的原因：

- 无法从 ID 本身判断对象类型。
- 容易让 DTO、Controller、Repository 继续使用 `Guid`，与平台对象协议冲突。

不选择 `resourceKey_guid` 的原因：

- `organization.person_...` 包含点号，作为 URL、日志、文件名和跨系统引用时处理成本更高。
- `ModelName` 已经是当前 ModelDefinition / 表名 / descriptor 的稳定对象名。

## 7. 关键模型

建议新增平台抽象：

```csharp
public interface IPlatformObjectIdGenerator
{
    string NewId(string objectName);
}
```

默认实现：

```csharp
public sealed class PlatformObjectIdGenerator : IPlatformObjectIdGenerator
{
    public string NewId(string objectName);
}
```

校验规则：

- `objectName` 必须非空。
- `objectName` 首轮要求匹配 `^[A-Za-z][A-Za-z0-9]*$`，防止空格、点号、斜杠进入 ID。
- 返回值匹配 `^{objectName}_[0-9a-f]{32}$`。

模型调整原则：

- 平台对象主键：`string Id`。
- 平台对象引用：`string XxxId` 或 `string? XxxId`。
- 外部用户 ID、权限码、配置 key：保持原有字符串语义，不套用 `{ModelName}_{Guid:N}`。

Person 相关 DTO 调整：

- `PersonListItemDto.Id: Guid -> string`
- `PersonDetailDto.Id: Guid -> string`
- `PersonDto.Id` 已是 `string`，保持。
- `CompanyId / DepartmentId / PositionId` 等平台对象引用继续对外表现为字符串。

## 8. 核心流程

### 创建流程

1. Controller、模块 service 或标准 CRUD 创建平台对象。
2. 创建路径调用 `IPlatformObjectIdGenerator.NewId(ModelName)`。
3. 新对象的 `EID` 写入 `{ModelName}_{Guid:N}`。
4. 关系对象单独生成自己的 `EID`，引用字段写入被关联对象 ID。
5. 返回 DTO 时保留字符串 ID。

### 读取 / 更新 / 删除流程

1. 外部传入字符串 ID。
2. service / repository 使用字符串等值匹配 `EID`。
3. 不解析 GUID，不拆前缀，不依赖对象名前缀执行权限或路由判断。

### 关系写入流程

以 `PersonUserLink` 为例：

1. API 接收 `personId = Person_<guid>`。
2. Organization 校验该 Person 是否存在。
3. 新建或更新 `PersonUserLink`：
   - `EID = PersonUserLink_<guid>`
   - `PersonId = Person_<guid>`
   - `UserId = <Identity Lite 用户 ID>`
4. 不再把 `PersonId` 同时作为关系对象主键。

## 9. 接口与集成点

- 后端 DI：
  - `AddPlatformObjectIdGeneration()` 注册默认 `IPlatformObjectIdGenerator`，这是独立于标准 CRUD 的平台对象 ID 生成能力。
  - `AddStandardCrudKernel()` 内部调用 `AddPlatformObjectIdGeneration()`，避免标准 CRUD 使用方遗漏生成器。
  - ConfigCenter、Authorization、Organization 这类直接通过模块 service 写入 EID 的模块，也必须在模块装配点调用 `AddPlatformObjectIdGeneration()` 或通过等价宿主级注册获得同一个生成器。
- 标准 CRUD 仓储：
  - 构造函数接收 `IPlatformObjectIdGenerator`。
  - 未传入 `EID` 时生成 `{descriptor.ModelName}_{Guid:N}`。
- 模块 service：
  - 创建 `ConfigItem / Role / Company / Person` 等对象时显式生成新 ID。
- Organization API：
  - 路由参数从 `{id:guid}` 改为 `{id}`。
  - service 和 repository 参数从 `Guid id` 改为 `string id`。
- DTO 生成：
  - `EID -> Id` 仍映射为 `string`。
  - 引用字段默认也按 `string` 输出。

## 10. 数据与状态

- 当前按空库重建处理，不保留旧裸 GUID 主键数据。
- `EID` 字段数据库类型仍为 `string` / `nvarchar(128)`。
- 多数 `ObjectId` 字段底层已经是字符串列，本轮重点是代码层类型和生成路径收口。
- 如果某些 migration snapshot 因领域类型变更产生结构差异，应先确认是否真实影响数据库列类型；只有真实结构变化才生成三套 migration。

## 11. 错误处理与风险

风险：

- 改动范围比原设计更大，会触及 ConfigCenter、Authorization、Organization 多条 service 测试。
- 某些前端或测试仍把 `id` 当 GUID，导致新 ID 无法解析。
- Organization 组织树、岗位、人员绑定等内部接口仍有 `Guid` 路由或 service 参数残留。
- 旧测试数据里用 `person-1` 这类非协议 ID，可能让新协议没有被真实覆盖。

控制方式：

- 先用 failing tests 锁定生成器和标准 CRUD 创建结果。
- 再分模块把 service / controller / repository 的 ID 类型切到字符串。
- 测试 ID 统一使用稳定协议值，例如 `Person_00000000000000000000000000000001`。
- 不混入统一 API、权限、业务规则等无关改动。

## 12. 验证方案

最小验证命令：

```powershell
dotnet test .\tests\PlatformCrud.Tests\PlatformCrud.Tests.csproj --filter "StandardCrudServiceWriteTests|StandardCrudEfRepositoryTests" -nr:false -v minimal
dotnet test .\tests\ConfigCenter.Tests\ConfigCenter.Tests.csproj -nr:false -v minimal
dotnet test .\tests\Authorization.Tests\Authorization.Tests.csproj -nr:false -v minimal
dotnet test .\tests\Organization.Tests\Organization.Tests.csproj -nr:false -v minimal
dotnet build .\apps\host-api\HostApi.csproj -nr:false -v minimal
```

证明点：

- 标准 CRUD create 返回 `Person_<32位小写hex>`。
- ConfigCenter / Authorization / Organization 的新建路径不再返回裸 GUID `EID`。
- Person list/detail/user-link 等接口的 `id` 是字符串。
- 后端不再因为 `Guid.Parse(EID)` 处理平台对象主键。

## 13. 未决问题

- 是否要在本轮同步改前端所有 `id` 类型声明：建议至少改 Organization 前端中明确把人员 ID 当 GUID 的部分；其余模块按编译反馈处理。
- 是否要清理旧 migration 中的历史回填断言：如果当前迁移从空库重放仍通过，可先不动；若测试断言语义已经误导，应同步更新测试与验证记录。

## 14. 实施拆分建议

1. 平台 ID 生成器与标准 CRUD 创建接入。
   - 最小交付：新增生成器，标准 CRUD 创建新行使用 `{ModelName}_{Guid:N}`。
2. PlatformCrud 测试仓储与测试样例同步。
   - 最小交付：平台 CRUD 单测不再用裸 GUID 表示新对象 ID。
3. ConfigCenter / Authorization 直接 service 写入路径接入生成器。
   - 最小交付：角色、权限、配置和值列表新建返回协议 ID。
4. Organization 领域模型、service、controller、repository 字符串 ID 收口。
   - 最小交付：组织主数据、人员、人员账号关联都使用协议 ID。
5. 文档与验证记录回填。
   - 最小交付：记录空库前提、新 ID 协议和后续统一资源 API 依赖。

## 15. AI-First 最小检查清单

- 入口在哪里：
  - `server/platform-crud` 的对象 ID 生成器与标准 CRUD 创建路径。
  - ConfigCenter / Authorization / Organization 的 service 创建路径。
- 边界在哪里：
  - 本轮只改平台对象 ID 生成与使用，不做统一资源 API、不改权限业务、不做历史数据迁移。
- 如何验证：
  - 运行 PlatformCrud、ConfigCenter、Authorization、Organization 测试和 HostApi 构建。
- 失败信号是什么：
  - 任一平台对象新建后仍返回裸 GUID。
  - 任一平台对象 API 路由仍要求 `{id:guid}`。
  - 后端仍对平台对象主键执行 `Guid.Parse(EID)`。
- 如何回滚：
  - 撤回统一 ID 生成器接入。
  - 恢复对应模块的 `Guid` 领域 ID 和路由约束。
  - 因本轮按空库重建且不做历史回填，回滚主要是代码与测试回退，不涉及数据回填。
