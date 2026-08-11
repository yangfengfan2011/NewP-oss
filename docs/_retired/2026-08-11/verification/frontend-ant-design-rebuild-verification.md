# Frontend Ant Design Rebuild Verification

## Final Acceptance

- Scope: Shell Ant Design chrome, UI Pro Components adapters, Standard CRUD personnel sample, and Audit low-risk pages.
- Commands:
  - `pnpm --dir frontend --filter @icp-platform/shell test`
  - `pnpm --dir frontend --filter @icp-platform/ui build`
  - `pnpm --dir frontend --filter @icp-platform/shell build`
- Result:
  - PASS: Shell test completed with 41 files / 141 tests passed.
  - PASS: UI build completed with `tsc`.
  - PASS: Shell build completed with `tsc && vite build`.
  - Note: UI build was run before the final Shell build because this change updates exported UI adapter declarations consumed by Shell.
  - Note: Shell build still reports the existing Vite chunk size warning; no build failure.
- Completed phases:
  - Phase 1: Shell Layout / Header / Sidebar / PageContainer / Ant Design theme.
  - Phase 2: UI package Pro Components adapters.
  - Phase 3: Personnel center Standard CRUD list/detail sample using adapters.
  - Phase 4: Audit operation/access log pages rebuilt on the table adapter.
- Not covered:
  - Config management pages.
  - Authorization center pages.
  - File center pages.
  - Plugin operations pages.
  - Metadata configuration center pages.
- Current next-step guidance: choose the next CRUD Pro sample or page-family rebuild through a separate design/plan; Config management is only one candidate page family, not the default next step.
- Known risks:
  - Ant Design / Pro Components emit jsdom warnings such as `findDOMNode`, `contentStyle` deprecation, and React Router future-flag notices during tests. Assertions pass; these warnings are not current failure signals.
  - Shell bundle still has a large main chunk after Ant Design and Pro Components introduction. Code splitting or manual chunks should be a separate design.
- Rollback:
  - Phase 1 rollback: remove Shell Ant Design theme/chrome/menu/page container changes and Shell dependencies.
  - Phase 2 rollback: remove UI Pro Components adapters and UI dependencies.
  - Phase 3 rollback: revert Standard CRUD host/personnel resource/test changes.
  - Phase 4 rollback: revert only audit log pages and their tests.

## 人员中心后台 CRUD Pro 样板固化验证

- 范围：标准 CRUD 列表工作台、详情抽屉、表单抽屉、刷新、删除协议、可选停用动作、旧新建路由兼容。
- 结果：人员中心新增/查看/编辑默认使用右侧抽屉；独立详情页保留为深链接和回滚出口；`disable` 作为人员资源可选动作保留；人员中心显式声明 `delete`，删除走确认协议并调用既有标准 CRUD DELETE 端点。
- 验证命令：
  - `pnpm --dir frontend --filter @icp-platform/shell test -- person-center-crud-pro-sample organization-person-center-list-layout organization-person-center-detail-layout standard-crud-sdk`
  - `pnpm --dir frontend --filter @icp-platform/sdk build`
  - `pnpm --dir frontend --filter @icp-platform/ui build`
  - `pnpm --dir frontend --filter @icp-platform/shell build`
  - `pnpm --dir frontend --filter @icp-modules/organization build`
- 验证结果：
  - PASS：Shell 定向测试通过，4 files / 27 tests。
  - PASS：SDK、UI、Shell、Organization 构建通过。
  - Note：Shell 构建仍输出既有 Vite chunk size warning；测试仍输出 AntD / Pro Components 的 `findDOMNode`、`contentStyle` deprecation 和 React Router future flag 警告，均未导致失败。
- 已知风险：人员删除本轮先沿用资源级权限边界；独立删除权限后续需要另起设计细化。已绑定账号等业务冲突由 `PersonCrudDeleteGuard` 拒绝，并应在危险动作区域可见展示。
- 回滚：回退 UI CRUD Pro adapter、Shell standard CRUD Host 抽屉状态机、路由兼容和人员资源动作声明；独立详情页仍可作为回滚入口。

## Phase 1

- Scope: Shell Layout / Header / Sidebar / PageContainer / Ant Design theme.
- Commands:
  - `pnpm --dir frontend --filter @icp-platform/shell test`
  - `pnpm --dir frontend --filter @icp-platform/shell build`
- Result:
  - PASS: Shell test completed with 39 files / 137 tests passed.
  - PASS: Shell build completed with `tsc && vite build`.
  - Note: Build reported a Vite chunk size warning after Ant Design dependency introduction; no build failure.
- Known risks:
  - Ant Design `Menu` in jsdom emits React `act(...)` warnings during tests; assertions pass and no runtime failure was observed in automated verification.
  - Phase 1 only rebuilds Shell chrome. Module page bodies still include legacy `sh-*` UI until later phases.
- Rollback: Revert `frontend/packages/shell` Ant Design dependencies, theme, menu/page chrome adapters, Layout, Header, Sidebar, PageContainer, Shell CSS changes, and Phase 1 tests.
