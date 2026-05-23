# Standard CRUD Metadata Recovery Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** 将人员中心标准 CRUD 从前端注册代码声明，收敛为 `view/model/dic/enum metadata` 声明和 Shell/SDK 代码内核执行。

**Architecture:** `modules/Organization/metadata/**` 成为人员中心字段、查询、列表列、动作和文案真源；`frontend/packages/shell` 负责解析、校验、渲染、状态机和错误边界；`frontend/modules/organization` 只保留 provider、renderer、handler、list/detail mapper 和真正必要的模块级提交适配。第一批只回收人员中心，不复制到其他模块。

**Tech Stack:** React 18、TypeScript、Vite、Vitest、Ant Design v5、pnpm workspace、.NET 标准 CRUD 后端测试。

---

## 前置要求

- 实施前保持当前分支，不切换分支；如工作区已有未提交内容，先确认是否属于本计划。
- 设计入口：`docs/superpowers/specs/platform/2026-05-24-standard-crud-metadata-recovery-design.md`。
- 每个 Task 完成后按任务内命令验证并提交，提交信息使用简体中文。
- 本计划只实施人员中心标准 CRUD metadata 回收，不恢复元数据配置中心开发，不新增 view adapter，不新增 action/control/i18n metadata 类型。

## 文件结构

- 修改 `frontend/packages/sdk/src/standardCrud.ts`
  - 扩展标准 CRUD 字段、动作、列、查询、表单和 schema 类型。
  - 让资源注册中的声明型字段、分组、列、查询、动作能力从必需/主路径降级为兼容扩展。
- 修改 `frontend/packages/ui/src/patterns/StandardCrudField.tsx`
  - 支持 V1 `uiType` 对应控件：`text/textarea/number/switch/checkbox/select/lookup/date/dateRange/readonly`。
  - 静态 UI 文案仍通过调用方传入的已翻译 label，不在 UI 包读取 metadata。
- 修改 `frontend/packages/ui/src/patterns/StandardCrudForm.tsx`
  - 接收已由 Shell 翻译后的字段与 section 标题，保留 required 校验和提交入口。
- 修改 `frontend/packages/shell/src/metadata/standardCrudSchemaResolver.ts`
  - 扩展为 `view + model + registration + localization catalog` 合成入口。
  - 校验 model field、query defaults、projection field、action、dataSource、uiType 和 dic key。
- 新建 `frontend/packages/shell/src/standard-crud/standardCrudMetadataForm.ts`
  - 将 model sections/fields 转换为 `StandardCrudFieldDefinition[]` 和 `StandardCrudFormSection[]`。
  - 字段 key 直接使用后端/metadata 暴露的模型字段名；不在前端新增 `EID / ENAME / ECODE` 或大小写友好名映射。
- 新建 `frontend/packages/shell/src/standard-crud/standardCrudMetadataQuery.ts`
  - 根据 query schema 生成空筛选、默认筛选、提交查询参数和筛选控件配置。
- 修改 `frontend/packages/shell/src/pages/StandardCrudListPageHost.tsx`
  - 列表查询、默认查询、列、页面动作、行动作和表单字段优先来自 metadata schema。
  - 特殊 renderer、provider、handler、mapper 仍来自资源注册。
- 修改 `frontend/packages/shell/src/pages/StandardCrudDetailPageHost.tsx`
  - 详情 sections、summaryBlocks、pageActions 和编辑表单字段优先来自 metadata schema。
- 修改 `frontend/packages/shell/src/standard-crud/standardCrudActionRuntime.tsx`
  - 动作文案、确认文案、权限和 handlerKey 按 metadata schema 执行。
- 修改 `modules/Organization/metadata/views/PersonList.view.json`
  - 移除公司筛选和公司列。
  - 补齐 `defaults/defaultSort/defaultPageSize`、`source/field`、`standardAction/variant/permissions/handlerKey/confirmMessageKey`。
- 修改 `modules/Organization/metadata/views/PersonDetail.view.json`
  - 补齐详情动作 `standardAction/variant/permissions/handlerKey/confirmMessageKey`。
- 修改 `modules/Organization/metadata/models/Person.model.json`
  - 补齐 `uiType/form/view/dataSource/default/required`。
  - 保留 `CompanyId` 作为过渡表单字段，不放回列表筛选和列表列。
- 修改 `modules/Organization/metadata/dictionaries/PersonListView.dic.json`
  - 补齐列表动作、确认提示、查询项、列、按钮和通用状态文案的 `zhs/en`。
  - 删除或保留未引用的公司列表文案均可；本计划不要求清理无害历史 entry。
- 修改 `modules/Organization/metadata/dictionaries/PersonDetailView.dic.json`
  - 补齐详情动作确认文案和表单 section 文案的 `zhs/en`。
- 修改 `frontend/modules/organization/src/standard-crud/personStandardCrudResource.tsx`
  - 删除 `PersonListFiltersPanel`、`personFields`、`personFormSections`、`listActions`、`detailActions`、`buildColumns`、`renderFilters` 的长期声明。
  - 保留 lookup providers、summary renderers、action handlers、list/detail mapper、row id，以及真正必要的模块级提交适配。
  - mutation mapper 只允许处理模块私有提交差异，不允许用于平台协议字段转换。
- 删除 `frontend/modules/organization/src/runtime/personCenterRuntime.ts`
  - 该文件存在私有 fallback schema，实施后会与平台 metadata resolver 形成双真源。
- 删除 `frontend/modules/organization/src/runtime/personCenterRuntime.types.ts`
  - 类型随私有 runtime 一并退场。
- 修改或新增测试：
  - `frontend/packages/shell/src/__tests__/standard-crud-schema-and-registry.test.ts`
  - `frontend/packages/shell/src/__tests__/standard-crud-metadata-form.test.ts`
  - `frontend/packages/shell/src/__tests__/standard-crud-metadata-query.test.ts`
  - `frontend/packages/shell/src/__tests__/standard-crud-list-host.test.tsx`
  - `frontend/packages/shell/src/__tests__/standard-crud-detail-host.test.tsx`
  - `frontend/packages/shell/src/__tests__/person-center-crud-pro-sample.test.tsx`
  - `frontend/packages/shell/src/__tests__/organization-person-phase2-shrink.test.tsx`
  - `frontend/packages/shell/src/__tests__/vite-config.test.ts`
  - `tests/Organization.Tests/PersonStandardCrudResourceTests.cs`
- 修改文档：
  - `docs/phases/current-handoff.md`
  - `docs/verification/verification-matrix.md` 或新增本轮验证记录，按仓库现有记录方式落地。

## Task 1：用失败测试锁定 metadata schema 合成与校验

**Files:**
- Modify: `frontend/packages/shell/src/__tests__/standard-crud-schema-and-registry.test.ts`
- Create: `frontend/packages/shell/src/__tests__/standard-crud-metadata-form.test.ts`
- Create: `frontend/packages/shell/src/__tests__/standard-crud-metadata-query.test.ts`
- Verify: `pnpm --dir frontend --filter @icp-platform/shell test -- standard-crud-schema-and-registry standard-crud-metadata-form standard-crud-metadata-query`

- [x] **Step 1: 扩展 schema resolver 测试，先写失败用例**

在 `frontend/packages/shell/src/__tests__/standard-crud-schema-and-registry.test.ts` 追加：

```ts
it('builds list schema with metadata-owned columns actions defaults and localization checks', () => {
  const schema = buildStandardCrudListSchema(
    {
      key: 'organization.person.list',
      dicKey: 'PersonListView',
      titleKey: 'Title',
      descriptionKey: 'Description',
      mode: 'list',
      model: 'Person',
      query: {
        fields: [
          { key: 'keyword', labelKey: 'Query_Keyword', source: 'preset', preset: 'keyword', operator: 'like' },
          { key: 'employmentStatus', labelKey: 'Query_EmploymentStatus', source: 'model', field: 'EmploymentStatus', operator: '=' },
        ],
        advancedFilter: {
          enabled: true,
          sections: [{
            key: 'organization',
            fields: [
              { key: 'departmentId', labelKey: 'Query_Department', source: 'model', field: 'DepartmentId', operator: '=' },
              { key: 'positionId', labelKey: 'Query_Position', source: 'model', field: 'PositionId', operator: '=' },
              { key: 'isEnabled', labelKey: 'Query_IsEnabled', source: 'model', field: 'IsEnabled', operator: '=' },
            ],
          }],
        },
        defaults: [{ field: 'isEnabled', value: true }],
        defaultSort: { field: 'PersonNo', direction: 'asc' },
        defaultPageSize: 20,
      },
      columns: [
        { key: 'personNo', labelKey: 'Column_PersonNo', source: 'model', field: 'PersonNo', width: 140, sortable: true },
        { key: 'departmentName', labelKey: 'Column_DepartmentName', source: 'projection', field: 'departmentName' },
      ],
      pageActions: [
        { key: 'create', labelKey: 'Action_Create', standardAction: 'create', variant: 'primary', permissions: ['Organization.Person.Create'] },
      ],
      rowActions: [
        { key: 'disable', labelKey: 'Action_Disable', standardAction: 'disable', variant: 'danger', confirmMessageKey: 'Action_Disable_Confirm', handlerKey: 'disable' },
      ],
    },
    personRegistration,
    {
      modelDocuments: [{
        type: 'model',
        Person: {
          builtinFields: ['ENAME'],
          fields: [
            { name: 'PersonNo', storage: { type: 'string' } },
            { name: 'DepartmentId', storage: { type: 'guid' }, dataSource: { type: 'provider', key: 'organization.departments' } },
            { name: 'PositionId', storage: { type: 'guid' }, dataSource: { type: 'provider', key: 'organization.positions', dependsOn: ['DepartmentId'] } },
            { name: 'EmploymentStatus', storage: { type: 'string' }, contract: { valueType: 'enum', clrType: 'EmploymentStatus' }, dataSource: { type: 'enum', key: 'EmploymentStatus' } },
            { name: 'IsEnabled', storage: { type: 'bool' } },
          ],
        },
      }],
      defaultLanguageCatalog: {
        'PersonListView.Title': '人员中心',
        'PersonListView.Description': '维护组织模块中的人员基础信息。',
        'PersonListView.Query_Keyword': '关键字',
        'PersonListView.Query_EmploymentStatus': '在职状态',
        'PersonListView.Query_Department': '部门',
        'PersonListView.Query_Position': '岗位',
        'PersonListView.Query_IsEnabled': '启用状态',
        'PersonListView.Column_PersonNo': '人员编号',
        'PersonListView.Column_DepartmentName': '部门',
        'PersonListView.Action_Create': '新建',
        'PersonListView.Action_Disable': '停用',
        'PersonListView.Action_Disable_Confirm': '确认停用该人员？',
      },
    },
  );

  expect(schema.query.defaults).toEqual([{ field: 'isEnabled', value: true }]);
  expect(schema.query.defaultSort).toEqual({ field: 'PersonNo', direction: 'asc' });
  expect(schema.query.defaultPageSize).toBe(20);
  expect(schema.columns).toEqual([
    { key: 'personNo', labelKey: 'Column_PersonNo', source: 'model', field: 'PersonNo', width: 140, sortable: true },
    { key: 'departmentName', labelKey: 'Column_DepartmentName', source: 'projection', field: 'departmentName' },
  ]);
  expect(schema.pageActions[0]).toMatchObject({
    key: 'create',
    standardAction: 'create',
    variant: 'primary',
    requiredPermissions: ['Organization.Person.Create'],
  });
  expect(schema.rowActions[0]).toMatchObject({
    key: 'disable',
    confirmMessageKey: 'Action_Disable_Confirm',
    handlerKey: 'disable',
  });
});
```

