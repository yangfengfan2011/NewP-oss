# 标准 CRUD 元数据回收验证记录

## 范围

- 人员中心标准 CRUD metadata 回收。
- Shell 标准 CRUD schema resolver、List Host、Detail Host。
- Organization 人员资源注册收缩。
- 后端人员 projection 字段契约。

## 已执行验证

```powershell
./scripts/validate-ai-first-design.ps1 -Path docs/superpowers/specs/platform/2026-05-24-standard-crud-metadata-recovery-design.md
./scripts/check-ai-first-design-targets.ps1
./scripts/validate-dic-localization.ps1
pnpm --dir frontend --filter @icp-platform/shell test -- standard-crud-schema-and-registry standard-crud-metadata-form standard-crud-metadata-query standard-crud-list-host standard-crud-detail-host person-center-crud-pro-sample organization-person-phase2-shrink
pnpm --dir frontend --filter @icp-platform/shell build
dotnet test tests\Organization.Tests\Organization.Tests.csproj -nr:false -v minimal
dotnet test tests\PlatformCrud.Tests\PlatformCrud.Tests.csproj -nr:false -v minimal
```

## 结果

- 通过。
- `@icp-platform/shell` 标准 CRUD 定向测试通过，7 files / 54 tests。
- `@icp-platform/shell` 构建通过，`tsc && vite build` 成功。
- `Organization.Tests` 通过，43 tests。
- `PlatformCrud.Tests` 通过，58 tests。
- Shell 构建仍输出既有 Vite chunk size warning；jsdom 测试仍输出既有 AntD / React Router 警告，未作为本轮失败信号。

## 回滚

- 回滚 Shell metadata resolver/Host 改动。
- 恢复 `personStandardCrudResource.tsx` 中字段、列、查询、动作声明。
- 反向回退 `modules/Organization/metadata/**` 中本轮 schema 调整。
- 如只需回退后端 projection 契约测试，可反向回退 `tests/Organization.Tests/PersonStandardCrudResourceTests.cs` 与 `tests/PlatformCrud.Tests/StandardCrudServiceReadTests.cs` 中本轮断言。
