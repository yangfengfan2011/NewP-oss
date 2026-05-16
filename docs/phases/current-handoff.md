# 当前交接台账

最近更新：2026-05-17

## 1. 目的

本文档用于承接“当前现状、上一次做了什么、下一步推荐做什么、还有哪些未解决问题”这类交接信息。

它只维护当前快照，不替代详细设计、实施计划、阶段地图或验证记录。需要判断设计边界、任务步骤和验证细节时，仍应回到对应源文档。

## 2. 使用方式

开始新任务时建议按以下顺序阅读：

1. `AGENTS.md`
2. `docs/phases/current-handoff.md`
3. `docs/phases/post-phase-6-stage-map.md`
4. 当前任务相关的设计、计划、模块说明和验证记录

每次完成一个工作包或一次明确收口后，应回填本文档的：

- 最近完成
- 当前推荐下一步
- 未解决问题
- 验证快照
- 已知风险与回滚思路

## 3. 当前事实源

- 仓库级规则入口：`AGENTS.md`
- 当前交接快照：`docs/phases/current-handoff.md`
- 阶段主线入口：`docs/phases/post-phase-6-stage-map.md`
- 新增设计与计划入口：`docs/superpowers/README.md`
- 验证口径入口：`docs/verification/verification-matrix.md`
- 历史状态归档：`docs/current-status.md`

如果本文档与具体设计、计划或验证记录冲突，以更具体、更新的任务文档为准，并在下一次收口时修正本文档。

## 4. 最近完成

### 4.1 前端标准 CRUD 平台化第二批

状态：已完成并验证。

主要依据：

- `docs/superpowers/specs/platform/2026-05-12-frontend-standard-crud-platformization-design.md` 第 14.2 节
- `docs/superpowers/plans/platform/2026-05-16-frontend-standard-crud-platformization-phase-2.md`
- `docs/verification/frontend-standard-crud-platformization-phase-2-verification.md`

完成内容：

- SDK 提供字段、表单、lookup、动作、summary 和 i18n 第二批契约与 helper。
- UI 提供无业务语义的标准 CRUD 字段和表单骨架。
- Shell 接管 lookup 加载、动作状态、权限动作过滤、summary 状态协议和详情编辑流程。
- Organization 人员中心接入第二批平台能力，通用字段渲染、级联 lookup 状态机和 summary 状态协议不再散落在人员组件中。

当前推荐下一步：

- 选择下一批标准 CRUD 资源试点，或继续拆解后端 required field / 数据一致性主线。

### 4.2 前端标准 CRUD 平台化第一批

状态：已完成并验证。

主要依据：

- `docs/superpowers/specs/platform/2026-05-12-frontend-standard-crud-platformization-design.md`
- `docs/superpowers/plans/platform/2026-05-12-frontend-standard-crud-platformization.md`
- `docs/verification/frontend-standard-crud-platformization-phase-1-verification.md`

完成内容：

- SDK 提供标准 CRUD 前端契约和客户端 helper。
- Shell 提供标准 CRUD schema resolver、registry、列表 Host 和详情 Host。
- Organization 人员中心接入标准 CRUD 前端资源注册。
- 人员中心列表/详情主状态机不再留在模块私有页面中。

当前推荐下一步：

- 第一批列表/详情 Host 已稳定接管；第二批可独立回滚，不影响第一批列表/详情 Host。

### 4.3 标准 CRUD 路由自动映射与人员旧样板退场

状态：已完成并验证。

主要依据：

- `docs/superpowers/specs/platform/2026-05-11-standard-crud-route-mapping-design.md`
- `docs/superpowers/plans/platform/2026-05-11-standard-crud-route-mapping.md`
- `docs/verification/organization-module-verification.md`

完成内容：

- 平台新增标准 CRUD 资源注册表，`StandardCrudDescriptor.Route` 成为标准 API 路由唯一真源。
- Host 通过 `MapStandardCrudResources()` 自动挂载标准 CRUD 路由，`/api/organization/persons` 不再依赖人员标准 CRUD Controller。
- Organization 只注册 Person descriptor；人员账号绑定拆为专属 `PersonUserLinksController` 与 `PersonUserLinkService`。
- 人员中心后端旧标准 CRUD Controller / Service / Repository / DTO 样板已删除。
- 人员中心前端已改为消费标准 CRUD list/detail/mutation 返回模型。
- 验证记录已回填标准 CRUD 自动映射入口、旧样板残留扫描和前后端构建测试结论。

最近相关提交：

- `94eba99f`：功能：新增标准CRUD资源注册表
- `0943e9ec`：功能：新增标准CRUD自动路由映射
- `a7ea752d`：功能：拆出人员账号绑定服务
- `570768c2`：重构：人员中心标准CRUD改由平台自动映射
- `345efed0`：重构：人员中心前端消费标准CRUD模型
- `65494edd`：验证：记录标准CRUD自动映射收口结果