- [x] **Step 2: 为校验失败补测试**

继续追加：

```ts
it('fails when projection column has no field', () => {
  expect(() => buildStandardCrudListSchema(
    {
      key: 'organization.person.list',
      dicKey: 'PersonListView',
      titleKey: 'Title',
      mode: 'list',
      model: 'Person',
      columns: [{ key: 'departmentName', labelKey: 'Column_DepartmentName', source: 'projection' }],
    },
    personRegistration,
    {
      modelDocuments: [{ type: 'model', Person: { fields: [] } }],
      defaultLanguageCatalog: {
        'PersonListView.Title': '人员中心',
        'PersonListView.Column_DepartmentName': '部门',
      },
    },
  )).toThrow('projection column "departmentName" must declare field');
});

it('fails when a metadata label key is missing from default language catalog', () => {
  expect(() => buildStandardCrudListSchema(
    {
      key: 'organization.person.list',
      dicKey: 'PersonListView',
      titleKey: 'Title',
      mode: 'list',
      model: 'Person',
      pageActions: [{ key: 'create', labelKey: 'Action_Create', standardAction: 'create' }],
    },
    personRegistration,
    {
      modelDocuments: [{ type: 'model', Person: { fields: [] } }],
      defaultLanguageCatalog: {
        'PersonListView.Title': '人员中心',
      },
    },
  )).toThrow('missing dictionary key "PersonListView.Action_Create"');
});

it('fails when query defaults reference an undeclared query field', () => {
  expect(() => buildStandardCrudListSchema(
    {
      key: 'organization.person.list',
      dicKey: 'PersonListView',
      titleKey: 'Title',
      mode: 'list',
      model: 'Person',
      query: {
        fields: [{ key: 'keyword', labelKey: 'Query_Keyword', source: 'preset', preset: 'keyword' }],
        defaults: [{ field: 'isEnabled', value: true }],
      },
    },
    personRegistration,
    {
      modelDocuments: [{ type: 'model', Person: { fields: [] } }],
      defaultLanguageCatalog: {
        'PersonListView.Title': '人员中心',
        'PersonListView.Query_Keyword': '关键字',
      },
    },
  )).toThrow('query default "isEnabled" does not reference a declared query field');
});

it('fails when a list view references an unknown model', () => {
  expect(() => buildStandardCrudListSchema(
    {
      key: 'organization.person.list',
      dicKey: 'PersonListView',
      titleKey: 'Title',
      mode: 'list',
      model: 'MissingPerson',
    },
    personRegistration,
    {
      modelDocuments: [{ type: 'model', Person: { fields: [] } }],
      defaultLanguageCatalog: {
        'PersonListView.Title': '人员中心',
      },
    },
  )).toThrow('view "organization.person.list" references unknown model "MissingPerson"');
});

it('fails when a query field uses an unsupported preset', () => {
  expect(() => buildStandardCrudListSchema(
    {
      key: 'organization.person.list',
      dicKey: 'PersonListView',
      titleKey: 'Title',
      mode: 'list',
      model: 'Person',
      query: {
        fields: [{ key: 'quick', labelKey: 'Query_Quick', source: 'preset', preset: 'quickSearch' }],
      },
    },
    personRegistration,
    {
      modelDocuments: [{ type: 'model', Person: { fields: [] } }],
      defaultLanguageCatalog: {
        'PersonListView.Title': '人员中心',
        'PersonListView.Query_Quick': '快速搜索',
      },
    },
  )).toThrow('unsupported query preset "quickSearch"');
});

it('fails when a metadata action uses an unsupported standard action', () => {
  expect(() => buildStandardCrudListSchema(
    {
      key: 'organization.person.list',
      dicKey: 'PersonListView',
      titleKey: 'Title',
      mode: 'list',
      model: 'Person',
      rowActions: [{ key: 'archive', labelKey: 'Action_Archive', standardAction: 'archive' }],
    },
    personRegistration,
    {
      modelDocuments: [{ type: 'model', Person: { fields: [] } }],
      defaultLanguageCatalog: {
        'PersonListView.Title': '人员中心',
        'PersonListView.Action_Archive': '归档',
      },
    },
  )).toThrow('unsupported standard action "archive"');
});

it('fails when an action handlerKey is not registered on the current resource', () => {
  expect(() => buildStandardCrudListSchema(
    {
      key: 'organization.person.list',
      dicKey: 'PersonListView',
      titleKey: 'Title',
      mode: 'list',
      model: 'Person',
      rowActions: [{ key: 'disable', labelKey: 'Action_Disable', standardAction: 'disable', handlerKey: 'missingDisable' }],
    },
    { ...personRegistration, actionHandlers: { disable: async () => undefined } },
    {
      modelDocuments: [{ type: 'model', Person: { fields: [] } }],
      defaultLanguageCatalog: {
        'PersonListView.Title': '人员中心',
        'PersonListView.Action_Disable': '停用',
      },
    },
  )).toThrow('action handler "missingDisable" is not registered on resource "organization.person"');
});

it('fails when metadata declares an unsupported dataSource type', () => {
  expect(() => buildStandardCrudListSchema(
    {
      key: 'organization.person.list',
      dicKey: 'PersonListView',
      titleKey: 'Title',
      mode: 'list',
      model: 'Person',
      query: {
        fields: [{ key: 'departmentId', labelKey: 'Query_Department', source: 'model', field: 'DepartmentId' }],
      },
    },
    personRegistration,
    {
      modelDocuments: [{
        type: 'model',
        Person: {
          fields: [{ name: 'DepartmentId', dataSource: { type: 'tree' } }],
        },
      }],
      defaultLanguageCatalog: {
        'PersonListView.Title': '人员中心',
        'PersonListView.Query_Department': '部门',
      },
    },
  )).toThrow('unsupported dataSource type "tree"');
});
```

- [x] **Step 3: 新建 metadata form 测试**

创建 `frontend/packages/shell/src/__tests__/standard-crud-metadata-form.test.ts`：

```ts
import { describe, expect, it } from 'vitest';
import { buildStandardCrudFormSchemaFromModel } from '../standard-crud/standardCrudMetadataForm';

describe('standard CRUD metadata form schema', () => {
  const personModel = {
    builtinFields: ['ENAME'],
    sections: [
      { key: 'basic', titleKey: 'Section_Basic', fields: ['PersonNo', 'ENAME'] },
      { key: 'organization', titleKey: 'Section_Organization', fields: ['DepartmentId', 'PositionId', 'EmploymentStatus', 'IsEnabled'] },
    ],
    fields: [
      { name: 'ENAME', uiType: 'text', form: { create: { mode: 'editable', required: true }, edit: { mode: 'editable' } } },
      { name: 'PersonNo', storage: { type: 'string' }, uiType: 'text', form: { create: { mode: 'editable', required: true }, edit: { mode: 'readonly' } } },
      { name: 'DepartmentId', storage: { type: 'guid', nullable: true }, uiType: 'lookup', form: { create: { mode: 'editable' }, edit: { mode: 'editable' } }, dataSource: { type: 'provider', key: 'organization.departments' } },
      { name: 'PositionId', storage: { type: 'guid', nullable: true }, uiType: 'lookup', form: { create: { mode: 'editable' }, edit: { mode: 'editable' } }, dataSource: { type: 'provider', key: 'organization.positions', dependsOn: ['DepartmentId'] } },
      { name: 'EmploymentStatus', storage: { type: 'string', nullable: true }, contract: { valueType: 'enum', clrType: 'EmploymentStatus' }, uiType: 'select', form: { create: { mode: 'editable', defaultValue: 'FullTime' }, edit: { mode: 'editable' } }, dataSource: { type: 'enum', key: 'EmploymentStatus' } },
      { name: 'IsEnabled', storage: { type: 'bool' }, uiType: 'switch', form: { create: { mode: 'editable', defaultValue: true }, edit: { mode: 'editable' } } },
    ],
  };

  it('normalizes platform field keys and form sections without metadata aliases', () => {
    const schema = buildStandardCrudFormSchemaFromModel(personModel, {
      mode: 'create',
      enumOptions: {
        EmploymentStatus: [{ value: 'FullTime', label: '正式' }],
      },
    });

    expect(schema.sections).toEqual([
      { key: 'basic', titleKey: 'Section_Basic', fields: ['PersonNo', 'ENAME'] },
      { key: 'organization', titleKey: 'Section_Organization', fields: ['DepartmentId', 'PositionId', 'EmploymentStatus', 'IsEnabled'] },
    ]);
    expect(schema.fields.map((field) => [field.key, field.modelField, field.kind])).toEqual([
      ['PersonNo', 'PersonNo', 'text'],
      ['ENAME', 'ENAME', 'text'],
      ['DepartmentId', 'DepartmentId', 'lookup'],
      ['PositionId', 'PositionId', 'lookup'],
      ['EmploymentStatus', 'EmploymentStatus', 'enum'],
      ['IsEnabled', 'IsEnabled', 'boolean'],
    ]);
    expect(schema.fields.find((field) => field.key === 'PersonNo')).toMatchObject({ required: true, readOnly: false });
    expect(schema.fields.find((field) => field.key === 'EmploymentStatus')).toMatchObject({ defaultValue: 'FullTime' });
    expect(schema.fields.find((field) => field.key === 'PositionId')?.lookup?.dependsOn).toEqual(['DepartmentId']);
  });

  it('applies edit mode readonly rules from model metadata', () => {
    const schema = buildStandardCrudFormSchemaFromModel(personModel, { mode: 'edit', enumOptions: {} });
    expect(schema.fields.find((field) => field.key === 'PersonNo')).toMatchObject({ readOnly: true });
  });

  it('rejects unsupported uiType before rendering', () => {
    expect(() => buildStandardCrudFormSchemaFromModel(
      { fields: [{ name: 'DepartmentId', uiType: 'treeSelect' }] },
      { mode: 'create', enumOptions: {} },
    )).toThrow('unsupported uiType "treeSelect"');
  });
});
```

