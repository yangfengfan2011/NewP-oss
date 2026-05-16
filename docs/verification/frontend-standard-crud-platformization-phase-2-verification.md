# 前端标准 CRUD 平台化第二批验证

## 1. 范围

本次验证覆盖前端标准 CRUD 第二批平台化：

- SDK 字段、表单、lookup、动作、summary 和 i18n 契约。
- UI 通用字段与标准 CRUD 表单骨架。
- Shell lookup、动作、summary 和详情编辑运行时。
- Organization 人员中心接入第二批平台能力。
- 第一批暂留项收缩与残留解释。

本次不覆盖：

- 后端标准 CRUD 内核。
- 后端路由自动映射。
- 远程插件市场、插件签名或跨应用分发。
- 复杂表单设计器或拖拽布局。

## 2. 已执行验证

```powershell
pnpm --dir .\frontend --filter @icp-platform/sdk build
pnpm --dir .\frontend --filter @icp-platform/ui build
pnpm --dir .\frontend --filter @icp-platform/shell test
pnpm --dir .\frontend --filter @icp-platform/shell build
pnpm --dir .\frontend --filter @icp-modules/organization build
rg -n "PersonEditorForm|getCompanies|getDepartments|getPositions|employmentStatusOptions|resolveListText|resolveDetailText|AuthorizationSummaryCard|PersonUserLinkCard" frontend/modules/organization/src frontend/packages
```

验证结果：

- `pnpm --dir .\frontend --filter @icp-platform/sdk build`：PASS。
- `pnpm --dir .\frontend --filter @icp-platform/ui build`：PASS。
- `pnpm --dir .\frontend --filter @icp-platform/shell test`：PASS，38 files / 131 tests。
- `pnpm --dir .\frontend --filter @icp-platform/shell build`：PASS。
- `pnpm --dir .\frontend --filter @icp-modules/organization build`：PASS。

## 3. 验证结论

- 标准 CRUD 字段、表单、lookup、动作、summary 和 i18n 契约已由 `@icp-platform/sdk` 提供。
- UI 提供无业务语义的标准 CRUD 字段和表单骨架。
- Shell 已接管 lookup 加载、动作状态、summary 状态协议、权限动作过滤和详情编辑流程。
- Organization 人员中心不再持有通用字段渲染、级联 lookup 加载和 summary 状态协议。
- 第一批暂留项已按设计第 14.2 节收缩；残留命中均可解释为模块专属语义、lookup provider、enum option 数据或兼容包装。

## 4. Organization 暂留项与原因

- `getCompanies / getDepartments / getPositions`：保留在 Organization service、人员资源 lookup providers、组织树和岗位管理页面中；其中人员表单 lookup 已通过标准 provider/runtime 消费，不再在 `PersonEditorForm` 或创建页散落通用状态机。
- `employmentStatusOptions`：保留为人员资源 enum option 数据和旧标签 helper 数据源，平台只消费 option 契约。
- `PersonUserLinkCard`：保留为 Organization 专属 summary renderer 内容组件，人员账号绑定仍属于组织模块业务语义。
- `AuthorizationSummaryCard`：保留为 Organization 专属 summary renderer 内容组件，授权摘要来源和业务含义由模块声明，平台只处理状态协议。
- `PersonEditorForm`：只剩兼容包装，显示“人员编辑表单已由标准 CRUD 表单接管。”
- `resolveListText / resolveDetailText`：残留扫描无命中，dicKey 拼接逻辑未继续散落在人员页面组件中。

## 5. 已知风险

- 第二批以 Person 资源为试点，下一批资源接入前仍需逐个确认字段、lookup、动作和 summary 边界。
- 平台表单只承接标准字段和基础提交规范化，不替代模块专属业务校验。
- Organization 专属 summary renderer 仍由模块维护；如果后续资源复制人员摘要组件，需要先确认哪些状态协议应继续留在平台。

## 6. 回滚思路

- 回滚第二批 SDK/UI/Shell 运行时和 Organization 接入提交。
- 恢复模块私有 `PersonEditorForm`、lookup 加载和 summary 状态渲染。
- 第二批可独立回滚，不影响第一批列表/详情 Host 接管结果。