### 4.4 EID 值协议平台化

状态：已完成并验证。

主要依据：

- `docs/superpowers/specs/platform/2026-05-08-eid-value-protocol-design.md`
- `docs/superpowers/plans/platform/2026-05-10-eid-value-protocol.md`
- `docs/verification/organization-module-verification.md`

完成内容：

- 新增平台对象 ID 生成器，平台对象新建 `EID` 统一使用 `{ModelName}_{Guid:N}`。
- `PlatformCrud`、`ConfigCenter`、`Authorization`、`Organization` 的新建路径不再生成裸 GUID `EID`。
- `Organization` 人员、组织主数据和人员账号关联的主键与引用字段已按字符串 ID 流转。
- API 路由已移除 `{id:guid}` 约束。
- `PersonUserLink` 已改为独立 EID，避免继续复用人员或账号 ID 作为关联主键。

最近相关提交：

- `c921d527`：设计：制定EID值协议平台化方案
- `25f90c7f`：功能：新增平台对象ID生成器
- `45cfba2c`：功能：ConfigCenter接入平台对象ID
- `974c4604`：功能：Authorization接入平台对象ID
- `6e486f84`：功能：Organization收口平台对象ID
- `3e8faa55`：功能：人员账号关联使用独立EID
- `72807119`：验证：记录EID值协议平台化结果

## 5. 当前推荐下一步

当前不建议直接扩大功能面。前端标准 CRUD 第二批已经收口，下一轮如果继续本主线，应优先选择下一批标准 CRUD 资源试点；如果转向后端主线，应继续拆解 required field / 数据一致性问题，并继续保持单一任务、设计先行和可验证收口。

推荐优先级：

1. 若继续前端标准 CRUD 主线，选择下一批标准 CRUD 资源试点，逐个确认字段、lookup、动作、summary 和模块专属能力边界。
2. 若继续标准 CRUD 后端主线，优先独立设计 required field / 模型必填校验，避免把资源必填规则重新塞回模块专属 validator。
3. 若继续数据一致性主线，单独设计 `PersonNo` 数据库唯一索引或并发强一致性策略。
4. 若继续 EID 主线，单独设计历史裸 GUID 数据迁移；当前开发数据库仍按空库重建口径处理。
5. 若继续平台元数据主线，再拆包吸收 `docs/designs/platform/review-open-items-tracker.md` 中“已冻结，待计划吸收”的问题。
6. 若继续业务验证主线，单独拆包处理真实业务页消费接入或下一批 Dictionary / Enumeration 样本扩展。

## 6. 未解决问题

以下问题已知存在，但当前不应混在同一次改动中顺手处理。

### 6.1 待计划吸收的 review 结论

来源：`docs/designs/platform/review-open-items-tracker.md`

- `.dic` 消费链仍需对齐 authoring 统一规则。
- `.model` 消费链仍有固定目录或固定文件路径依赖。
- `.settings` 消费链仍主要依赖单一 `SourcePath`。
- 组织模块人员中心试点实施计划必须吸收已冻结的首轮边界。

### 6.2 EID 后续风险

来源：`docs/verification/organization-module-verification.md`

- 本轮没有做历史裸 GUID 数据回填；如需导入旧库数据，应单独设计迁移。
- 当前开发数据库按空库重建处理，不承诺旧裸 GUID 主键数据兼容。

### 6.3 标准 CRUD 后续风险

来源：`docs/verification/organization-module-verification.md`

- `ENAME` 等字段必填校验仍需要后续标准 CRUD required field 或模型必填校验设计承接。
- `PersonNo` 当前由标准 CRUD 服务层保证全局唯一，但未引入数据库唯一索引；并发强一致性如需支持，应另开设计。
- 标准 CRUD 自动路由映射当前已由 Person 试点验证；下一批资源接入前仍应逐个确认 descriptor 路由、字段边界和模块专属能力是否拆分清楚。

## 7. 验证快照

最近一次前端标准 CRUD 第二批验证覆盖：

```powershell
pnpm --dir .\frontend --filter @icp-platform/sdk build
pnpm --dir .\frontend --filter @icp-platform/ui build
pnpm --dir .\frontend --filter @icp-platform/shell test
pnpm --dir .\frontend --filter @icp-platform/shell build
pnpm --dir .\frontend --filter @icp-modules/organization build
rg -n "PersonEditorForm|getCompanies|getDepartments|getPositions|employmentStatusOptions|resolveListText|resolveDetailText|AuthorizationSummaryCard|PersonUserLinkCard" frontend/modules/organization/src frontend/packages
```

验证结论：