- [x] **Step 4: 新建 metadata query 测试**

创建 `frontend/packages/shell/src/__tests__/standard-crud-metadata-query.test.ts`：

```ts
import { describe, expect, it } from 'vitest';
import {
  buildStandardCrudDefaultFilters,
  buildStandardCrudEmptyFilters,
  buildStandardCrudListQuery,
} from '../standard-crud/standardCrudMetadataQuery';

const querySchema = {
  fields: [
    { key: 'keyword', labelKey: 'Query_Keyword', source: 'preset' as const, preset: 'keyword', operator: 'like' },
    { key: 'employmentStatus', labelKey: 'Query_EmploymentStatus', source: 'model' as const, field: 'EmploymentStatus', operator: '=' },
  ],
  advancedFilter: {
    enabled: true,
    sections: [{
      key: 'organization',
      fields: [
        { key: 'departmentId', labelKey: 'Query_Department', source: 'model' as const, field: 'DepartmentId', operator: '=' },
        { key: 'positionId', labelKey: 'Query_Position', source: 'model' as const, field: 'PositionId', operator: '=' },
        { key: 'isEnabled', labelKey: 'Query_IsEnabled', source: 'model' as const, field: 'IsEnabled', operator: '=' },
      ],
    }],
  },
  defaults: [{ field: 'isEnabled', value: true }],
  defaultPageSize: 20,
};

describe('standard CRUD metadata query helpers', () => {
  it('builds empty and default filters from declared query keys', () => {
    expect(buildStandardCrudEmptyFilters(querySchema)).toEqual({
      keyword: '',
      employmentStatus: '',
      departmentId: '',
      positionId: '',
      isEnabled: '',
    });
    expect(buildStandardCrudDefaultFilters(querySchema)).toEqual({
      keyword: '',
      employmentStatus: '',
      departmentId: '',
      positionId: '',
      isEnabled: true,
    });
  });

  it('submits only declared keys and skips empty values', () => {
    expect(buildStandardCrudListQuery(querySchema, {
      keyword: '张',
      employmentStatus: '',
      departmentId: 'Dept_1',
      positionId: '',
      isEnabled: true,
      companyId: 'must-not-leak',
    })).toEqual({
      keyword: '张',
      departmentId: 'Dept_1',
      isEnabled: true,
    });
  });
});
```

- [x] **Step 5: 运行测试确认失败**

Run:

```powershell
pnpm --dir frontend --filter @icp-platform/shell test -- standard-crud-schema-and-registry standard-crud-metadata-form standard-crud-metadata-query
```

Expected:

```text
FAIL ... buildStandardCrudListSchema ... expected 3 arguments
FAIL ... Cannot find module '../standard-crud/standardCrudMetadataForm'
FAIL ... Cannot find module '../standard-crud/standardCrudMetadataQuery'
```

- [x] **Step 6: 提交失败测试**

Run:

```powershell
git add frontend/packages/shell/src/__tests__/standard-crud-schema-and-registry.test.ts frontend/packages/shell/src/__tests__/standard-crud-metadata-form.test.ts frontend/packages/shell/src/__tests__/standard-crud-metadata-query.test.ts
git commit -m "测试：锁定标准CRUD元数据schema回收行为"
```

## Task 2：扩展 SDK 与 UI 字段控件契约

**Files:**
- Modify: `frontend/packages/sdk/src/standardCrud.ts`
- Modify: `frontend/packages/ui/src/patterns/StandardCrudField.tsx`
- Modify: `frontend/packages/ui/src/patterns/StandardCrudForm.tsx`
- Modify: `frontend/packages/shell/src/__tests__/standard-crud-field-form.test.tsx`
- Modify: `frontend/packages/shell/src/__tests__/standard-crud-phase2-sdk.test.ts`
- Verify: `pnpm --dir frontend --filter @icp-platform/shell test -- standard-crud-field-form standard-crud-phase2-sdk`
- Verify: `pnpm --dir frontend --filter @icp-platform/ui build`
- Verify: `pnpm --dir frontend --filter @icp-platform/sdk build`

- [x] **Step 1: 先扩字段控件测试**

在 `frontend/packages/shell/src/__tests__/standard-crud-field-form.test.tsx` 增加：

```tsx
it('renders textarea number checkbox dateRange and readonly field kinds', async () => {
  const fields: StandardCrudFieldDefinition[] = [
    { key: 'remark', labelKey: '备注', modelField: 'Remark', kind: 'textarea' },
    { key: 'sortOrder', labelKey: '排序', modelField: 'SortOrder', kind: 'number' },
    { key: 'isPrimary', labelKey: '主岗', modelField: 'IsPrimary', kind: 'checkbox' },
    { key: 'entryRange', labelKey: '入职范围', modelField: 'EntryRange', kind: 'dateRange' },
    { key: 'createdAt', labelKey: '创建时间', modelField: 'CreatedAt', kind: 'readonly' },
  ];
  const values = { remark: 'hello', sortOrder: 3, isPrimary: true, entryRange: ['2026-01-01', '2026-01-31'], createdAt: '2026-01-01' };
  const onChange = vi.fn();

  await act(async () => {
    root.render(
      <StandardCrudForm
        formId="field-kind-form"
        mode="edit"
        fields={fields}
        sections={[]}
        values={values}
        onChange={onChange}
        onSubmit={() => undefined}
        saving={false}
        titleForSection={(titleKey) => titleKey}
      />,
    );
  });

  expect(container.querySelector('textarea')).not.toBeNull();
  expect(container.querySelector('input[type="number"]')).not.toBeNull();
  expect(container.textContent).toContain('主岗');
  expect(container.querySelectorAll('input[type="date"]')).toHaveLength(2);
  expect(container.textContent).toContain('2026-01-01');
});
```

- [x] **Step 2: 扩 SDK 类型**

在 `frontend/packages/sdk/src/standardCrud.ts` 中替换字段类型：

```ts
export type StandardCrudFieldKind =
  | 'text'
  | 'textarea'
  | 'number'
  | 'boolean'
  | 'switch'
  | 'checkbox'
  | 'enum'
  | 'select'
  | 'lookup'
  | 'date'
  | 'dateRange'
  | 'readonly';
```

在 `StandardCrudActionDefinition` 中增加 metadata 所需字段：

```ts
export interface StandardCrudActionDefinition {
  key: string;
  labelKey: string;
  variant?: 'default' | 'primary' | 'danger';
  standardAction?: 'create' | 'view' | 'edit' | 'refresh' | 'delete' | 'disable' | 'submit';
  confirmMessage?: string;
  confirmMessageKey?: string;
  requiredPermissions?: string[];
  handlerKey?: string;
}
```

在 `StandardCrudResourceRegistration` 中增加枚举候选项桥接。该字段只承载枚举 option 数据，不声明字段、列、查询或动作：

```ts
enumOptions?: Record<string, StandardCrudOption[]>;
```

把 `StandardCrudResourceRegistration` 中这些声明型成员改为可选兼容扩展：

```ts
buildColumns?: (context: StandardCrudListExtensionContext<TFilter, TRow>) => StandardCrudTableColumn<TRow>[];
renderFilters?: (context: StandardCrudListExtensionContext<TFilter, TRow>) => ReactNode;
```

- [x] **Step 3: 更新 `StandardCrudField` 控件渲染**

在 `frontend/packages/ui/src/patterns/StandardCrudField.tsx` 中保持 `boolean` 兼容，同时新增 `switch/checkbox/select/textarea/number/dateRange` 分支：

```tsx
const isBooleanField = field.kind === 'boolean' || field.kind === 'switch' || field.kind === 'checkbox';
const isSelectField = field.kind === 'enum' || field.kind === 'select' || field.kind === 'lookup';

if (isBooleanField) {
  if (field.kind === 'checkbox') {
    return (
      <div className={fieldClassName}>
        {renderLabel(field, readOnly)}
        <label className="np-standard-crud-checkbox-row">
          <input
            id={field.key}
            type="checkbox"
            checked={value === true}
            disabled={readOnly}
            onChange={(event) => onChange(field.key, event.currentTarget.checked)}
          />
          <span>{value === true ? '是' : '否'}</span>
        </label>
        {fieldError ? <div className="sh-form-error np-standard-crud-field-error">{fieldError}</div> : null}
      </div>
    );
  }

  return (
    <div className={fieldClassName}>
      {renderLabel(field, readOnly)}
      <div className="np-standard-crud-switch-row">
        <Switch
          id={field.key}
          className="np-standard-crud-switch"
          checked={value === true}
          disabled={readOnly}
          onChange={(checked) => onChange(field.key, checked)}
        />
        <span>{value === true ? '启用' : '停用'}</span>
      </div>
      {fieldError ? <div className="sh-form-error np-standard-crud-field-error">{fieldError}</div> : null}
    </div>
  );
}

if (isSelectField) {
  // 复用现有 Select 分支，条件从 field.kind === 'enum' || field.kind === 'lookup' 改成 isSelectField。
}

if (field.kind === 'textarea') {
  return (
    <div className={fieldClassName}>
      {renderLabel(field, readOnly)}
      <textarea
        id={field.key}
        className="sh-form-input np-standard-crud-input"
        value={value == null ? '' : String(value)}
        disabled={readOnly}
        onChange={(event) => onChange(field.key, event.target.value)}
      />
      {fieldError ? <div className="sh-form-error np-standard-crud-field-error">{fieldError}</div> : null}
    </div>
  );
}

if (field.kind === 'dateRange') {
  const [start, end] = Array.isArray(value) ? value : ['', ''];
  return (
    <div className={fieldClassName}>
      {renderLabel(field, readOnly)}
      <div className="np-standard-crud-date-range">
        <input
          id={`${field.key}-start`}
          className="sh-form-input np-standard-crud-input"
          type="date"
          value={typeof start === 'string' ? start : ''}
          disabled={readOnly}
          onChange={(event) => onChange(field.key, [event.target.value, typeof end === 'string' ? end : ''])}
        />
        <input
          id={`${field.key}-end`}
          className="sh-form-input np-standard-crud-input"
          type="date"
          value={typeof end === 'string' ? end : ''}
          disabled={readOnly}
          onChange={(event) => onChange(field.key, [typeof start === 'string' ? start : '', event.target.value])}
        />
      </div>
      {fieldError ? <div className="sh-form-error np-standard-crud-field-error">{fieldError}</div> : null}
    </div>
  );
}
```

