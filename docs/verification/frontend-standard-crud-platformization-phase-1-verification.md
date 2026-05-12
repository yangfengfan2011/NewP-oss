# 前端标准 CRUD 平台化第一批验证

## 1. 范围

本次验证覆盖前端标准 CRUD 第一批平台化：

- SDK 标准 CRUD 前端契约与客户端 helper。
- Shell 标准 CRUD schema resolver、registry、列表 Host、详情 Host。
- Organization 人员中心试点接入。
- 第二批遗留登记。

本次不覆盖：

- 通用字段渲染器。
- 通用 lookup 数据源。
- 通用动作扩展与权限过滤。
- 通用 summary block 渲染协议。
- 通用 i18n key 解析。

## 2. 已执行验证

```powershell
pnpm --dir .\frontend --filter @icp-platform/sdk build
pnpm --dir .\frontend --filter @icp-platform/ui build
pnpm --dir .\frontend --filter @icp-platform/shell test
pnpm --dir .\frontend --filter @icp-platform/shell build
pnpm --dir .\frontend --filter @icp-modules/organization build
rg -n "StandardCrudListResult|StandardCrudDetail|normalizeSummaryBlock|normalizePersonListResponse|normalizePersonDetail|getPersonListRuntimeSchema|getPersonDetailRuntimeSchema" frontend/modules/organization/src frontend/packages
rg -n "PersonEditorForm|getCompanies|getDepartments|getPositions|PersonUserLinkCard|AuthorizationSummaryCard" frontend/modules/organization/src
```

## 3. 验证结论

- 标准 CRUD 类型与 helper 已由 `@icp-platform/sdk` 提供。
- Shell 标准 CRUD Host 已接管人员中心列表/详情主流程。
- Organization 不再持有完整标准 CRUD 列表状态机。
- 人员账号绑定、授权摘要、组织归属 lookup 和人员编辑表单仍留在 Organization，作为第二批输入。

## 4. 第二批固定交付清单

第二批入口：`docs/superpowers/specs/platform/2026-05-12-frontend-standard-crud-platformization-design.md` 第 14.2 节。

第二批必须覆盖：

- 通用字段渲染器：文本、布尔、枚举、引用、日期、只读展示。
- 通用编辑表单：create / edit / view、sections、必填、只读、默认值、提交值规范化。
- 通用数据源 lookup：enum、dictionary、value list、model lookup、显式级联依赖。
- 通用动作扩展：标准动作、模块动作、确认文案、pending 状态、错误提示、权限过滤。
- 通用 summary block 渲染协议：平台处理状态，模块只提供专属内容 renderer。
- 通用 i18n key 解析：统一 `dicKey + localKey` 规则。
- Organization 人员中心继续收缩，只保留人员账号绑定、授权摘要、组织归属特殊显示和必要业务校验。

## 5. 第一批暂留项与原因

- `PersonEditorForm`：表单字段渲染和级联 lookup 属于第二批。
- `getCompanies / getDepartments / getPositions`：组织归属 lookup 仍由 Organization 承接，第二批再通过 lookup provider 平台化。
- `PersonUserLinkCard`：人员账号绑定是 Organization 专属摘要展示。
- `AuthorizationSummaryCard`：授权摘要来源和业务含义仍由模块声明，第二批只抽 summary block 状态协议。
- `PersonCreatePage`：新建表单仍依赖 `PersonEditorForm`，第二批再收缩。

## 6. 已知风险

- 第一批只移走列表/详情主状态机，表单和 lookup 仍可能在下一批前出现重复模式。
- 如果第二批不推进，Organization 仍会保留较多字段渲染和选项加载代码。
- 如果后续模块直接复制 `PersonEditorForm`，会绕开本设计的第二批目标。

## 7. 回滚思路

- 回滚 SDK 标准 CRUD helper 与 Shell Host 相关提交。
- 恢复 Organization 原 `PersonListStandardListEntry` 和 `PersonDetailPage` 状态机。
- 保留本验证记录或在回滚提交中追加说明，避免第二批状态误判为已完成。
