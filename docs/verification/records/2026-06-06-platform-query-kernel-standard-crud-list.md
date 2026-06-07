# 平台查询内核与 Standard CRUD 列表接入验证记录

日期：2026-06-07

## 范围

- `server/platform-query` 查询契约、字段规划和 EF executor。
- Standard CRUD List 首个接入平台查询内核。
- 人员中心结构化 display value 与批量解析。
- Shell 列表 `fields` 参数、保留查询键护栏与真实 metadata 字段契约。
- Standard CRUD 导出 `current/all` 字段边界。

## 已执行验证

```powershell
dotnet build server\platform-query\PlatformQuery.csproj -nr:false -v minimal
dotnet build apps\host-api\HostApi.csproj -nr:false -v minimal
dotnet test tests\PlatformCrud.Tests\PlatformCrud.Tests.csproj --filter StandardCrud -nr:false -v minimal
dotnet test tests\Organization.Tests\Organization.Tests.csproj -nr:false -v minimal
pnpm --dir frontend --filter @icp-platform/shell test -- standard-crud-schema-and-registry standard-crud-metadata-query standard-crud-sdk standard-crud-list-host person-center-crud-pro-sample
pnpm --dir frontend --filter @icp-platform/shell build
powershell -ExecutionPolicy Bypass -File .\scripts\validate-ai-first-design.ps1 -Path .\docs\superpowers\specs\platform\2026-06-06-platform-query-kernel-standard-crud-list-design.md
powershell -ExecutionPolicy Bypass -File .\scripts\check-ai-first-design-targets.ps1
```

## 结果

- 通过。
- `server/platform-query` 构建通过，0 warnings / 0 errors。
- `apps/host-api` 构建通过，0 warnings / 0 errors。
- `PlatformCrud.Tests` 中 `StandardCrud` 相关测试通过，63 tests。
- `Organization.Tests` 全量通过，47 tests。
- Shell 定向测试通过，5 files / 71 tests。
- Shell build 通过，`tsc && vite build` 成功。
- AI-first 设计校验和 target 检查通过。
- Shell 测试仍输出既有 jsdom / AntD / React Router warnings；Shell build 仍输出既有 Vite chunk-size warning，未作为本轮失败信号。

## 通过标准对照

- 列表请求会按当前 view columns 与 row action `visibleWhen.field` 提交 `fields`，且默认包含 `EID`。
- metadata query key 已拒绝 `fields / page / pageIndex / pageSize / sortBy / sortDirection` 等宿主保留键，避免视图筛选键与标准 CRUD 传输参数冲突。
- 人员中心真实 view metadata 已与后端字段契约对齐，`DepartmentName` 不再因大小写不一致触发查询字段校验失败。
- Standard CRUD / Organization / Shell 三侧验证均覆盖到本轮新增边界。

## 回滚

- 如需整体回退本计划，可按提交逆序回退：`d00a6e6d`、`31454f33`、`3f9c4b99`、`8c32e628`、`aff575e0`、`47039fc0`。
- 如只需回退 Shell `fields` 传参与保留键护栏，可优先回退 `d00a6e6d`。
- 如需回退查询内核接入，可继续逆序回退 `31454f33` 与 `3f9c4b99`，恢复原先 Standard CRUD list 查询路径。