把普通 input 的 `type` 改为：

```tsx
type={field.kind === 'date' ? 'date' : field.kind === 'number' ? 'number' : 'text'}
```

并把 number 的 `onChange` 标准化为：

```tsx
onChange={(event) => onChange(field.key, field.kind === 'number' ? Number(event.target.value) : event.target.value)}
```

- [x] **Step 4: 运行字段测试和构建**

Run:

```powershell
pnpm --dir frontend --filter @icp-platform/shell test -- standard-crud-field-form standard-crud-phase2-sdk
pnpm --dir frontend --filter @icp-platform/ui build
pnpm --dir frontend --filter @icp-platform/sdk build
```

Expected:

```text
Test Files ... passed
```

- [x] **Step 5: 提交 SDK/UI 契约扩展**

Run:

```powershell
git add frontend/packages/sdk/src/standardCrud.ts frontend/packages/ui/src/patterns/StandardCrudField.tsx frontend/packages/ui/src/patterns/StandardCrudForm.tsx frontend/packages/shell/src/__tests__/standard-crud-field-form.test.tsx frontend/packages/shell/src/__tests__/standard-crud-phase2-sdk.test.ts
git commit -m "功能：扩展标准CRUD字段控件契约"
```

## Task 3：实现 metadata form/query/schema resolver

**Files:**
- Create: `frontend/packages/shell/src/standard-crud/standardCrudMetadataForm.ts`
- Create: `frontend/packages/shell/src/standard-crud/standardCrudMetadataQuery.ts`
- Modify: `frontend/packages/shell/src/metadata/standardCrudSchemaResolver.ts`
- Modify: `frontend/packages/shell/src/__tests__/standard-crud-schema-and-registry.test.ts`
- Verify: `pnpm --dir frontend --filter @icp-platform/shell test -- standard-crud-schema-and-registry standard-crud-metadata-form standard-crud-metadata-query`

- [x] **Step 1: 实现 metadata form helper**

创建 `frontend/packages/shell/src/standard-crud/standardCrudMetadataForm.ts`：

```ts
import type { StandardCrudFieldDefinition, StandardCrudFormMode, StandardCrudFormSection, StandardCrudOption } from '@icp-platform/sdk';

type MetadataRecord = Record<string, unknown>;

export interface BuildStandardCrudFormSchemaInput {
  mode: StandardCrudFormMode;
  enumOptions: Record<string, StandardCrudOption[]>;
}

export interface StandardCrudMetadataFormSchema {
  fields: StandardCrudFieldDefinition[];
  sections: StandardCrudFormSection[];
}

const supportedUiTypes = new Set(['text', 'textarea', 'number', 'switch', 'checkbox', 'select', 'lookup', 'date', 'dateRange', 'readonly']);
const supportedDataSourceTypes = new Set(['enum', 'model', 'provider', 'static']);

function readRecord(value: unknown): MetadataRecord {
  return typeof value === 'object' && value !== null && !Array.isArray(value) ? value as MetadataRecord : {};
}

function readArray<T>(value: unknown): T[] {
  return Array.isArray(value) ? value as T[] : [];
}

export function normalizeStandardCrudFieldKey(modelField: string): string {
  return modelField;
}

function normalizeDependencyKey(modelField: string): string {
  return normalizeStandardCrudFieldKey(modelField);
}

function inferKind(field: MetadataRecord): StandardCrudFieldDefinition['kind'] {
  const uiType = typeof field.uiType === 'string' ? field.uiType : '';
  if (uiType.length > 0 && !supportedUiTypes.has(uiType)) {
    throw new Error(`unsupported uiType "${uiType}"`);
  }

  if (uiType === 'select') return 'enum';
  if (uiType === 'switch') return 'boolean';
  if (uiType.length > 0) return uiType as StandardCrudFieldDefinition['kind'];

  const contract = readRecord(field.contract);
  const dataSource = readRecord(field.dataSource);
  const storage = readRecord(field.storage);
  if (typeof dataSource.type === 'string' && !supportedDataSourceTypes.has(dataSource.type)) {
    throw new Error(`unsupported dataSource type "${dataSource.type}"`);
  }
  if (contract.valueType === 'enum' || dataSource.type === 'enum') return 'enum';
  if (dataSource.type === 'model' || dataSource.type === 'provider') return 'lookup';
  if (storage.type === 'bool') return 'boolean';
  if (storage.type === 'date' || storage.type === 'datetime') return 'date';
  return 'text';
}

function readModeConfig(field: MetadataRecord, mode: StandardCrudFormMode): MetadataRecord {
  const form = readRecord(field.form);
  return readRecord(form[mode]);
}

function buildField(field: MetadataRecord, input: BuildStandardCrudFormSchemaInput): StandardCrudFieldDefinition | null {
  const modelField = typeof field.name === 'string' ? field.name : '';
  if (modelField.length === 0) return null;

  const modeConfig = readModeConfig(field, input.mode);
  if (modeConfig.mode === 'hidden') return null;

  const dataSource = readRecord(field.dataSource);
  const kind = inferKind(field);
  const key = normalizeStandardCrudFieldKey(modelField);
  const result: StandardCrudFieldDefinition = {
    key,
    labelKey: `Field_${modelField}`,
    modelField,
    kind,
    required: modeConfig.required === true,
    readOnly: modeConfig.mode === 'readonly' || kind === 'readonly',
    defaultValue: Object.prototype.hasOwnProperty.call(modeConfig, 'defaultValue') ? modeConfig.defaultValue : undefined,
    emptyAsNull: readRecord(field.storage).nullable === true,
    trim: readRecord(field.storage).type === 'string',
  };

  if (kind === 'enum') {
    const enumKey = typeof dataSource.key === 'string'
      ? dataSource.key
      : typeof readRecord(field.contract).clrType === 'string'
        ? readRecord(field.contract).clrType as string
        : '';
    result.options = enumKey.length > 0 ? input.enumOptions[enumKey] ?? [] : [];
  }

  if (kind === 'lookup') {
    const providerKey = dataSource.type === 'provider' && typeof dataSource.key === 'string'
      ? dataSource.key
      : dataSource.type === 'model' && typeof dataSource.model === 'string'
        ? `model.${dataSource.model}`
        : '';
    result.lookup = {
      providerKey,
      dependsOn: readArray<string>(dataSource.dependsOn).map(normalizeDependencyKey),
      currentValueLabelField: `${modelField.replace(/Id$/, '')}Name`,
    };
  }

  return result;
}

export function buildStandardCrudFormSchemaFromModel(
  model: MetadataRecord,
  input: BuildStandardCrudFormSchemaInput,
): StandardCrudMetadataFormSchema {
  const rawFields = readArray<MetadataRecord>(model.fields);
  const fields = rawFields
    .map((field) => buildField(field, input))
    .filter((field): field is StandardCrudFieldDefinition => field !== null);
  const fieldKeys = new Set(fields.map((field) => field.key));
  const sections = readArray<MetadataRecord>(model.sections).map((section) => ({
    key: typeof section.key === 'string' ? section.key : '',
    titleKey: typeof section.titleKey === 'string' ? section.titleKey : '',
    fields: readArray<string>(section.fields).map(normalizeStandardCrudFieldKey).filter((fieldKey) => fieldKeys.has(fieldKey)),
  })).filter((section) => section.key.length > 0);

  return { fields, sections };
}
```

- [x] **Step 2: 实现 metadata query helper**

创建 `frontend/packages/shell/src/standard-crud/standardCrudMetadataQuery.ts`：

```ts
export interface StandardCrudQueryFieldSchema {
  key: string;
  hidden?: boolean;
}

export interface StandardCrudAdvancedFilterSchema {
  enabled?: boolean;
  sections?: Array<{
    key: string;
    fields?: StandardCrudQueryFieldSchema[];
  }>;
}

export interface StandardCrudQueryDefaultSchema {
  field: string;
  value: unknown;
}

export interface StandardCrudQuerySchema {
  fields: StandardCrudQueryFieldSchema[];
  advancedFilter?: StandardCrudAdvancedFilterSchema;
  defaults?: StandardCrudQueryDefaultSchema[];
  defaultPageSize?: number;
}

export function collectStandardCrudQueryFields(
  query: StandardCrudQuerySchema,
  advancedFilter: StandardCrudAdvancedFilterSchema | undefined = query.advancedFilter,
): StandardCrudQueryFieldSchema[] {
  const advancedFields = (advancedFilter?.sections ?? []).flatMap((section) => section.fields ?? []);
  return [...query.fields, ...advancedFields];
}

export function buildStandardCrudEmptyFilters(
  query: StandardCrudQuerySchema,
  advancedFilter?: StandardCrudAdvancedFilterSchema,
): Record<string, unknown> {
  return Object.fromEntries(collectStandardCrudQueryFields(query, advancedFilter).map((field) => [field.key, '']));
}

export function buildStandardCrudDefaultFilters(
  query: StandardCrudQuerySchema,
  advancedFilter?: StandardCrudAdvancedFilterSchema,
): Record<string, unknown> {
  const filters = buildStandardCrudEmptyFilters(query, advancedFilter);
  for (const item of query.defaults ?? []) {
    filters[item.field] = item.value;
  }
  return filters;
}

export function buildStandardCrudListQuery(
  query: StandardCrudQuerySchema,
  filters: Record<string, unknown>,
  advancedFilter?: StandardCrudAdvancedFilterSchema,
): Record<string, string | number | boolean | null | undefined> {
  const allowedKeys = new Set(collectStandardCrudQueryFields(query, advancedFilter).map((field) => field.key));
  const result: Record<string, string | number | boolean | null | undefined> = {};

  for (const [key, value] of Object.entries(filters)) {
    if (!allowedKeys.has(key) || value === '') {
      continue;
    }
    if (typeof value === 'string' || typeof value === 'number' || typeof value === 'boolean' || value === null || value === undefined) {
      result[key] = value;
    }
  }

  return result;
}
```

