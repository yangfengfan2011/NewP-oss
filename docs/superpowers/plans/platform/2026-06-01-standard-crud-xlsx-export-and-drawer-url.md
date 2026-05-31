# Standard CRUD XLSX Export And Drawer URL Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Add reusable Standard CRUD `.xlsx` export and smoother drawer URL synchronization without adding module-specific code.

**Architecture:** The backend owns the export endpoint and XLSX file generation. The frontend Host reads `view.export.modes`, sends the current view columns and query to the backend, and synchronizes drawer state with URL query parameters.

**Tech Stack:** .NET Standard CRUD minimal APIs, BCL `ZipArchive` XLSX writer, React + TypeScript + Vite, Vitest.

---

## Files

- Create: `server/platform-crud/Models/StandardCrudExportColumn.cs`
- Create: `server/platform-crud/Models/StandardCrudExportRequest.cs`
- Create: `server/platform-crud/Models/StandardCrudExportFile.cs`
- Create: `server/platform-crud/Exports/StandardCrudXlsxWriter.cs`
- Create: `server/platform-crud/Runtime/StandardCrudExportService.cs`
- Modify: `server/platform-crud/Extensions/StandardCrudEndpointRouteBuilderExtensions.cs`
- Modify: `server/platform-crud/Extensions/StandardCrudServiceCollectionExtensions.cs`
- Modify: `tests/PlatformCrud.Tests/StandardCrudEndpointRouteBuilderTests.cs`
- Modify: `frontend/packages/sdk/src/standardCrud.ts`
- Modify: `frontend/packages/ui/src/pro-adapters/SearchTablePage.tsx`
- Modify: `frontend/packages/shell/src/pages/StandardCrudListPageHost.tsx`
- Modify: `frontend/packages/shell/src/__tests__/standard-crud-list-host.test.tsx`
- Modify: `frontend/packages/shell/src/__tests__/person-center-crud-pro-sample.test.tsx`

## Task 1: Backend Export Endpoint

- [x] **Step 1: Write failing backend route tests**

Add tests to `tests/PlatformCrud.Tests/StandardCrudEndpointRouteBuilderTests.cs`:

```csharp
[Fact]
public async Task MapStandardCrudResources_ExportsCurrentPageAsXlsx()
{
    var recording = new RecordingStandardCrudService
    {
        ListResult = new StandardCrudListResult(
            [new Dictionary<string, object?> { ["EID"] = "Person_1", ["PersonNo"] = "P001", ["ENAME"] = "张三" }],
            1,
            2,
            5,
            null,
            null)
    };
    await using var app = BuildApp(recording);
    await app.StartAsync();

    var response = await app.GetTestClient().PostAsJsonAsync(
        "/api/organization/persons/export",
        new
        {
            mode = "current",
            query = new { pageIndex = 2, pageSize = 5, filters = new Dictionary<string, string?> { ["keyword"] = "张" } },
            columns = new[]
            {
                new { key = "personNo", field = "PersonNo", title = "人员编号" },
                new { key = "name", field = "ENAME", title = "姓名" },
                new { key = "blocked", field = "SecretField", title = "秘密字段" }
            }
        });

    response.EnsureSuccessStatusCode();
    Assert.Equal("application/vnd.openxmlformats-officedocument.spreadsheetml.sheet", response.Content.Headers.ContentType?.MediaType);
    Assert.Equal(2, recording.LastListQuery!.Page);
    Assert.Equal(5, recording.LastListQuery.PageSize);
    Assert.Equal("张", recording.LastListQuery.Filters!["keyword"]);
    var bytes = await response.Content.ReadAsByteArrayAsync();
    Assert.True(bytes.Length > 0);
    AssertXlsxContains(bytes, "人员编号", "张三");
    AssertXlsxDoesNotContain(bytes, "秘密字段");
}

[Fact]
public async Task MapStandardCrudResources_ExportsAllWithNormalizedPaging()
{
    var recording = new RecordingStandardCrudService();
    await using var app = BuildApp(recording);
    await app.StartAsync();

    var response = await app.GetTestClient().PostAsJsonAsync(
        "/api/organization/persons/export",
        new
        {
            mode = "all",
            query = new { pageIndex = 3, pageSize = 20, filters = new Dictionary<string, string?> { ["keyword"] = "张" } },
            columns = new[] { new { key = "name", field = "ENAME", title = "姓名" } }
        });

    response.EnsureSuccessStatusCode();
    Assert.Equal(1, recording.LastListQuery!.Page);
    Assert.True(recording.LastListQuery.PageSize > 20);
    Assert.Equal("张", recording.LastListQuery.Filters!["keyword"]);
}
```