- `@icp-platform/sdk` 构建通过。
- `@icp-platform/ui` 构建通过。
- `@icp-platform/shell` 测试通过，38 files / 131 tests。
- `@icp-platform/shell` 构建通过。
- `@icp-modules/organization` 构建通过。
- 残留扫描中，`getCompanies/getDepartments/getPositions` 保留在 Organization service、人员资源 lookup providers、组织树和岗位管理页面中；人员表单 lookup 已通过标准 provider/runtime 消费，不再在 `PersonEditorForm` 或创建页散落通用状态机。
- `employmentStatusOptions` 保留为人员资源 enum option 数据和旧标签 helper 数据源。
- `PersonUserLinkCard`、`AuthorizationSummaryCard` 保留为 Organization 专属 summary renderer 内容组件，平台只处理状态协议。
- `PersonEditorForm` 只剩兼容包装，显示“人员编辑表单已由标准 CRUD 表单接管。”
- `resolveListText/resolveDetailText` 无命中。

前端标准 CRUD 第一批验证覆盖：

```powershell
pnpm --dir .\frontend --filter @icp-platform/sdk build
pnpm --dir .\frontend --filter @icp-platform/ui build
pnpm --dir .\frontend --filter @icp-platform/shell test
pnpm --dir .\frontend --filter @icp-platform/shell build
pnpm --dir .\frontend --filter @icp-modules/organization build
rg -n "StandardCrudListResult|StandardCrudDetail|normalizeSummaryBlock|normalizePersonListResponse|normalizePersonDetail|getPersonListRuntimeSchema|getPersonDetailRuntimeSchema" frontend/modules/organization/src frontend/packages
rg -n "PersonEditorForm|getCompanies|getDepartments|getPositions|PersonUserLinkCard|AuthorizationSummaryCard" frontend/modules/organization/src
```

验证结论：

- SDK、UI、Shell 和 Organization 前端构建通过，Shell 测试通过。
- 标准 CRUD 类型与 helper 已由 SDK 提供，Shell Host 已接管人员中心列表/详情主流程。
- 第二批暂留项已在 `docs/verification/frontend-standard-crud-platformization-phase-2-verification.md` 中完成收缩解释。

上一轮标准 CRUD 自动映射验证覆盖：

```powershell
dotnet test .\tests\PlatformCrud.Tests\PlatformCrud.Tests.csproj -nr:false -v minimal
dotnet test .\tests\Organization.Tests\Organization.Tests.csproj -nr:false -v minimal
dotnet build .\apps\host-api\HostApi.csproj -nr:false -v minimal
pnpm --dir .\frontend --filter @icp-modules/organization build
rg -n "PersonsController|IPersonService|PersonService|IPersonRepository|EfPersonRepository|PersonListItemDto|PersonDetailDto|PersonSummaryBlockDto" server/modules/Organization tests/Organization.Tests frontend/modules/organization/src
rg -n "MapStandardCrudResources|AddStandardCrudResource|IStandardCrudResourceRegistry|PersonUserLinksController|IPersonUserLinkService" apps/host-api server/platform-crud server/modules/Organization tests
```

验证结论：

- Person 标准 CRUD 路由由 `StandardCrudDescriptor.Route` 自动映射，对外仍保持 `/api/organization/persons` 模块语义路由。
- 人员中心只保留账号绑定专属 Controller，不再保留标准 CRUD 空壳 Controller。
- `server/modules/Organization`、`tests/Organization.Tests` 与 `frontend/modules/organization/src` 中未发现人员旧标准 CRUD 样板名称残留。
- 平台 CRUD 测试、Organization 测试、HostApi 构建与 Organization 前端构建均已通过；详细结果见 `docs/verification/organization-module-verification.md`。

## 8. 已知风险

- 本文档是当前快照，不应把它当作长期 authoring 真源。
- 如果只更新计划或验证记录而不更新本文档，下一次交接可能读到过期推荐下一步。
- 历史阶段文档中仍可能出现旧推荐语，应以本文件和对应任务的最新计划、验证记录为准。
- 前端标准 CRUD 第二批仍以 Person 资源为试点，下一批资源接入前需要重新确认字段、lookup、动作、summary 和模块专属能力边界。

## 9. 回滚思路

- 如果交接口径写错，只改本文档，不需要修改原始设计、计划或验证记录。
- 如果前端标准 CRUD 第二批需要回滚，可反向回滚第二批 SDK/UI/Shell 运行时和 Organization 接入提交；第二批可独立回滚，不影响第一批列表/详情 Host。
- 如果标准 CRUD 自动映射需要回滚，应按本轮最近相关提交反向 revert，并优先恢复 `PersonsController` 等旧样板前确认对应设计、计划和验证记录中的边界变化。
- 如果 EID 值协议需要回滚，应按最近相关提交反向 revert，并优先回到对应设计和计划确认影响范围。
- 如果下一步方向选择变化，先更新本文件第 5 节，再补对应设计或计划，避免多个入口同时表达不同推荐。