- [x] **Step 3: 扩展 resolver 输入和 schema 类型**

在 `frontend/packages/shell/src/metadata/standardCrudSchemaResolver.ts` 中新增类型：

```ts
export interface StandardCrudSchemaResolverContext {
  modelDocuments?: Array<Record<string, unknown>>;
  defaultLanguageCatalog?: Record<string, string>;
}

export interface StandardCrudRuntimeQuerySchema {
  fields: StandardCrudRuntimeFieldSchema[];
  defaults: Array<{ field: string; value: unknown }>;
  defaultSort?: { field: string; direction: 'asc' | 'desc' };
  defaultPageSize?: number;
}
```

把 `StandardCrudListRuntimeSchema` 的 `queryFields` 扩展为兼容字段并新增 `query`：

```ts
query: StandardCrudRuntimeQuerySchema;
queryFields: StandardCrudRuntimeFieldSchema[];
```

把 `StandardCrudRuntimeColumnSchema` 改为：

```ts
export interface StandardCrudRuntimeColumnSchema {
  key: string;
  labelKey: string;
  source: 'model' | 'projection';
  field: string;
  width?: number;
  sortable?: boolean;
}
```

把 `StandardCrudRuntimeActionSchema` 改为包含：

```ts
standardAction?: 'create' | 'view' | 'edit' | 'refresh' | 'delete' | 'disable' | 'submit';
variant?: 'default' | 'primary' | 'danger';
confirmMessageKey?: string;
handlerKey?: string;
requiredPermissions?: string[];
```

- [x] **Step 4: 在 resolver 中增加校验函数**

在同文件加入：

```ts
function assertDictionaryKey(dicKey: string, localKey: string, catalog: Record<string, string> | undefined): void {
  if (!catalog || localKey.length === 0) return;
  const fullKey = `${dicKey}.${localKey}`;
  if (catalog[fullKey] === undefined) {
    throw new Error(`missing dictionary key "${fullKey}"`);
  }
}

function findModelDocument(modelDocuments: Array<Record<string, unknown>>, modelKey: string): Record<string, unknown> {
  const model = modelDocuments.find((document) => typeof document === 'object' && document !== null && modelKey in document);
  return readRecord(model?.[modelKey]);
}

function requireModelDocument(viewKey: string, modelDocuments: Array<Record<string, unknown>>, modelKey: string): Record<string, unknown> {
  const model = findModelDocument(modelDocuments, modelKey);
  if (modelKey.length > 0 && Object.keys(model).length === 0) {
    throw new Error(`view "${viewKey}" references unknown model "${modelKey}"`);
  }
  return model;
}

function collectModelFields(model: Record<string, unknown>): Set<string> {
  const names = new Set<string>(['EID', 'ENAME', 'ECODE']);
  for (const field of readRecordArray(model.fields)) {
    const name = readString(field.name, '');
    if (name.length > 0) names.add(name);
  }
  return names;
}

const supportedStandardActions = new Set(['create', 'view', 'edit', 'refresh', 'delete', 'disable', 'submit']);
const supportedDataSourceTypes = new Set(['enum', 'model', 'provider', 'static']);
const supportedQueryPresets = new Set(['keyword']);

function collectProviderKeys(registration: StandardCrudResourceRegistration): Set<string> {
  return new Set((registration.lookupProviders ?? []).map((provider) => provider.key));
}

export function assertStandardCrudModelDataSources(
  model: Record<string, unknown>,
  registration: StandardCrudResourceRegistration,
): void {
  const providerKeys = collectProviderKeys(registration);
  for (const field of readRecordArray(model.fields)) {
    const fieldName = readString(field.name, '');
    if (fieldName.length === 0) continue;
    assertDataSource(fieldName, field, providerKeys);
  }
}

function assertDataSource(fieldName: string, field: Record<string, unknown>, providerKeys: Set<string>): void {
  const dataSource = readRecord(field.dataSource);
  if (dataSource.type === undefined) return;
  const type = readString(dataSource.type, '');
  if (!supportedDataSourceTypes.has(type)) {
    throw new Error(`unsupported dataSource type "${type}"`);
  }
  if (type === 'provider') {
    const providerKey = readString(dataSource.key, '');
    if (!providerKeys.has(providerKey)) {
      throw new Error(`dataSource provider "${providerKey}" for field "${fieldName}" is not registered`);
    }
  }
  if (type === 'model') {
    const providerKey = `model.${readString(dataSource.model, readString(dataSource.key, ''))}`;
    if (!providerKeys.has(providerKey)) {
      throw new Error(`model dataSource provider "${providerKey}" for field "${fieldName}" is not registered`);
    }
  }
}

function assertAction(action: StandardCrudRuntimeActionSchema, registration: StandardCrudResourceRegistration): void {
  if (action.standardAction && !supportedStandardActions.has(action.standardAction)) {
    throw new Error(`unsupported standard action "${action.standardAction}"`);
  }
  if (action.handlerKey && !registration.actionHandlers?.[action.handlerKey]) {
    throw new Error(`action handler "${action.handlerKey}" is not registered on resource "${registration.resourceKey}"`);
  }
}

function assertQuery(query: StandardCrudRuntimeQuerySchema, advancedFilter?: StandardCrudAdvancedFilterSchema): void {
  const fields = collectStandardCrudQueryFields(query, advancedFilter);
  const queryKeys = new Set(fields.map((field) => field.key));
  for (const field of fields) {
    if (field.source === 'preset' && field.preset && !supportedQueryPresets.has(field.preset)) {
      throw new Error(`unsupported query preset "${field.preset}"`);
    }
  }
  for (const item of query.defaults) {
    if (!queryKeys.has(item.field)) {
      throw new Error(`query default "${item.field}" does not reference a declared query field`);
    }
  }
}
```

在 `buildStandardCrudListSchema` 中调用：

```ts
const context = arguments[2] as StandardCrudSchemaResolverContext | undefined;
const dicKey = readString(view.dicKey, registration.resourceKey);
assertDictionaryKey(dicKey, readString(view.titleKey, 'Title'), context?.defaultLanguageCatalog);
const modelKey = readString(view.model, '');
const viewKey = readString(view.key, registration.listViewKey);
const model = modelKey.length > 0 ? requireModelDocument(viewKey, context?.modelDocuments ?? [], modelKey) : {};
const modelFields = collectModelFields(model);
```

对 query/column/action 逐项校验：

```ts
if (field.source === 'model' && field.field && !modelFields.has(field.field)) {
  throw new Error(`query field "${field.key}" references unknown model field "${field.field}"`);
}
if (column.source === 'projection' && column.field.length === 0) {
  throw new Error(`projection column "${column.key}" must declare field`);
}
if (column.source === 'model' && !modelFields.has(column.field)) {
  throw new Error(`column "${column.key}" references unknown model field "${column.field}"`);
}
assertStandardCrudModelDataSources(model, registration);
for (const action of [...schema.pageActions, ...schema.rowActions]) {
  assertAction(action, registration);
}
assertQuery(schema.query, schema.advancedFilter);
```

`buildStandardCrudDetailSchema` 和 `buildStandardCrudFormSchemaFromModel` 所在调用链必须复用 `assertStandardCrudModelDataSources`；不要只在 list schema 中校验 provider/model dataSource，否则用户直达详情或编辑抽屉时会绕过 provider 注册失败信号。

- [x] **Step 5: 跑 Task 1 测试并修正类型错误**

Run:

```powershell
pnpm --dir frontend --filter @icp-platform/shell test -- standard-crud-schema-and-registry standard-crud-metadata-form standard-crud-metadata-query
```

Expected:

```text
Test Files 3 passed
```

- [x] **Step 6: 提交 resolver 实现**

Run:

```powershell
git add frontend/packages/shell/src/metadata/standardCrudSchemaResolver.ts frontend/packages/shell/src/standard-crud/standardCrudMetadataForm.ts frontend/packages/shell/src/standard-crud/standardCrudMetadataQuery.ts frontend/packages/shell/src/__tests__/standard-crud-schema-and-registry.test.ts frontend/packages/shell/src/__tests__/standard-crud-metadata-form.test.ts frontend/packages/shell/src/__tests__/standard-crud-metadata-query.test.ts
git commit -m "功能：实现标准CRUD元数据schema合成"
```

## Task 4：补齐人员中心 metadata 与字典

**Files:**
- Modify: `modules/Organization/metadata/views/PersonList.view.json`
- Modify: `modules/Organization/metadata/views/PersonDetail.view.json`
- Modify: `modules/Organization/metadata/models/Person.model.json`
- Modify: `modules/Organization/metadata/dictionaries/PersonListView.dic.json`
- Modify: `modules/Organization/metadata/dictionaries/PersonDetailView.dic.json`
- Modify: `frontend/packages/shell/src/__tests__/vite-config.test.ts`
- Verify: `./scripts/validate-dic-localization.ps1`
- Verify: `pnpm --dir frontend --filter @icp-platform/shell test -- vite-config`

- [x] **Step 1: 更新 `PersonList.view.json`**

把 `query.fields` 调整为不含 `companyId`，并增加默认查询；部门、岗位、启用状态进入高级查询，不放在轻筛选条：

```json
"query": {
  "fields": [
    {
      "key": "keyword",
      "labelKey": "Query_Keyword",
      "source": "preset",
      "preset": "keyword",
      "operator": "like"
    },
    {
      "key": "employmentStatus",
      "labelKey": "Query_EmploymentStatus",
      "source": "model",
      "field": "EmploymentStatus",
      "operator": "="
    }
  ],
  "defaults": [
    {
      "field": "isEnabled",
      "value": true
    }
  ],
  "defaultSort": {
    "field": "PersonNo",
    "direction": "asc"
  },
  "defaultPageSize": 20
}
```

把 `advancedFilter.enabled` 改为 `true`，补齐部门、岗位和启用状态高级查询字段；部门和岗位仍使用普通 lookup，不引入树形控件：

```json
"advancedFilter": {
  "enabled": true,
  "titleKey": "AdvancedFilter_Title",
  "sections": [
    {
      "key": "organization",
      "titleKey": "AdvancedFilter_Organization",
      "fields": [
        {
          "key": "departmentId",
          "labelKey": "Query_Department",
          "source": "model",
          "field": "DepartmentId",
          "operator": "="
        },
        {
          "key": "positionId",
          "labelKey": "Query_Position",
          "source": "model",
          "field": "PositionId",
          "operator": "="
        },
        {
          "key": "isEnabled",
          "labelKey": "Query_IsEnabled",
          "source": "model",
          "field": "IsEnabled",
          "operator": "="
        }
      ]
    }
  ]
}
```