- [x] **Step 2: Run backend tests and verify RED**

Run: `dotnet test tests/PlatformCrud.Tests/PlatformCrud.Tests.csproj --filter StandardCrudEndpointRouteBuilderTests`

Expected: FAIL because `/export` is not mapped.

- [x] **Step 3: Implement export models, service, writer and route**

Add request/result models, a minimal XLSX writer under `server/platform-crud/Exports`, and `StandardCrudExportService` that validates columns against `descriptor.FieldSet.ListFields`. Map `POST {route}/export` before `GET {route}/{id}` and include it in route conflict checks.

- [x] **Step 4: Run backend tests and verify GREEN**

Run: `dotnet test tests/PlatformCrud.Tests/PlatformCrud.Tests.csproj --filter StandardCrudEndpointRouteBuilderTests`

Expected: PASS.

## Task 2: Frontend SDK And Host Export

- [x] **Step 1: Write failing frontend Host test**

Add a test in `frontend/packages/shell/src/__tests__/standard-crud-list-host.test.tsx` that renders a view with `export.modes`, clicks `导出当前`, and asserts `fetch` receives `POST /api/organization/persons/export` with mode `current` and columns from `view.columns`.

- [x] **Step 2: Run frontend test and verify RED**

Run: `pnpm --dir frontend --filter @icp-platform/shell test -- standard-crud-list-host.test.tsx`

Expected: FAIL because export buttons are not rendered.

- [x] **Step 3: Add SDK export client and Host toolbar**

Extend `StandardCrudClient` with `exportList`. In Host, build export columns from runtime columns, render export buttons when `schema.exportModes` contains `current` or `all`, and download the returned blob.

- [x] **Step 4: Run frontend test and verify GREEN**

Run: `pnpm --dir frontend --filter @icp-platform/shell test -- standard-crud-list-host.test.tsx`

Expected: PASS.

## Task 3: Drawer URL Synchronization

- [x] **Step 1: Write failing URL sync tests**

Add tests that open create/view/edit drawers from user actions and verify `drawer/id` appear in the URL, then close the drawer and verify `drawer/id` are removed while unrelated params remain.

- [x] **Step 2: Run frontend tests and verify RED**

Run: `pnpm --dir frontend --filter @icp-platform/shell test -- person-center-crud-pro-sample.test.tsx`

Expected: FAIL because user actions do not currently update URL and close does not clear URL.

- [x] **Step 3: Implement URL helpers in Host**

Use `useNavigate` and `useLocation`. User-action drawer opens push query params. Route-compatibility opens do not push again. Close uses `replace: true` to remove only `drawer` and `id`.

- [x] **Step 4: Run frontend tests and verify GREEN**

Run: `pnpm --dir frontend --filter @icp-platform/shell test -- person-center-crud-pro-sample.test.tsx`

Expected: PASS.

## Task 4: Final Verification

- [x] **Step 1: Run design checks**

Run:

```powershell
powershell -ExecutionPolicy Bypass -File .\scripts\validate-ai-first-design.ps1 -Path .\docs\superpowers\specs\platform\2026-06-01-standard-crud-xlsx-export-and-drawer-url-design.md
powershell -ExecutionPolicy Bypass -File .\scripts\check-ai-first-design-targets.ps1
```

Expected: both PASS.

- [x] **Step 2: Run targeted backend and frontend verification**

Run:

```powershell
dotnet test tests/PlatformCrud.Tests/PlatformCrud.Tests.csproj --filter StandardCrud
pnpm --dir frontend --filter @icp-platform/shell test -- standard-crud-list-host.test.tsx person-center-crud-pro-sample.test.tsx
pnpm --dir frontend --filter @icp-platform/shell build
```

Expected: all PASS.

## Self-Review

- Spec coverage: backend export, frontend export trigger, `view.columns` as true source, descriptor whitelist, drawer URL sync, verification and rollback are covered.
- 未定事项扫描：没有残留未定义事项。
- Type consistency: request, column and mode names match between backend, SDK and Host.