把 `columns` 调整为不含公司列，并补齐来源：

```json
"columns": [
  {
    "key": "personNo",
    "labelKey": "Column_PersonNo",
    "source": "model",
    "field": "PersonNo",
    "width": 140,
    "sortable": true
  },
  {
    "key": "name",
    "labelKey": "Column_Name",
    "source": "model",
    "field": "ENAME"
  },
  {
    "key": "departmentName",
    "labelKey": "Column_DepartmentName",
    "source": "projection",
    "field": "departmentName"
  },
  {
    "key": "employmentStatus",
    "labelKey": "Column_EmploymentStatus",
    "source": "model",
    "field": "EmploymentStatus"
  },
  {
    "key": "isEnabled",
    "labelKey": "Column_IsEnabled",
    "source": "model",
    "field": "IsEnabled"
  }
]
```

把动作补齐：

```json
"pageActions": [
  {
    "key": "create",
    "labelKey": "Action_Create",
    "standardAction": "create",
    "variant": "primary",
    "permissions": ["Organization.Person.Create"]
  },
  {
    "key": "refresh",
    "labelKey": "Action_Refresh",
    "standardAction": "refresh"
  }
],
"rowActions": [
  {
    "key": "view",
    "labelKey": "Action_View",
    "standardAction": "view"
  },
  {
    "key": "edit",
    "labelKey": "Action_Edit",
    "standardAction": "edit"
  },
  {
    "key": "delete",
    "labelKey": "Action_Delete",
    "standardAction": "delete",
    "variant": "danger",
    "confirmMessageKey": "Action_Delete_Confirm",
    "handlerKey": "delete"
  },
  {
    "key": "disable",
    "labelKey": "Action_Disable",
    "standardAction": "disable",
    "variant": "danger",
    "confirmMessageKey": "Action_Disable_Confirm",
    "handlerKey": "disable"
  }
]
```

- [x] **Step 2: 更新 `PersonDetail.view.json`**

把 `pageActions` 改为：

```json
"pageActions": [
  {
    "key": "edit",
    "labelKey": "Action_Edit",
    "standardAction": "edit"
  },
  {
    "key": "disable",
    "labelKey": "Action_Disable",
    "standardAction": "disable",
    "variant": "danger",
    "confirmMessageKey": "Action_Disable_Confirm",
    "handlerKey": "disable"
  }
]
```

- [x] **Step 3: 更新 `Person.model.json` 字段 UI/schema**

在 `fields` 中补齐下面形态；保留现有 `storage/contract/dataSource`，只补缺失 UI 和 form 语义：

```json
{
  "name": "PersonNo",
  "storage": { "type": "string" },
  "uiType": "text",
  "form": {
    "create": { "mode": "editable", "required": true },
    "edit": { "mode": "readonly" },
    "view": { "mode": "readonly" }
  },
  "view": { "visible": true }
}
```

为 `ENAME` 增加内核字段补充声明：

```json
{
  "name": "ENAME",
  "uiType": "text",
  "form": {
    "create": { "mode": "editable", "required": true },
    "edit": { "mode": "editable", "required": true },
    "view": { "mode": "readonly" }
  },
  "view": { "visible": true }
}
```

为 lookup 字段补齐：

```json
"uiType": "lookup",
"form": {
  "create": { "mode": "editable" },
  "edit": { "mode": "editable" },
  "view": { "mode": "readonly" }
}
```

`DepartmentId` 的 dataSource 改用 provider 并保留普通扁平 lookup：

```json
"dataSource": {
  "type": "provider",
  "key": "organization.departments",
  "dependsOn": ["CompanyId"]
}
```

`PositionId` 的 dataSource：

```json
"dataSource": {
  "type": "provider",
  "key": "organization.positions",
  "dependsOn": ["DepartmentId"]
}
```

`EmploymentStatus` 补：

```json
"uiType": "select",
"form": {
  "create": { "mode": "editable", "defaultValue": "FullTime" },
  "edit": { "mode": "editable" },
  "view": { "mode": "readonly" }
}
```

`IsEnabled` 补：

```json
"uiType": "switch",
"form": {
  "create": { "mode": "editable", "defaultValue": true },
  "edit": { "mode": "editable" },
  "view": { "mode": "readonly" }
}
```

- [x] **Step 4: 更新字典**

在 `PersonListView.dic.json` 增加：

```json
"Query_IsEnabled": {
  "zhs": "启用状态",
  "en": "Enabled"
},
"Query_Department": {
  "zhs": "部门",
  "en": "Department"
},
"Query_Position": {
  "zhs": "岗位",
  "en": "Position"
},
"AdvancedFilter_Organization": {
  "zhs": "组织信息",
  "en": "Organization"
},
"Action_Delete": {
  "zhs": "删除",
  "en": "Delete"
},
"Action_Delete_Confirm": {
  "zhs": "确认删除该人员吗？",
  "en": "Delete this person?"
},
"Action_Disable_Confirm": {
  "zhs": "确认停用该人员？",
  "en": "Disable this person?"
},
"Action_Processing": {
  "zhs": "处理中...",
  "en": "Processing..."
},
"Action_More": {
  "zhs": "更多",
  "en": "More"
},
"Drawer_View": {
  "zhs": "查看人员",
  "en": "View Person"
},
"Drawer_Edit": {
  "zhs": "编辑人员",
  "en": "Edit Person"
},
"Drawer_Create": {
  "zhs": "新建人员",
  "en": "Create Person"
}
```

在 `PersonDetailView.dic.json` 增加：

```json
"Action_Disable_Confirm": {
  "zhs": "确认停用当前人员吗？",
  "en": "Disable this person?"
},
"Detail_Info": {
  "zhs": "详情信息",
  "en": "Details"
}
```

- [x] **Step 5: 更新 vite localization manifest 测试**

在 `frontend/packages/shell/src/__tests__/vite-config.test.ts` 的 localization 测试中补断言：

```ts
expect(manifest.catalogs.zhs['PersonListView.Action_Delete_Confirm']).toBe('确认删除该人员吗？');
expect(manifest.catalogs.en['PersonListView.Action_Delete_Confirm']).toBe('Delete this person?');
expect(manifest.catalogs.zhs['PersonListView.Query_Department']).toBe('部门');
expect(manifest.catalogs.zhs['PersonListView.Query_Position']).toBe('岗位');
expect(manifest.catalogs.zhs['PersonListView.AdvancedFilter_Organization']).toBe('组织信息');
```

- [x] **Step 6: 验证 metadata 与字典**

Run:

```powershell
./scripts/validate-dic-localization.ps1
pnpm --dir frontend --filter @icp-platform/shell test -- vite-config
```

Expected:

```text
dic localization validation passed
Test Files ... passed
```

- [x] **Step 7: 提交 metadata**

Run:

```powershell
git add modules/Organization/metadata/views/PersonList.view.json modules/Organization/metadata/views/PersonDetail.view.json modules/Organization/metadata/models/Person.model.json modules/Organization/metadata/dictionaries/PersonListView.dic.json modules/Organization/metadata/dictionaries/PersonDetailView.dic.json frontend/packages/shell/src/__tests__/vite-config.test.ts
git commit -m "配置：补齐人员中心标准CRUD元数据"
```

## Task 5：改造 Shell List/Detail Host 消费 metadata schema

**Files:**
- Modify: `frontend/packages/shell/src/pages/StandardCrudListPageHost.tsx`
- Modify: `frontend/packages/shell/src/pages/StandardCrudDetailPageHost.tsx`
- Modify: `frontend/packages/shell/src/standard-crud/standardCrudActionRuntime.tsx`
- Modify: `frontend/packages/shell/src/__tests__/standard-crud-list-host.test.tsx`
- Modify: `frontend/packages/shell/src/__tests__/standard-crud-detail-host.test.tsx`
- Verify: `pnpm --dir frontend --filter @icp-platform/shell test -- standard-crud-list-host standard-crud-detail-host`

- [ ] **Step 1: 扩展 List Host 测试**

在 `standard-crud-list-host.test.tsx` 中把 localization catalog 补齐：

```ts
'PersonListView.Column_Name': '姓名',
'PersonListView.Action_Create': '新建',
'PersonListView.Action_View': '查看',
'PersonListView.Action_Edit': '编辑',
'PersonListView.Action_Refresh': '刷新',
'PersonListView.Action_Delete': '删除',
'PersonListView.Action_Delete_Confirm': '确认删除该人员吗？',
'PersonListView.Action_Disable': '停用',
'PersonListView.Action_Disable_Confirm': '确认停用该人员？',
```

新增测试：

```tsx
it('renders columns and actions from metadata when registration no longer declares them', async () => {
  mockStandardCrudModule({
    buildColumns: undefined,
    renderFilters: undefined,
    listActions: undefined,
    actionHandlers: {
      delete: vi.fn(),
    },
  });
  vi.spyOn(globalThis, 'fetch').mockResolvedValue(
    new Response(JSON.stringify({ items: [{ EID: 'Person_1', ENAME: '张三', departmentName: '平台部' }], total: 1, page: 1, pageSize: 20 }), {
      status: 200,
      headers: { 'Content-Type': 'application/json' },
    }),
  );

  await renderHost();
  await act(async () => undefined);

  expect(container.textContent).toContain('姓名');
  expect(container.textContent).toContain('查看');
  expect(container.textContent).toContain('编辑');
  expect(container.textContent).not.toContain('公司');
});
```

- [ ] **Step 2: 在 List Host 传入 resolver context**

在 `StandardCrudListPageHost.tsx` 中取得本地化函数：

```ts
const { t } = useLocalization();
```

Host 运行时不从 `LocalizationContext` 反向读取完整 catalog。resolver 的字典 key 强校验由 Task 1 单元测试、Task 4 `vite-config` manifest 测试和 `validate-dic-localization.ps1` 保障；Host 只传 model documents：

```ts
const schema = useMemo(() => buildStandardCrudListSchema(view, registration, {
  modelDocuments: runtimeMetadata?.modelDocuments ?? [],
}), [view, registration, runtimeMetadata?.modelDocuments]);
```

- [ ] **Step 3: 用 metadata query 初始化筛选状态**

把初始状态从 `registration.createEmptyFilters()` 改为：

```ts
const initialFilters = useMemo(() => buildStandardCrudDefaultFilters(schema.query, schema.advancedFilter), [schema.query, schema.advancedFilter]);
const [filters, setFilters] = useState<TFilter>(() => initialFilters as TFilter);
const [appliedFilters, setAppliedFilters] = useState<TFilter>(() => initialFilters as TFilter);
```

在 `resetFilters` 中使用：

```ts
setFilters(initialFilters as TFilter);
setAppliedFilters(initialFilters as TFilter);
```

在 `tableRequest.buildQuery` 中优先使用 metadata：

```ts
buildQuery: () => buildStandardCrudListQuery(schema.query, appliedFilters, schema.advancedFilter),
```

- [ ] **Step 4: 用 metadata 渲染轻筛选**

新增本地函数：

```tsx
function renderMetadataFilterField(
  field: StandardCrudRuntimeFieldSchema,
  value: unknown,
  onChange: (key: string, value: unknown) => void,
  labelFor: (labelKey: string) => string,
): ReactElement | null {
  if (field.hidden) return null;

  return (
    <label key={field.key} className="np-filter-control">
      <span className="sh-form-label">{labelFor(field.labelKey)}</span>
      <input
        className="sh-form-input"
        value={value == null ? '' : String(value)}
        onChange={(event) => onChange(field.key, event.target.value)}
      />
    </label>
  );
}
```

补一个 enum 查询控件分支，候选项来自 `registration.enumOptions`，字段的 enum key 由 resolver 从 model `dataSource.key` 投影到 `field.dataSourceKey`：

```tsx
if (field.dataSourceKey && registration.enumOptions?.[field.dataSourceKey]) {
  return (
    <label key={field.key} className="np-filter-control">
      <span className="sh-form-label">{labelFor(field.labelKey)}</span>
      <Select
        className="np-filter-select"
        value={typeof value === 'string' && value.length > 0 ? value : undefined}
        allowClear
        options={registration.enumOptions[field.dataSourceKey]}
        onChange={(nextValue) => onChange(field.key, nextValue ?? '')}
      />
    </label>
  );
}
```

把 `filterBar` 改为优先 metadata：

```tsx
const filterBar = (
  <CrudSearchBar
    fields={(
      <div className="np-person-filter-grid">
        {schema.query.fields.map((field) => renderMetadataFilterField(
          field,
          filters[field.key],
          (key, value) => setFilters((current) => ({ ...current, [key]: value })),
          (labelKey) => t(`${schema.dicKey}.${labelKey}`, labelKey),
        ))}
      </div>
    )}
    loading={loading}
    onSearch={context.applyFilters}
    onReset={context.resetFilters}
    more={schema.advancedFilter.enabled ? <AntdButton type="text" size="middle">{t(`${schema.dicKey}.${schema.advancedFilter.titleKey}`, schema.advancedFilter.titleKey)}</AntdButton> : undefined}
  />
);
```

同时补一个高级筛选面板或抽屉，字段来自 `schema.advancedFilter.sections[].fields`，复用 `renderMetadataFilterField` 和相同 filters 状态；部门、岗位、启用状态必须能从高级筛选写入 `filters.departmentId / filters.positionId / filters.isEnabled`，并被 `buildStandardCrudListQuery` 提交。若现有 Host 暂无高级筛选容器，本任务先使用受控弹层实现，不把高级筛选字段塞回轻筛选条。

- [ ] **Step 5: 用 metadata 渲染 columns/actions**

构建特殊列 renderer map：

```ts
const registeredColumns = registration.buildColumns?.(context) ?? [];
const registeredColumnByKey = new Map(registeredColumns.map((column) => [column.key, column]));
const metadataColumns = schema.columns.map((column) => {
  const registeredColumn = registeredColumnByKey.get(column.key);
  return {
    key: column.key,
    title: t(`${schema.dicKey}.${column.labelKey}`, column.labelKey),
    render: registeredColumn?.render,
  };
});
```

行动作使用 schema：

```ts
const createAction = schema.pageActions.find((action) => action.standardAction === 'create');
const refreshAction = schema.pageActions.find((action) => action.standardAction === 'refresh');
const rowActionDefinitions = schema.rowActions;
```

显示文案：

```ts
label: t(`${schema.dicKey}.${action.labelKey}`, action.labelKey),
confirmMessage: action.confirmMessageKey
  ? t(`${schema.dicKey}.${action.confirmMessageKey}`, action.confirmMessageKey)
  : undefined,
```

handler 查找优先 `handlerKey`：

```ts
const handler = registration.actionHandlers?.[action.handlerKey ?? action.key];
```

- [ ] **Step 6: 用 metadata form schema 替代 registration fields**

在 List Host 中从 model 构建：

```ts
const modelDocument = findStandardCrudModelDocument(runtimeMetadata?.modelDocuments ?? [], view.model);
const createFormSchema = useMemo(() => buildStandardCrudFormSchemaFromModel(modelDocument, {
  mode: 'create',
  enumOptions: registration.enumOptions ?? {},
}), [modelDocument, registration.enumOptions]);
const editFormSchema = useMemo(() => buildStandardCrudFormSchemaFromModel(modelDocument, {
  mode: 'edit',
  enumOptions: registration.enumOptions ?? {},
}), [modelDocument, registration.enumOptions]);
const editFields = editFormSchema.fields;
const createFields = createFormSchema.fields;
```

- [ ] **Step 7: 改造 Detail Host**

在 `StandardCrudDetailPageHost.tsx` 中同样用 `buildStandardCrudDetailSchema(view, registration, { modelDocuments })` 和 `buildStandardCrudFormSchemaFromModel`：

```ts
const formSchema = useMemo(() => buildStandardCrudFormSchemaFromModel(modelDocument, {
  mode: 'edit',
  enumOptions,
}), [modelDocument, enumOptions]);
const editFields = formSchema.fields;
```

`titleForBlock/titleForSection` 使用 dic：

```tsx
titleForBlock={(key) => t(`${schema.dicKey}.Block_${key === 'userLink' ? 'UserLink' : 'AuthorizationSummary'}`, key)}
titleForSection={(titleKey) => t(`${schema.dicKey}.${titleKey}`, titleKey)}
```

详情 page actions 使用 schema：

```tsx
<StandardCrudActions
  actions={schema.pageActions.map((action) => ({
    ...action,
    labelKey: t(`${schema.dicKey}.${action.labelKey}`, action.labelKey),
    confirmMessage: action.confirmMessageKey ? t(`${schema.dicKey}.${action.confirmMessageKey}`, action.confirmMessageKey) : undefined,
  }))}
  grantedPermissions={registration.permissions}
  handlers={registration.actionHandlers}
  context={{ id, detail: currentDetail as TDetail, refresh }}
/>
```

- [ ] **Step 8: 运行 Host 测试**

Run:

```powershell
pnpm --dir frontend --filter @icp-platform/shell test -- standard-crud-list-host standard-crud-detail-host
```

Expected:

```text
Test Files 2 passed
```

- [ ] **Step 9: 提交 Host 改造**

Run:

```powershell
git add frontend/packages/shell/src/pages/StandardCrudListPageHost.tsx frontend/packages/shell/src/pages/StandardCrudDetailPageHost.tsx frontend/packages/shell/src/standard-crud/standardCrudActionRuntime.tsx frontend/packages/shell/src/__tests__/standard-crud-list-host.test.tsx frontend/packages/shell/src/__tests__/standard-crud-detail-host.test.tsx
git commit -m "功能：让标准CRUD宿主消费元数据schema"
```

## Task 6：收缩人员中心资源注册并移除私有 runtime fallback

**Files:**
- Modify: `frontend/modules/organization/src/standard-crud/personStandardCrudResource.tsx`
- Delete: `frontend/modules/organization/src/runtime/personCenterRuntime.ts`
- Delete: `frontend/modules/organization/src/runtime/personCenterRuntime.types.ts`
- Modify: `frontend/packages/shell/src/__tests__/organization-person-create-flow.test.tsx`
- Modify: `frontend/packages/shell/src/__tests__/organization-person-phase2-shrink.test.tsx`
- Modify: `frontend/packages/shell/src/__tests__/person-center-crud-pro-sample.test.tsx`
- Verify: `pnpm --dir frontend --filter @icp-platform/shell test -- organization-person-create-flow organization-person-phase2-shrink person-center-crud-pro-sample`

- [ ] **Step 1: 更新 shrink 测试**

在 `organization-person-phase2-shrink.test.tsx` 增加断言：

```ts
expect(personStandardCrudResource.fields).toBeUndefined();
expect(personStandardCrudResource.formSections).toBeUndefined();
expect(personStandardCrudResource.listActions).toBeUndefined();
expect(personStandardCrudResource.detailActions).toBeUndefined();
expect(personStandardCrudResource.buildColumns).toBeUndefined();
expect(personStandardCrudResource.renderFilters).toBeUndefined();
expect(personStandardCrudResource.lookupProviders?.map((provider) => provider.key)).toEqual([
  'organization.companies',
  'organization.departments',
  'organization.positions',
]);
expect(Object.keys(personStandardCrudResource.actionHandlers ?? {})).toEqual(['refresh', 'delete', 'disable']);
```

- [ ] **Step 2: 收缩 `personStandardCrudResource.tsx`**

删除这些 import：

```ts
import type { Dispatch, SetStateAction } from 'react';
import { Input, Select } from 'antd';
import { StatusTag } from '@icp-platform/ui';
```

删除这些声明：

```ts
PersonListFiltersPanel
personFields
personFormSections
```

把注册改为：

```ts
export const personStandardCrudResource: StandardCrudResourceRegistration<PersonListFilters, PersonListRow, PersonDetailView> = {
  resourceKey: 'organization.person',
  apiRoute: '/api/organization/persons',
  listViewKey: 'organization.person.list',
  listRoutePath: '/organization/persons',
  detailViewKey: 'organization.person.detail',
  detailRoutePath: '/organization/persons/:id',
  permissions: ['Organization.Person.View', 'Organization.Person.Create'],
  lookupProviders: organizationLookupProviders,
  enumOptions: {
    EmploymentStatus: employmentStatusOptions,
  },
  summaryRenderers: personSummaryRenderers,
  actionHandlers: {
    refresh: ({ refresh }) => {
      refresh();
    },
    delete: async ({ id, refresh }) => {
      if (!id) {
        throw new Error('缺少人员标识，无法删除。');
      }
      await deletePerson(id);
      refresh();
    },
    disable: async ({ id, refresh }) => {
      if (!id) {
        throw new Error('缺少人员标识，无法停用。');
      }
      await disablePerson(id);
      refresh();
    },
  },
  createEmptyFilters,
  buildListQuery,
  mapListItem: mapPersonListItem,
  getRowId: (row) => row.id,
  mapDetail: mapPersonDetailView,
};
```

资源注册删除当前不必要的 `buildMutationValues`，由 Host 使用 metadata fields 调用 `normalizeStandardCrudSubmitValues(fields, values)`。后续只有在 Organization 人员资源存在真正模块级提交差异时，才允许恢复专属提交适配；该适配不得用于 `EID / ENAME / ECODE` 或大小写友好名等平台协议字段转换。

- [ ] **Step 3: 删除私有 runtime fallback**

Run:

```powershell
git rm frontend/modules/organization/src/runtime/personCenterRuntime.ts frontend/modules/organization/src/runtime/personCenterRuntime.types.ts
```

更新 `organization-person-create-flow.test.tsx`，删除对 `personCenterRuntime` 的 mock，并把旧新建页测试改为兼容提示：

```tsx
it('redirects legacy create page users to metadata driven list drawer', async () => {
  await act(async () => {
    root = renderWithLocalization(<PersonCreatePage />, container);
  });

  expect(container.textContent).toContain('人员新建已由标准 CRUD 抽屉接管');
});
```

并将 `PersonCreatePage` 改为兼容提示或跳转，不再读取 `personStandardCrudResource.fields`。
将 `PersonCreatePage` 实现改为：

```tsx
import { ErrorState } from '@icp-platform/ui';

export default function PersonCreatePage() {
  return (
    <ErrorState message="人员新建已由标准 CRUD 抽屉接管，请从人员中心列表点击新建。" />
  );
}
```

- [ ] **Step 4: 人员集成测试覆盖 metadata 行为**

在 `person-center-crud-pro-sample.test.tsx` 补断言：

```ts
expect(container.textContent).toContain('人员编号');
expect(container.textContent).toContain('姓名');
expect(container.textContent).toContain('部门');
expect(container.textContent).not.toContain('公司');
expect(fetch).toHaveBeenCalledWith('/api/organization/persons?isEnabled=true&pageIndex=1&pageSize=20', { method: 'GET' });
```

补 projection 缺值容错：

```ts
vi.spyOn(globalThis, 'fetch').mockResolvedValue(
  new Response(JSON.stringify({ items: [{ EID: 'Person_1', PersonNo: 'P001', ENAME: '张三', DepartmentId: 'Dept_1' }], total: 1, page: 1, pageSize: 20 }), {
    status: 200,
    headers: { 'Content-Type': 'application/json' },
  }),
);

await renderPersonCenter();
await act(async () => undefined);

expect(container.textContent).toContain('张三');
expect(container.textContent).not.toContain('Cannot read');
```

- [ ] **Step 5: 运行人员中心定向测试**

Run:

```powershell
pnpm --dir frontend --filter @icp-platform/shell test -- organization-person-create-flow organization-person-phase2-shrink person-center-crud-pro-sample
```

Expected:

```text
Test Files 3 passed
```

- [ ] **Step 6: 提交人员模块收缩**

Run:

```powershell
git add frontend/modules/organization/src/standard-crud/personStandardCrudResource.tsx frontend/modules/organization/src/pages/PersonCreatePage.tsx frontend/packages/shell/src/__tests__/organization-person-create-flow.test.tsx frontend/packages/shell/src/__tests__/organization-person-phase2-shrink.test.tsx frontend/packages/shell/src/__tests__/person-center-crud-pro-sample.test.tsx
git add -u frontend/modules/organization/src/runtime
git commit -m "重构：收缩人员中心标准CRUD资源注册"
```

## Task 7：补齐后端 projection 契约测试与前端构建验证

**Files:**
- Modify: `tests/Organization.Tests/PersonStandardCrudResourceTests.cs`
- Modify: `tests/PlatformCrud.Tests/StandardCrudServiceReadTests.cs`
- Verify: `dotnet test tests\Organization.Tests\Organization.Tests.csproj -nr:false -v minimal`
- Verify: `dotnet test tests\PlatformCrud.Tests\PlatformCrud.Tests.csproj -nr:false -v minimal`
- Verify: `pnpm --dir frontend --filter @icp-platform/shell build`

- [ ] **Step 1: 后端人员 descriptor 测试补 projection 字段**

在 `PersonStandardCrudResourceTests.cs` 中确认或补充：

```csharp
[Fact]
public void Descriptor_ShouldExposePersonDisplayValueProjectionFields()
{
    var descriptor = PersonCrudDescriptorFactory.Create();

    Assert.Contains("DepartmentName", descriptor.DisplayValueFields);
    Assert.Contains("PositionName", descriptor.DisplayValueFields);
}
```

- [ ] **Step 2: 标准 CRUD 读取测试补 projection 返回**

在 `StandardCrudServiceReadTests.cs` 中补充人员或已有标准资源 projection 断言：

```csharp
Assert.True(item.Fields.ContainsKey("DepartmentName"));
Assert.True(item.Fields.ContainsKey("PositionName"));
```

岗位可以为空值，但列表 DTO 必须包含 `PositionName` key。

- [ ] **Step 3: 运行后端定向测试**

Run:

```powershell
dotnet test tests\Organization.Tests\Organization.Tests.csproj -nr:false -v minimal
dotnet test tests\PlatformCrud.Tests\PlatformCrud.Tests.csproj -nr:false -v minimal
```

Expected:

```text
Passed!
```

- [ ] **Step 4: 运行前端构建**

Run:

```powershell
pnpm --dir frontend --filter @icp-platform/shell build
```

Expected:

```text
vite build ... built
```

- [ ] **Step 5: 提交契约测试与构建修正**

Run:

```powershell
git add tests/Organization.Tests/PersonStandardCrudResourceTests.cs tests/PlatformCrud.Tests/StandardCrudServiceReadTests.cs
git commit -m "测试：补齐人员中心投影字段契约"
```

## Task 8：全量收口、文档和验证记录

**Files:**
- Modify: `docs/phases/current-handoff.md`
- Modify: `docs/verification/verification-matrix.md`
- Create: `docs/verification/records/2026-05-24-standard-crud-metadata-recovery.md`
- Verify: `./scripts/validate-ai-first-design.ps1 -Path docs/superpowers/specs/platform/2026-05-24-standard-crud-metadata-recovery-design.md`
- Verify: `./scripts/check-ai-first-design-targets.ps1`
- Verify: `./scripts/validate-dic-localization.ps1`
- Verify: `pnpm --dir frontend --filter @icp-platform/shell test -- standard-crud-schema-and-registry standard-crud-metadata-form standard-crud-metadata-query standard-crud-list-host standard-crud-detail-host person-center-crud-pro-sample organization-person-phase2-shrink`
- Verify: `pnpm --dir frontend --filter @icp-platform/shell build`

- [ ] **Step 1: 更新当前交接台账**

在 `docs/phases/current-handoff.md` 增加本轮完成项：

```md
- 标准 CRUD 元数据回收实施完成：人员中心查询、默认查询、列表列、页面动作、行动作、详情 sections、summaryBlocks 和表单字段已优先从 `PersonList.view.json / PersonDetail.view.json / Person.model.json` 合成；Organization 前端注册仅保留 provider、renderer、handler、mapper。
```

在推荐下一步增加：

```md
- 基于人员中心样板评估第二个标准 CRUD 资源，但在复制前先确认是否需要树形 lookup 或通用 model option endpoint。
```

- [ ] **Step 2: 更新验证记录**

新建 `docs/verification/records/2026-05-24-standard-crud-metadata-recovery.md`：

````md
# 标准 CRUD 元数据回收验证记录

## 范围

- 人员中心标准 CRUD metadata 回收。
- Shell 标准 CRUD schema resolver、List Host、Detail Host。
- Organization 人员资源注册收缩。

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

## 回滚

- 回滚 Shell metadata resolver/Host 改动。
- 恢复 `personStandardCrudResource.tsx` 中字段、列、查询、动作声明。
- 反向回退 `modules/Organization/metadata/**` 中本轮 schema 调整。
````

本任务要求执行记录中的命令；执行失败时先修复并重跑，环境阻断时在 `结果` 中写明阻断原因和未覆盖风险。

- [ ] **Step 3: 跑最终验证**

Run:

```powershell
./scripts/validate-ai-first-design.ps1 -Path docs/superpowers/specs/platform/2026-05-24-standard-crud-metadata-recovery-design.md
./scripts/check-ai-first-design-targets.ps1
./scripts/validate-dic-localization.ps1
pnpm --dir frontend --filter @icp-platform/shell test -- standard-crud-schema-and-registry standard-crud-metadata-form standard-crud-metadata-query standard-crud-list-host standard-crud-detail-host person-center-crud-pro-sample organization-person-phase2-shrink
pnpm --dir frontend --filter @icp-platform/shell build
```

Expected:

```text
AI-first design validation passed
dic localization validation passed
Test Files ... passed
vite build ... built
```

- [ ] **Step 4: 提交文档收口**

Run:

```powershell
git add docs/phases/current-handoff.md docs/verification/verification-matrix.md docs/verification/records/2026-05-24-standard-crud-metadata-recovery.md
git commit -m "文档：记录标准CRUD元数据回收验证"
```

## 最终验收清单

- [ ] 人员中心列表不展示公司筛选和公司列。
- [ ] 人员中心部门仍作为普通 lookup 渲染，不出现 `treeSelect/cascader/tree view`。
- [ ] `PersonList.view.json` 控制查询项、默认查询、列表列、页面动作、行动作。
- [ ] `PersonDetail.view.json` 控制详情 sections、summaryBlocks、详情动作。
- [ ] `Person.model.json` 控制表单字段、字段控件、字段模式、数据源、默认值。
- [ ] `personStandardCrudResource.tsx` 不再声明标准字段数组、标准列数组、标准筛选 React 组件或标准动作数组。
- [ ] 多语言文案来自 dic，切换 `zhs/en` 后标题、列名、查询项、动作、确认提示和枚举标签可解析。
- [ ] projection 字段缺值不导致整页失败；后端/前端契约测试能发现已声明 projection 字段未稳定返回。
- [ ] `file-type-mappings.json` 未新增 `view` mapping，元数据配置中心仍暂停。
- [ ] 未新增 action/control/i18n metadata 类型。
- [ ] 所有最终验证命令有记录，失败项有原因和回滚口径。
