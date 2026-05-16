# 前端标准 CRUD 平台化第二批 Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** 消除人员中心里剩余的可复制表单、lookup、动作和摘要块通用逻辑，让 Organization 只保留人员账号绑定、授权摘要、组织归属特殊显示和必要业务校验。

**Architecture:** 第二批继续沿用第一批边界：SDK 暴露稳定契约，UI 提供无业务语义的表单/字段/摘要展示骨架，Shell 负责 lookup、动作、summary 和详情编辑策略装配，Organization 通过标准 CRUD 资源注册声明人员资源的字段、数据源、动作和专属 renderer。元数据只声明字段、动作和数据源归属，不承载加载流程、级联算法、提交算法或回滚逻辑。

**Tech Stack:** React 18 / TypeScript / Vite / Vitest / @icp-platform/sdk / @icp-platform/ui / @icp-platform/shell

---

## 文件结构与职责

**Create**

- `frontend/packages/shell/src/__tests__/standard-crud-phase2-sdk.test.ts`  
  覆盖第二批新增 SDK 契约的纯函数：i18n key 解析、字段值读取、提交值规范化、lookup 当前值保留、summary 状态文案。
- `frontend/packages/ui/src/patterns/StandardCrudField.tsx`  
  通用字段只读展示和编辑控件渲染，不发请求、不读取 metadata、不知道人员中心。
- `frontend/packages/ui/src/patterns/StandardCrudForm.tsx`  
  通用 create / edit / view 表单骨架，负责 sections、保存按钮、错误位和字段布局。
- `frontend/packages/shell/src/standard-crud/standardCrudLookupRuntime.ts`  
  Shell 侧 lookup 运行时：按资源注册的数据源 provider 加载 enum、value list、model lookup，处理显式级联依赖和当前值保留。
- `frontend/packages/shell/src/standard-crud/standardCrudActionRuntime.tsx`  
  Shell 侧动作运行时：渲染标准动作和模块动作，统一 confirm、pending、错误提示和权限过滤入口。
- `frontend/packages/shell/src/standard-crud/standardCrudSummaryRuntime.tsx`  
  Shell 侧 summary block 运行时：平台处理 summary 状态协议，模块只提供专属内容 renderer。
- `frontend/packages/shell/src/__tests__/standard-crud-field-form.test.tsx`  
  覆盖 UI 字段和表单骨架。
- `frontend/packages/shell/src/__tests__/standard-crud-lookup-runtime.test.tsx`  
  覆盖 lookup 加载、级联依赖、当前值保留和失败降级。
- `frontend/packages/shell/src/__tests__/standard-crud-action-runtime.test.tsx`  
  覆盖标准动作、模块动作、权限过滤、confirm、pending 和错误提示。
- `frontend/packages/shell/src/__tests__/standard-crud-summary-runtime.test.tsx`  
  覆盖 summary 状态协议和模块 renderer 边界。
- `frontend/packages/shell/src/__tests__/organization-person-phase2-shrink.test.tsx`  
  覆盖人员中心接入第二批平台能力后的收缩边界。
- `docs/verification/frontend-standard-crud-platformization-phase-2-verification.md`  
  记录第二批验证结果、Organization 暂留项解释、残留扫描和回滚口径。

**Modify**

- `frontend/packages/sdk/src/standardCrud.ts`  
  增加第二批公开契约：字段 schema、表单 section、lookup provider、动作、summary renderer、i18n helper 和提交值规范化 helper。
- `frontend/packages/sdk/src/index.ts`  
  导出第二批新增类型与 helper。
- `frontend/packages/ui/src/index.ts`  
  导出 `StandardCrudField`、`StandardCrudForm` 和相关 props 类型。
- `frontend/packages/shell/src/metadata/standardCrudSchemaResolver.ts`  
  扩展 detail sections、fields、summary blocks 和 action schema 读取，但继续保持只读解析，不写入运行流程。
- `frontend/packages/shell/src/pages/StandardCrudListPageHost.tsx`  
  接入动作运行时、lookup filter 渲染和权限过滤，移除 Host 内散落的默认动作判断。
- `frontend/packages/shell/src/pages/StandardCrudDetailPageHost.tsx`  
  接入标准表单、summary runtime、详情标准动作和编辑保存流程。
- `frontend/modules/organization/src/standard-crud/personStandardCrudResource.tsx`  
  改为声明人员字段、lookup provider、标准动作、summary renderer 和必要人员专属包装。
- `frontend/modules/organization/src/components/PersonEditorForm.tsx`  
  删除或收缩为人员专属包装；通用字段、lookup、sections、保存按钮和提交值规范化转移到平台能力。
- `frontend/modules/organization/src/services/organizationService.ts`  
  保留人员 DTO mapper、组织 lookup provider、人员专属请求；移除表单通用提交值规范化和可平台化 i18n/字段渲染辅助。
- `frontend/modules/organization/src/pages/PersonCreatePage.tsx`  
  改用标准 CRUD form 能力；只保留创建页路由兼容和成功后跳转。
- `frontend/modules/organization/src/components/AuthorizationSummaryCard.tsx`
- `frontend/modules/organization/src/components/PersonUserLinkCard.tsx`  
  只承载专属内容 renderer，不再负责平台 summary 状态协议。
- `docs/phases/current-handoff.md`  
  第二批完成后回填最近完成、验证快照、剩余风险和下一步建议。

**Do Not Modify**

- `server/**`
- `apps/host-api/**`
- `scripts/build.ps1`
- `scripts/test.ps1`
- `frontend/packages/shell/src/components/Layout.tsx`
- `frontend/packages/shell/src/components/Sidebar.tsx`
- `frontend/packages/shell/src/registerModules.ts`

---

### Task 0: 实施前一致性 Review

**Files:**
- Read: `AGENTS.md`
- Read: `docs/superpowers/specs/platform/2026-05-12-frontend-standard-crud-platformization-design.md`
- Read: `docs/verification/frontend-standard-crud-platformization-phase-1-verification.md`
- Read: `docs/phases/current-handoff.md`
- Read: `frontend/packages/sdk/src/standardCrud.ts`
- Read: `frontend/packages/shell/src/pages/StandardCrudListPageHost.tsx`
- Read: `frontend/packages/shell/src/pages/StandardCrudDetailPageHost.tsx`
- Read: `frontend/modules/organization/src/standard-crud/personStandardCrudResource.tsx`
- Read: `frontend/modules/organization/src/components/PersonEditorForm.tsx`

- [x] **Step 1: 确认工作区没有需要切分的冲突**

Run:

```powershell
git status --short
```

Expected: 允许看到本计划文档或用户已有文档改动；如果同一批前端文件已有未提交改动，先阅读对应 diff，不能覆盖用户改动。

- [x] **Step 2: 确认第二批入口与边界**

Run:

```powershell
Select-String -Path .\docs\superpowers\specs\platform\2026-05-12-frontend-standard-crud-platformization-design.md -Pattern "14.2|第二批验收条件|第二批回滚|第二批实施验证"
```

Expected: 输出包含第二批固定交付清单、验收条件、回滚和验证建议。

- [x] **Step 3: 扫描第一批暂留项**

Run:

```powershell
rg -n "PersonEditorForm|getCompanies|getDepartments|getPositions|resolveListText|resolveDetailText|AuthorizationSummaryCard|PersonUserLinkCard|employmentStatusOptions" frontend/modules/organization/src frontend/packages
```

Expected: 命中 `PersonEditorForm`、组织归属 lookup、人员专属 summary card 和状态选项；这些命中是第二批收缩输入，不是立即删除目标。

- [x] **Step 4: 记录一致性 review 结论**

在 task runner summary 中记录以下结论，不创建额外仓库文件：

```text
一致性 review 结论：
- 本轮只做前端标准 CRUD 第二批，不修改后端、HostApi、部署脚本、CI 或 Shell 布局。
- SDK 只暴露字段、表单、lookup、动作、summary 和 i18n 的稳定契约。
- UI 只做无业务语义的展示和表单骨架，不发请求、不读取 runtime metadata。
- Shell 负责运行时装配、lookup 加载、动作执行状态、summary 状态协议和详情编辑流程。
- Organization 只声明人员资源配置、组织归属 lookup provider、人员账号绑定与授权摘要专属 renderer、必要业务校验。
```

- [x] **Step 5: Commit**

No commit for Task 0. This task is a pre-flight gate.

---

### Task 1: SDK 第二批契约与纯函数

**Files:**
- Modify: `frontend/packages/sdk/src/standardCrud.ts`
- Modify: `frontend/packages/sdk/src/index.ts`
- Create: `frontend/packages/shell/src/__tests__/standard-crud-phase2-sdk.test.ts`

- [x] **Step 1: 写 SDK 第二批失败测试**

Create `frontend/packages/shell/src/__tests__/standard-crud-phase2-sdk.test.ts`:

```ts
import { describe, expect, it } from 'vitest';
import {
  buildStandardCrudI18nLabel,
  normalizeStandardCrudSubmitValues,
  preserveStandardCrudCurrentOption,
  readStandardCrudFieldValue,
  resolveStandardCrudSummaryMessage,
  type StandardCrudFieldDefinition,
} from '@icp-platform/sdk';

describe('standard CRUD phase 2 SDK helpers', () => {
  it('resolves i18n labels from dicKey and local label keys', () => {
    expect(buildStandardCrudI18nLabel('Person', 'Fields.PersonNo')).toBe('Person.Fields.PersonNo');
    expect(buildStandardCrudI18nLabel('', 'Fields.PersonNo')).toBe('Fields.PersonNo');
    expect(buildStandardCrudI18nLabel('Person', '')).toBe('Person');
  });

  it('reads field values by model field name and optional fallback keys', () => {
    const field: StandardCrudFieldDefinition = {
      key: 'name',
      labelKey: 'Fields.Name',
      modelField: 'ENAME',
      fallbackFields: ['Name'],
      kind: 'text',
    };

    expect(readStandardCrudFieldValue({ ENAME: '张三' }, field)).toBe('张三');
    expect(readStandardCrudFieldValue({ Name: '李四' }, field)).toBe('李四');
    expect(readStandardCrudFieldValue({}, field)).toBeNull();
  });

  it('normalizes submit values with trim and empty string to null rules', () => {
    const fields: StandardCrudFieldDefinition[] = [
      { key: 'personNo', labelKey: 'Fields.PersonNo', modelField: 'PersonNo', kind: 'text', required: true, trim: true },
      { key: 'departmentId', labelKey: 'Fields.DepartmentId', modelField: 'DepartmentId', kind: 'lookup', emptyAsNull: true },
      { key: 'isEnabled', labelKey: 'Fields.IsEnabled', modelField: 'IsEnabled', kind: 'boolean' },
    ];

    expect(normalizeStandardCrudSubmitValues(fields, { personNo: ' P001 ', departmentId: '', isEnabled: true })).toEqual({
      PersonNo: 'P001',
      DepartmentId: null,
      IsEnabled: true,
    });
  });

  it('preserves current lookup value when provider result does not include it', () => {
    expect(
      preserveStandardCrudCurrentOption(
        [{ value: 'Company_2', label: '二号公司' }],
        { value: 'Company_1', label: '一号公司' },
      ),
    ).toEqual([
      { value: 'Company_1', label: '一号公司' },
      { value: 'Company_2', label: '二号公司' },
    ]);
  });

  it('maps summary status to stable platform messages', () => {
    expect(resolveStandardCrudSummaryMessage({ status: 'notConfigured', data: null })).toBe('暂未配置');
    expect(resolveStandardCrudSummaryMessage({ status: 'dependencyUnavailable', data: null })).toBe('依赖不可用');
  });
});
```

- [x] **Step 2: 运行测试确认失败**

Run:

```powershell
pnpm --dir .\frontend --filter @icp-platform/shell test -- standard-crud-phase2-sdk
```

Expected: FAIL，提示 `buildStandardCrudI18nLabel`、`StandardCrudFieldDefinition` 或其他第二批导出尚不存在。

- [x] **Step 3: 新增 SDK 第二批类型**

Append these exports to `frontend/packages/sdk/src/standardCrud.ts` after the first-batch registration types:

```ts
export type StandardCrudFieldKind = 'text' | 'boolean' | 'enum' | 'lookup' | 'date' | 'readonly';

export interface StandardCrudOption {
  value: string;
  label: string;
  disabled?: boolean;
}

export interface StandardCrudLookupRequest {
  fieldKey: string;
  keyword?: string;
  values: Record<string, unknown>;
  dependencyValues: Record<string, unknown>;
}

export interface StandardCrudLookupProvider {
  key: string;
  load: (request: StandardCrudLookupRequest) => Promise<StandardCrudOption[]>;
}

export interface StandardCrudLookupConfig {
  providerKey: string;
  dependsOn?: string[];
  placeholder?: string;
  currentValueLabelField?: string;
}

export interface StandardCrudFieldDefinition {
  key: string;
  labelKey: string;
  modelField: string;
  fallbackFields?: string[];
  kind: StandardCrudFieldKind;
  required?: boolean;
  readOnly?: boolean;
  trim?: boolean;
  emptyAsNull?: boolean;
  defaultValue?: unknown;
  options?: StandardCrudOption[];
  lookup?: StandardCrudLookupConfig;
}

export interface StandardCrudFormSection {
  key: string;
  titleKey: string;
  fields: string[];
}

export type StandardCrudFormMode = 'create' | 'edit' | 'view';

export interface StandardCrudActionDefinition {
  key: string;
  labelKey: string;
  variant?: 'default' | 'primary' | 'danger';
  standardAction?: 'create' | 'edit' | 'refresh' | 'disable' | 'submit';
  confirmMessage?: string;
  requiredPermissions?: string[];
}

export interface StandardCrudActionContext<TDetail = unknown, TRow = unknown> {
  id?: string;
  detail?: TDetail;
  row?: TRow;
  refresh: () => void;
}

export type StandardCrudActionHandler<TDetail = unknown, TRow = unknown> = (
  context: StandardCrudActionContext<TDetail, TRow>,
) => Promise<void> | void;

export interface StandardCrudSummaryRendererContext<TData = unknown> {
  blockKey: string;
  block: StandardCrudSummaryBlock<TData>;
  title: string;
}

export type StandardCrudSummaryRenderer<TData = unknown> = (context: StandardCrudSummaryRendererContext<TData>) => ReactNode;
```

Extend `StandardCrudResourceRegistration` with optional second-batch fields:

```ts
  fields?: StandardCrudFieldDefinition[];
  formSections?: StandardCrudFormSection[];
  lookupProviders?: StandardCrudLookupProvider[];
  listActions?: StandardCrudActionDefinition[];
  detailActions?: StandardCrudActionDefinition[];
  actionHandlers?: Record<string, StandardCrudActionHandler<TDetail, TRow>>;
  summaryRenderers?: Record<string, StandardCrudSummaryRenderer>;
  buildMutationValues?: (values: Record<string, unknown>) => StandardCrudMutationValues;
  validateMutationValues?: (values: Record<string, unknown>, mode: StandardCrudFormMode) => string | null;
```

Add a Chinese comment before the optional fields:

```ts
  // 第二批扩展只声明资源允许的平台化能力；lookup 加载、动作状态、summary 状态和提交流程仍由 Shell 运行时执行。
```

- [x] **Step 4: 新增 SDK helper 实现**

Add these functions to `frontend/packages/sdk/src/standardCrud.ts`:

```ts
export function buildStandardCrudI18nLabel(dicKey: string, localKey: string): string {
  if (!dicKey) {
    return localKey;
  }

  if (!localKey) {
    return dicKey;
  }

  return `${dicKey}.${localKey}`;
}

export function readStandardCrudFieldValue(
  record: Record<string, unknown>,
  field: StandardCrudFieldDefinition,
): unknown {
  const keys = [field.modelField, ...(field.fallbackFields ?? [])];
  for (const key of keys) {
    if (Object.prototype.hasOwnProperty.call(record, key)) {
      return record[key] ?? null;
    }
  }

  return null;
}

export function normalizeStandardCrudSubmitValues(
  fields: StandardCrudFieldDefinition[],
  values: Record<string, unknown>,
): StandardCrudMutationValues {
  const result: StandardCrudMutationValues = {};

  for (const field of fields) {
    if (field.readOnly) {
      continue;
    }

    let value = values[field.key] ?? field.defaultValue ?? null;
    if (typeof value === 'string' && field.trim) {
      value = value.trim();
    }

    if (value === '' && field.emptyAsNull) {
      value = null;
    }

    result[field.modelField] = value;
  }

  return result;
}

export function preserveStandardCrudCurrentOption(
  options: StandardCrudOption[],
  current?: StandardCrudOption | null,
): StandardCrudOption[] {
  if (!current?.value || options.some((option) => option.value === current.value)) {
    return options;
  }

  return [current, ...options];
}

export function resolveStandardCrudSummaryMessage(block: StandardCrudSummaryBlock): string {
  if (block.status === 'notConfigured') {
    return '暂未配置';
  }

  if (block.status === 'dependencyUnavailable') {
    return '依赖不可用';
  }

  return '';
}
```

- [x] **Step 5: 从 SDK 入口导出第二批能力**

Confirm `frontend/packages/sdk/src/index.ts` uses the existing star export or add:

```ts
export * from './standardCrud';
```

Expected: `@icp-platform/sdk` consumers can import every new type/helper from package root.

- [x] **Step 6: 运行 SDK 第二批测试**

Run:

```powershell
pnpm --dir .\frontend --filter @icp-platform/shell test -- standard-crud-phase2-sdk
```

Expected: PASS。

- [x] **Step 7: 运行 SDK 构建**

Run:

```powershell
pnpm --dir .\frontend --filter @icp-platform/sdk build
```

Expected: PASS。

- [x] **Step 8: Commit**

```powershell
git add frontend/packages/sdk/src/standardCrud.ts frontend/packages/sdk/src/index.ts frontend/packages/shell/src/__tests__/standard-crud-phase2-sdk.test.ts
git commit -m "功能：扩展标准CRUD前端第二批契约"
```

---

### Task 2: UI 通用字段与表单骨架

**Files:**
- Create: `frontend/packages/ui/src/patterns/StandardCrudField.tsx`
- Create: `frontend/packages/ui/src/patterns/StandardCrudForm.tsx`
- Modify: `frontend/packages/ui/src/index.ts`
- Create: `frontend/packages/shell/src/__tests__/standard-crud-field-form.test.tsx`

- [x] **Step 1: 写 UI 字段与表单失败测试**

Create `frontend/packages/shell/src/__tests__/standard-crud-field-form.test.tsx`:

```tsx
// @vitest-environment jsdom

import { afterEach, describe, expect, it, vi } from 'vitest';
import { act } from 'react';
import { createRoot, type Root } from 'react-dom/client';
import { StandardCrudField, StandardCrudForm } from '@icp-platform/ui';
import type { StandardCrudFieldDefinition } from '@icp-platform/sdk';

globalThis.IS_REACT_ACT_ENVIRONMENT = true;

describe('Standard CRUD UI field and form', () => {
  let container: HTMLDivElement;
  let root: Root;

  afterEach(async () => {
    if (root) {
      await act(async () => root.unmount());
    }
    container?.remove();
  });

  async function render(element: React.ReactElement) {
    container = document.createElement('div');
    document.body.appendChild(container);
    await act(async () => {
      root = createRoot(container);
      root.render(element);
    });
  }

  const fields: StandardCrudFieldDefinition[] = [
    { key: 'personNo', labelKey: '工号', modelField: 'PersonNo', kind: 'text', required: true },
    { key: 'isEnabled', labelKey: '启用状态', modelField: 'IsEnabled', kind: 'boolean' },
    {
      key: 'employmentStatus',
      labelKey: '用工状态',
      modelField: 'EmploymentStatus',
      kind: 'enum',
      options: [{ value: 'FullTime', label: '正式' }],
    },
  ];

  it('renders readonly field values by kind', async () => {
    await render(<StandardCrudField field={fields[1]} value={true} mode="view" onChange={() => undefined} />);

    expect(container.textContent).toContain('启用状态');
    expect(container.textContent).toContain('是');
  });

  it('renders editable inputs and submit button from sections', async () => {
    const onSubmit = vi.fn();
    await render(
      <StandardCrudForm
        formId="person-standard-form"
        mode="edit"
        titleForSection={(key) => key}
        fields={fields}
        sections={[{ key: 'basic', titleKey: '基础信息', fields: ['personNo', 'employmentStatus'] }]}
        values={{ personNo: 'P001', employmentStatus: 'FullTime' }}
        onChange={() => undefined}
        onSubmit={onSubmit}
        saving={false}
      />,
    );

    expect(container.textContent).toContain('基础信息');
    expect(container.textContent).toContain('保存修改');
    expect(container.querySelector('input[value="P001"]')).not.toBeNull();
    expect(container.querySelector('select')).not.toBeNull();
  });

  it('does not render submit button in view mode', async () => {
    await render(
      <StandardCrudForm
        formId="person-standard-form"
        mode="view"
        titleForSection={(key) => key}
        fields={fields}
        sections={[{ key: 'basic', titleKey: '基础信息', fields: ['personNo'] }]}
        values={{ personNo: 'P001' }}
        onChange={() => undefined}
        onSubmit={() => undefined}
        saving={false}
      />,
    );

    expect(container.textContent).not.toContain('保存修改');
  });
});
```

- [x] **Step 2: 运行 UI 测试确认失败**

Run:

```powershell
pnpm --dir .\frontend --filter @icp-platform/shell test -- standard-crud-field-form
```

Expected: FAIL，提示 `StandardCrudField` 或 `StandardCrudForm` 尚未从 `@icp-platform/ui` 导出。

- [x] **Step 3: 实现 `StandardCrudField`**

Create `frontend/packages/ui/src/patterns/StandardCrudField.tsx`:

```tsx
import type { ReactElement } from 'react';
import type { StandardCrudFieldDefinition, StandardCrudFormMode, StandardCrudOption } from '@icp-platform/sdk';
import { Checkbox } from '../components/Checkbox';
import { Input } from '../components/Input';
import { Select } from '../components/Select';
import { StatusBadge } from './StatusBadge';

export interface StandardCrudFieldProps {
  field: StandardCrudFieldDefinition;
  value: unknown;
  mode: StandardCrudFormMode;
  options?: StandardCrudOption[];
  loading?: boolean;
  error?: string | null;
  onChange: (key: string, value: unknown) => void;
}

function formatReadonlyValue(field: StandardCrudFieldDefinition, value: unknown, options: StandardCrudOption[]): ReactElement | string {
  if (field.kind === 'boolean') {
    return value === true ? '是' : '否';
  }

  const matched = options.find((option) => option.value === value);
  if (matched) {
    return matched.label;
  }

  if (value === null || value === undefined || value === '') {
    return '未填写';
  }

  return String(value);
}

export function StandardCrudField({
  field,
  value,
  mode,
  options = field.options ?? [],
  loading = false,
  error = null,
  onChange,
}: StandardCrudFieldProps): ReactElement {
  const readOnly = mode === 'view' || field.readOnly === true;
  const label = field.required && !readOnly ? `${field.labelKey} *` : field.labelKey;

  if (readOnly || field.kind === 'readonly') {
    const rendered = field.kind === 'boolean'
      ? <StatusBadge variant={value === true ? 'success' : 'warning'} label={value === true ? '是' : '否'} />
      : formatReadonlyValue(field, value, options);

    return (
      <div style={{ display: 'grid', gap: 4 }}>
        <span style={{ color: 'var(--sh-color-text-secondary, #64748b)', fontSize: 12 }}>{field.labelKey}</span>
        <span>{rendered}</span>
        {error ? <span style={{ color: 'var(--sh-color-danger, #dc2626)', fontSize: 12 }}>{error}</span> : null}
      </div>
    );
  }

  if (field.kind === 'boolean') {
    return <Checkbox label={label} checked={value === true} onChange={(event) => onChange(field.key, event.target.checked)} />;
  }

  if (field.kind === 'enum' || field.kind === 'lookup') {
    return (
      <Select
        label={label}
        value={typeof value === 'string' ? value : ''}
        onChange={(event) => onChange(field.key, event.target.value)}
        options={options}
        placeholder={loading ? '正在加载...' : field.lookup?.placeholder}
        disabled={loading}
        style={{ minHeight: 40, fontSize: 14 }}
      />
    );
  }

  return (
    <Input
      label={label}
      value={value == null ? '' : String(value)}
      type={field.kind === 'date' ? 'date' : 'text'}
      onChange={(event) => onChange(field.key, event.target.value)}
      style={{ minHeight: 40, fontSize: 14 }}
      required={field.required === true}
    />
  );
}
```

- [x] **Step 4: 实现 `StandardCrudForm`**

Create `frontend/packages/ui/src/patterns/StandardCrudForm.tsx`:

```tsx
import type { FormEvent, ReactElement } from 'react';
import type { StandardCrudFieldDefinition, StandardCrudFormMode, StandardCrudFormSection, StandardCrudOption } from '@icp-platform/sdk';
import { Button } from '../components/Button';
import { ErrorState } from '../components/ErrorState';
import { Panel } from './Panel';
import { StandardCrudField } from './StandardCrudField';

export interface StandardCrudFormProps {
  formId: string;
  mode: StandardCrudFormMode;
  fields: StandardCrudFieldDefinition[];
  sections: StandardCrudFormSection[];
  values: Record<string, unknown>;
  lookupOptions?: Record<string, StandardCrudOption[]>;
  lookupLoading?: Record<string, boolean>;
  lookupErrors?: Record<string, string | null>;
  saving: boolean;
  error?: string | null;
  saveLabel?: string;
  titleForSection: (titleKey: string) => string;
  onChange: (key: string, value: unknown) => void;
  onSubmit: () => void | Promise<void>;
}

export function StandardCrudForm({
  formId,
  mode,
  fields,
  sections,
  values,
  lookupOptions = {},
  lookupLoading = {},
  lookupErrors = {},
  saving,
  error = null,
  saveLabel,
  titleForSection,
  onChange,
  onSubmit,
}: StandardCrudFormProps): ReactElement {
  const fieldByKey = new Map(fields.map((field) => [field.key, field]));
  const effectiveSections = sections.length > 0
    ? sections
    : [{ key: 'default', titleKey: '基础信息', fields: fields.map((field) => field.key) }];

  async function handleSubmit(event: FormEvent<HTMLFormElement>) {
    event.preventDefault();
    if (mode === 'view') {
      return;
    }

    await onSubmit();
  }

  return (
    <>
      {error ? <ErrorState message={error} /> : null}
      <form id={formId} onSubmit={(event) => void handleSubmit(event)} style={{ marginTop: 8 }}>
        {effectiveSections.map((section, index) => (
          <Panel
            key={section.key}
            title={titleForSection(section.titleKey)}
            compact
            actions={mode !== 'view' && index === 0 ? (
              <Button size="sm" variant="primary" type="submit" form={formId} disabled={saving}>
                {saving ? '保存中...' : (saveLabel ?? (mode === 'create' ? '保存并关闭' : '保存修改'))}
              </Button>
            ) : undefined}
          >
            <div style={{ display: 'grid', gridTemplateColumns: 'repeat(auto-fit, minmax(180px, 1fr))', gap: 12 }}>
              {section.fields.map((fieldKey) => {
                const field = fieldByKey.get(fieldKey);
                if (!field) {
                  return null;
                }

                return (
                  <StandardCrudField
                    key={field.key}
                    field={field}
                    value={values[field.key]}
                    mode={mode}
                    options={lookupOptions[field.key]}
                    loading={lookupLoading[field.key]}
                    error={lookupErrors[field.key]}
                    onChange={onChange}
                  />
                );
              })}
            </div>
          </Panel>
        ))}
      </form>
    </>
  );
}
```

- [x] **Step 5: 导出 UI 组件**

Modify `frontend/packages/ui/src/index.ts`:

```ts
export { StandardCrudField } from './patterns/StandardCrudField';
export type { StandardCrudFieldProps } from './patterns/StandardCrudField';

export { StandardCrudForm } from './patterns/StandardCrudForm';
export type { StandardCrudFormProps } from './patterns/StandardCrudForm';
```

- [x] **Step 6: 运行 UI 测试**

Run:

```powershell
pnpm --dir .\frontend --filter @icp-platform/shell test -- standard-crud-field-form
```

Expected: PASS。

- [x] **Step 7: 运行 UI 构建**

Run:

```powershell
pnpm --dir .\frontend --filter @icp-platform/ui build
```

Expected: PASS。

- [x] **Step 8: Commit**

```powershell
git add frontend/packages/ui/src/patterns/StandardCrudField.tsx frontend/packages/ui/src/patterns/StandardCrudForm.tsx frontend/packages/ui/src/index.ts frontend/packages/shell/src/__tests__/standard-crud-field-form.test.tsx
git commit -m "功能：新增标准CRUD通用字段与表单骨架"
```

---

### Task 3: Shell Lookup 运行时

**Files:**
- Create: `frontend/packages/shell/src/standard-crud/standardCrudLookupRuntime.ts`
- Create: `frontend/packages/shell/src/__tests__/standard-crud-lookup-runtime.test.tsx`

- [x] **Step 1: 写 lookup runtime 失败测试**

Create `frontend/packages/shell/src/__tests__/standard-crud-lookup-runtime.test.tsx`:

```tsx
// @vitest-environment jsdom

import { describe, expect, it, vi } from 'vitest';
import { loadStandardCrudLookupOptions } from '../standard-crud/standardCrudLookupRuntime';
import type { StandardCrudFieldDefinition, StandardCrudLookupProvider } from '@icp-platform/sdk';

describe('standard CRUD lookup runtime', () => {
  const fields: StandardCrudFieldDefinition[] = [
    { key: 'companyId', labelKey: '公司', modelField: 'CompanyId', kind: 'lookup', lookup: { providerKey: 'companies' } },
    {
      key: 'departmentId',
      labelKey: '部门',
      modelField: 'DepartmentId',
      kind: 'lookup',
      lookup: { providerKey: 'departments', dependsOn: ['companyId'], currentValueLabelField: 'DepartmentName' },
    },
  ];

  it('loads provider options with explicit dependency values', async () => {
    const provider: StandardCrudLookupProvider = {
      key: 'departments',
      load: vi.fn(async () => [{ value: 'Dept_2', label: '二部' }]),
    };

    const result = await loadStandardCrudLookupOptions({
      field: fields[1],
      providers: [provider],
      values: { companyId: 'Company_1', departmentId: 'Dept_1' },
      detailFields: { DepartmentName: '一部' },
    });

    expect(provider.load).toHaveBeenCalledWith({
      fieldKey: 'departmentId',
      values: { companyId: 'Company_1', departmentId: 'Dept_1' },
      dependencyValues: { companyId: 'Company_1' },
    });
    expect(result.options[0]).toEqual({ value: 'Dept_1', label: '一部' });
    expect(result.error).toBeNull();
  });

  it('skips provider load when a dependency is empty', async () => {
    const provider: StandardCrudLookupProvider = {
      key: 'departments',
      load: vi.fn(async () => [{ value: 'Dept_2', label: '二部' }]),
    };

    const result = await loadStandardCrudLookupOptions({
      field: fields[1],
      providers: [provider],
      values: { companyId: '', departmentId: '' },
      detailFields: {},
    });

    expect(provider.load).not.toHaveBeenCalled();
    expect(result).toEqual({ options: [], error: null });
  });

  it('returns a stable field error when provider fails', async () => {
    const provider: StandardCrudLookupProvider = {
      key: 'companies',
      load: vi.fn(async () => {
        throw new Error('公司接口不可用');
      }),
    };

    const result = await loadStandardCrudLookupOptions({
      field: fields[0],
      providers: [provider],
      values: {},
      detailFields: {},
    });

    expect(result.error).toBe('公司接口不可用');
  });
});
```

- [x] **Step 2: 运行 lookup 测试确认失败**

Run:

```powershell
pnpm --dir .\frontend --filter @icp-platform/shell test -- standard-crud-lookup-runtime
```

Expected: FAIL，提示 `standardCrudLookupRuntime` 模块不存在。

- [x] **Step 3: 实现 lookup 运行时**

Create `frontend/packages/shell/src/standard-crud/standardCrudLookupRuntime.ts`:

```ts
import {
  preserveStandardCrudCurrentOption,
  type StandardCrudFieldDefinition,
  type StandardCrudLookupProvider,
  type StandardCrudOption,
} from '@icp-platform/sdk';

export interface LoadStandardCrudLookupOptionsInput {
  field: StandardCrudFieldDefinition;
  providers: StandardCrudLookupProvider[];
  values: Record<string, unknown>;
  detailFields: Record<string, unknown>;
}

export interface LoadStandardCrudLookupOptionsResult {
  options: StandardCrudOption[];
  error: string | null;
}

function readCurrentOption(field: StandardCrudFieldDefinition, values: Record<string, unknown>, detailFields: Record<string, unknown>) {
  const value = values[field.key];
  if (typeof value !== 'string' || value.length === 0) {
    return null;
  }

  const labelField = field.lookup?.currentValueLabelField;
  const label = labelField && typeof detailFields[labelField] === 'string' ? detailFields[labelField] : value;
  return { value, label };
}

export async function loadStandardCrudLookupOptions({
  field,
  providers,
  values,
  detailFields,
}: LoadStandardCrudLookupOptionsInput): Promise<LoadStandardCrudLookupOptionsResult> {
  if (!field.lookup) {
    return { options: field.options ?? [], error: null };
  }

  const provider = providers.find((item) => item.key === field.lookup?.providerKey);
  if (!provider) {
    return { options: [], error: `未找到 lookup provider：${field.lookup.providerKey}` };
  }

  const dependencyValues: Record<string, unknown> = {};
  for (const dependency of field.lookup.dependsOn ?? []) {
    const dependencyValue = values[dependency];
    if (dependencyValue === null || dependencyValue === undefined || dependencyValue === '') {
      return { options: [], error: null };
    }

    dependencyValues[dependency] = dependencyValue;
  }

  try {
    const options = await provider.load({ fieldKey: field.key, values, dependencyValues });
    return {
      options: preserveStandardCrudCurrentOption(options, readCurrentOption(field, values, detailFields)),
      error: null,
    };
  } catch (loadError) {
    return {
      options: preserveStandardCrudCurrentOption([], readCurrentOption(field, values, detailFields)),
      error: loadError instanceof Error ? loadError.message : '加载选项失败',
    };
  }
}
```

- [x] **Step 4: 运行 lookup 测试**

Run:

```powershell
pnpm --dir .\frontend --filter @icp-platform/shell test -- standard-crud-lookup-runtime
```

Expected: PASS。

- [x] **Step 5: Commit**

```powershell
git add frontend/packages/shell/src/standard-crud/standardCrudLookupRuntime.ts frontend/packages/shell/src/__tests__/standard-crud-lookup-runtime.test.tsx
git commit -m "功能：新增标准CRUD lookup运行时"
```

---

### Task 4: Shell 动作运行时

**Files:**
- Create: `frontend/packages/shell/src/standard-crud/standardCrudActionRuntime.tsx`
- Create: `frontend/packages/shell/src/__tests__/standard-crud-action-runtime.test.tsx`
- Modify: `frontend/packages/shell/src/pages/StandardCrudListPageHost.tsx`
- Modify: `frontend/packages/shell/src/pages/StandardCrudDetailPageHost.tsx`

- [ ] **Step 1: 写动作运行时失败测试**

Create `frontend/packages/shell/src/__tests__/standard-crud-action-runtime.test.tsx`:

```tsx
// @vitest-environment jsdom

import { afterEach, describe, expect, it, vi } from 'vitest';
import { act } from 'react';
import { createRoot, type Root } from 'react-dom/client';
import { StandardCrudActions } from '../standard-crud/standardCrudActionRuntime';
import type { StandardCrudActionDefinition } from '@icp-platform/sdk';

globalThis.IS_REACT_ACT_ENVIRONMENT = true;

describe('standard CRUD action runtime', () => {
  let container: HTMLDivElement;
  let root: Root;

  afterEach(async () => {
    vi.restoreAllMocks();
    if (root) {
      await act(async () => root.unmount());
    }
    container?.remove();
  });

  async function render(actions: StandardCrudActionDefinition[], permissions: string[], handler = vi.fn()) {
    container = document.createElement('div');
    document.body.appendChild(container);
    await act(async () => {
      root = createRoot(container);
      root.render(
        <StandardCrudActions
          actions={actions}
          grantedPermissions={permissions}
          handlers={{ disable: handler }}
          context={{ id: 'Person_1', refresh: vi.fn() }}
        />,
      );
    });
    return handler;
  }

  it('filters actions by required permissions', async () => {
    await render(
      [
        { key: 'edit', labelKey: '编辑', requiredPermissions: ['Organization.Person.Edit'] },
        { key: 'disable', labelKey: '停用', requiredPermissions: ['Organization.Person.Disable'] },
      ],
      ['Organization.Person.Edit'],
    );

    expect(container.textContent).toContain('编辑');
    expect(container.textContent).not.toContain('停用');
  });

  it('runs handler after confirm and shows pending text', async () => {
    vi.spyOn(window, 'confirm').mockReturnValue(true);
    const handler = await render([{ key: 'disable', labelKey: '停用', variant: 'danger', confirmMessage: '确认停用？' }], []);

    const button = container.querySelector('button')!;
    await act(async () => button.dispatchEvent(new MouseEvent('click', { bubbles: true })));

    expect(window.confirm).toHaveBeenCalledWith('确认停用？');
    expect(handler).toHaveBeenCalled();
  });

  it('renders handler errors without throwing', async () => {
    vi.spyOn(window, 'confirm').mockReturnValue(true);
    await render([{ key: 'disable', labelKey: '停用' }], [], async () => {
      throw new Error('停用失败');
    });

    const button = container.querySelector('button')!;
    await act(async () => button.dispatchEvent(new MouseEvent('click', { bubbles: true })));

    expect(container.textContent).toContain('停用失败');
  });
});
```

- [ ] **Step 2: 运行动作测试确认失败**

Run:

```powershell
pnpm --dir .\frontend --filter @icp-platform/shell test -- standard-crud-action-runtime
```

Expected: FAIL，提示 `standardCrudActionRuntime` 模块不存在。

- [ ] **Step 3: 实现动作运行时组件**

Create `frontend/packages/shell/src/standard-crud/standardCrudActionRuntime.tsx`:

```tsx
import { useState, type ReactElement } from 'react';
import { Button } from '@icp-platform/ui';
import type { StandardCrudActionContext, StandardCrudActionDefinition, StandardCrudActionHandler } from '@icp-platform/sdk';

export interface StandardCrudActionsProps<TDetail = unknown, TRow = unknown> {
  actions: StandardCrudActionDefinition[];
  grantedPermissions?: string[];
  handlers?: Record<string, StandardCrudActionHandler<TDetail, TRow>>;
  context: StandardCrudActionContext<TDetail, TRow>;
}

function hasPermission(action: StandardCrudActionDefinition, grantedPermissions: string[]): boolean {
  return (action.requiredPermissions ?? []).every((permission) => grantedPermissions.includes(permission));
}

export function StandardCrudActions<TDetail = unknown, TRow = unknown>({
  actions,
  grantedPermissions = [],
  handlers = {},
  context,
}: StandardCrudActionsProps<TDetail, TRow>): ReactElement {
  const [pendingKey, setPendingKey] = useState<string | null>(null);
  const [error, setError] = useState<string | null>(null);
  const visibleActions = actions.filter((action) => hasPermission(action, grantedPermissions));

  async function runAction(action: StandardCrudActionDefinition) {
    const handler = handlers[action.key];
    if (!handler) {
      return;
    }

    if (action.confirmMessage && !window.confirm(action.confirmMessage)) {
      return;
    }

    try {
      setPendingKey(action.key);
      setError(null);
      await handler(context);
    } catch (actionError) {
      setError(actionError instanceof Error ? actionError.message : '执行操作失败');
    } finally {
      setPendingKey(null);
    }
  }

  return (
    <div style={{ display: 'grid', gap: 6 }}>
      <div style={{ display: 'flex', gap: 8, flexWrap: 'wrap', alignItems: 'center' }}>
        {visibleActions.map((action) => (
          <Button
            key={action.key}
            size="sm"
            variant={action.variant}
            disabled={pendingKey !== null || !handlers[action.key]}
            onClick={() => void runAction(action)}
          >
            {pendingKey === action.key ? '处理中...' : action.labelKey}
          </Button>
        ))}
      </div>
      {error ? <span style={{ color: 'var(--sh-color-danger, #dc2626)', fontSize: 12 }}>{error}</span> : null}
    </div>
  );
}
```

- [ ] **Step 4: 接入 Host 动作入口**

Modify `frontend/packages/shell/src/pages/StandardCrudListPageHost.tsx` and `frontend/packages/shell/src/pages/StandardCrudDetailPageHost.tsx` to prefer `registration.listActions/detailActions` plus `registration.actionHandlers`. Keep existing `renderHeaderActions` and `renderRowActions` as compatibility fallback for modules not migrated yet.

Add this import in both files:

```ts
import { StandardCrudActions } from '../standard-crud/standardCrudActionRuntime';
```

List Host behavior:

```tsx
const headerActions = registration.listActions ? (
  <StandardCrudActions
    actions={registration.listActions}
    grantedPermissions={registration.permissions}
    handlers={registration.actionHandlers}
    context={{ refresh: context.refresh }}
  />
) : registration.renderHeaderActions ? registration.renderHeaderActions(context) : undefined;
```

Row action behavior:

```tsx
render: (row: TRow) => registration.renderRowActions
  ? registration.renderRowActions(row, context)
  : registration.actionHandlers && registration.listActions
    ? (
      <StandardCrudActions
        actions={registration.listActions.filter((action) => action.key !== 'create' && action.key !== 'refresh')}
        grantedPermissions={registration.permissions}
        handlers={registration.actionHandlers}
        context={{ row, refresh: context.refresh }}
      />
    )
    : null,
```

Detail Host behavior:

```tsx
const detailActions = currentDetail && registration.detailActions ? (
  <StandardCrudActions
    actions={registration.detailActions}
    grantedPermissions={registration.permissions}
    handlers={registration.actionHandlers}
    context={{ id, detail: currentDetail as TDetail, refresh }}
  />
) : undefined;
```

Use `detailActions` when `renderDetailHeaderActions` is absent.

- [ ] **Step 5: 运行动作测试**

Run:

```powershell
pnpm --dir .\frontend --filter @icp-platform/shell test -- standard-crud-action-runtime standard-crud-list-host standard-crud-detail-host
```

Expected: PASS。

- [ ] **Step 6: Commit**

```powershell
git add frontend/packages/shell/src/standard-crud/standardCrudActionRuntime.tsx frontend/packages/shell/src/__tests__/standard-crud-action-runtime.test.tsx frontend/packages/shell/src/pages/StandardCrudListPageHost.tsx frontend/packages/shell/src/pages/StandardCrudDetailPageHost.tsx
git commit -m "功能：新增标准CRUD动作运行时"
```

---

### Task 5: Summary Block 状态协议运行时

**Files:**
- Create: `frontend/packages/shell/src/standard-crud/standardCrudSummaryRuntime.tsx`
- Create: `frontend/packages/shell/src/__tests__/standard-crud-summary-runtime.test.tsx`
- Modify: `frontend/packages/shell/src/pages/StandardCrudDetailPageHost.tsx`
- Modify: `frontend/modules/organization/src/components/AuthorizationSummaryCard.tsx`
- Modify: `frontend/modules/organization/src/components/PersonUserLinkCard.tsx`

- [ ] **Step 1: 写 summary runtime 失败测试**

Create `frontend/packages/shell/src/__tests__/standard-crud-summary-runtime.test.tsx`:

```tsx
// @vitest-environment jsdom

import { afterEach, describe, expect, it } from 'vitest';
import { act } from 'react';
import { createRoot, type Root } from 'react-dom/client';
import { StandardCrudSummaryBlocks } from '../standard-crud/standardCrudSummaryRuntime';

globalThis.IS_REACT_ACT_ENVIRONMENT = true;

describe('standard CRUD summary runtime', () => {
  let container: HTMLDivElement;
  let root: Root;

  afterEach(async () => {
    if (root) {
      await act(async () => root.unmount());
    }
    container?.remove();
  });

  async function render(element: React.ReactElement) {
    container = document.createElement('div');
    document.body.appendChild(container);
    await act(async () => {
      root = createRoot(container);
      root.render(element);
    });
  }

  it('renders platform status when a summary is not configured', async () => {
    await render(
      <StandardCrudSummaryBlocks
        blocks={['userLink']}
        summaries={{ userLink: { status: 'notConfigured', data: null } }}
        renderers={{}}
        titleForBlock={(key) => key}
      />,
    );

    expect(container.textContent).toContain('userLink');
    expect(container.textContent).toContain('暂未配置');
  });

  it('uses module renderer only for ready summary content', async () => {
    await render(
      <StandardCrudSummaryBlocks
        blocks={['authorizationSummary']}
        summaries={{ authorizationSummary: { status: 'ready', data: ['Admin'] } }}
        renderers={{ authorizationSummary: ({ block }) => <div>{(block.data as string[]).join(',')}</div> }}
        titleForBlock={(key) => key}
      />,
    );

    expect(container.textContent).toContain('Admin');
  });
});
```

- [ ] **Step 2: 运行 summary 测试确认失败**

Run:

```powershell
pnpm --dir .\frontend --filter @icp-platform/shell test -- standard-crud-summary-runtime
```

Expected: FAIL，提示 `standardCrudSummaryRuntime` 模块不存在。

- [ ] **Step 3: 实现 summary runtime**

Create `frontend/packages/shell/src/standard-crud/standardCrudSummaryRuntime.tsx`:

```tsx
import type { ReactElement } from 'react';
import { SummaryCard } from '@icp-platform/ui';
import {
  resolveStandardCrudSummaryMessage,
  type StandardCrudSummaryBlock,
  type StandardCrudSummaryRenderer,
} from '@icp-platform/sdk';

export interface StandardCrudSummaryBlocksProps {
  blocks: string[];
  summaries: Record<string, StandardCrudSummaryBlock | undefined>;
  renderers?: Record<string, StandardCrudSummaryRenderer>;
  titleForBlock: (key: string) => string;
}

export function StandardCrudSummaryBlocks({
  blocks,
  summaries,
  renderers = {},
  titleForBlock,
}: StandardCrudSummaryBlocksProps): ReactElement | null {
  if (blocks.length === 0) {
    return null;
  }

  return (
    <div className="sh-section-grid">
      {blocks.map((blockKey) => {
        const block = summaries[blockKey] ?? { status: 'notConfigured' as const, data: null };
        const title = titleForBlock(blockKey);
        const renderer = renderers[blockKey];

        if (block.status === 'ready' && renderer) {
          return <div key={blockKey}>{renderer({ blockKey, block, title })}</div>;
        }

        return (
          <SummaryCard
            key={blockKey}
            label={title}
            value={block.status === 'ready' ? '已就绪' : resolveStandardCrudSummaryMessage(block)}
            meta={block.status === 'ready' ? '当前摘要未提供专属展示组件' : '由平台 summary 状态协议降级展示'}
          />
        );
      })}
    </div>
  );
}
```

- [ ] **Step 4: 接入 Detail Host summary runtime**

Modify `frontend/packages/shell/src/pages/StandardCrudDetailPageHost.tsx` so the default detail rendering path can show summary blocks before module detail content. If `registration.renderDetail` exists, keep compatibility and let Organization migrate in Task 7.

Add import:

```ts
import { StandardCrudSummaryBlocks } from '../standard-crud/standardCrudSummaryRuntime';
```

Add helper inside `StandardCrudDetailPageContent`:

```ts
const normalizedDetail = currentDetail && 'fields' in (currentDetail as Record<string, unknown>)
  ? currentDetail as StandardCrudDetail
  : null;
```

Default detail branch:

```tsx
) : normalizedDetail ? (
  <>
    <StandardCrudSummaryBlocks
      blocks={schema.summaryBlocks}
      summaries={normalizedDetail.summaries}
      renderers={registration.summaryRenderers}
      titleForBlock={(key) => key}
    />
    <pre>{JSON.stringify(normalizedDetail.fields, null, 2)}</pre>
  </>
)
```

- [ ] **Step 5: 收缩专属 summary card 的状态责任**

Modify `AuthorizationSummaryCard.tsx` and `PersonUserLinkCard.tsx` so they assume `summary.status === 'ready'` and only render `summary.data` content. Platform status messages must come from `StandardCrudSummaryBlocks`.

Expected content boundary:

```tsx
if (summary.status !== 'ready' || !summary.data) {
  return null;
}
```

Keep their labels, data formatting and module-specific wording unchanged.

- [ ] **Step 6: 运行 summary 测试**

Run:

```powershell
pnpm --dir .\frontend --filter @icp-platform/shell test -- standard-crud-summary-runtime standard-crud-detail-host organization-person-center-detail-layout
```

Expected: PASS。

- [ ] **Step 7: Commit**

```powershell
git add frontend/packages/shell/src/standard-crud/standardCrudSummaryRuntime.tsx frontend/packages/shell/src/__tests__/standard-crud-summary-runtime.test.tsx frontend/packages/shell/src/pages/StandardCrudDetailPageHost.tsx frontend/modules/organization/src/components/AuthorizationSummaryCard.tsx frontend/modules/organization/src/components/PersonUserLinkCard.tsx
git commit -m "功能：新增标准CRUD摘要状态运行时"
```

---

### Task 6: 标准 CRUD 详情编辑流程

**Files:**
- Modify: `frontend/packages/shell/src/pages/StandardCrudDetailPageHost.tsx`
- Modify: `frontend/packages/shell/src/metadata/standardCrudSchemaResolver.ts`
- Create or modify: `frontend/packages/shell/src/__tests__/standard-crud-detail-editor.test.tsx`

- [ ] **Step 1: 写详情编辑失败测试**

Create `frontend/packages/shell/src/__tests__/standard-crud-detail-editor.test.tsx`:

```tsx
// @vitest-environment jsdom

import { afterEach, describe, expect, it, vi } from 'vitest';
import { act } from 'react';
import { createRoot, type Root } from 'react-dom/client';
import { ModuleRegistry } from '@icp-platform/sdk';
import { RuntimeMetadataProvider } from '../metadata/runtimeMetadataContext';
import StandardCrudDetailPageHost from '../pages/StandardCrudDetailPageHost';

globalThis.IS_REACT_ACT_ENVIRONMENT = true;

describe('StandardCrudDetailPageHost editor', () => {
  let container: HTMLDivElement;
  let root: Root;

  afterEach(async () => {
    vi.restoreAllMocks();
    if (root) {
      await act(async () => root.unmount());
    }
    container?.remove();
  });

  async function renderHost() {
    vi.spyOn(ModuleRegistry, 'getAllModules').mockReturnValue([
      {
        metadata: { moduleId: 'organization', displayName: 'Organization', version: '1.0.0' },
        standardCrudResources: [
          {
            resourceKey: 'organization.person',
            apiRoute: '/api/organization/persons',
            listViewKey: 'organization.person.list',
            detailViewKey: 'organization.person.detail',
            createEmptyFilters: () => ({}),
            buildListQuery: () => ({}),
            mapListItem: (value: unknown) => value as Record<string, unknown>,
            getRowId: (row: Record<string, unknown>) => String(row.EID),
            buildColumns: () => [],
            fields: [
              { key: 'personNo', labelKey: '工号', modelField: 'PersonNo', kind: 'text', readOnly: true },
              { key: 'name', labelKey: '姓名', modelField: 'ENAME', kind: 'text', required: true, trim: true },
            ],
            formSections: [{ key: 'basic', titleKey: '基础信息', fields: ['personNo', 'name'] }],
          },
        ],
      },
    ]);

    vi.spyOn(globalThis, 'fetch').mockResolvedValue(
      new Response(JSON.stringify({ fields: { EID: 'Person_1', PersonNo: 'P001', ENAME: '张三' }, summaries: {} }), {
        status: 200,
        headers: { 'Content-Type': 'application/json' },
      }),
    );

    container = document.createElement('div');
    document.body.appendChild(container);
    await act(async () => {
      root = createRoot(container);
      root.render(
        <RuntimeMetadataProvider
          value={{
            manifest: { modules: [] },
            navDocuments: [],
            modelDocuments: [],
            viewDocuments: [
              {
                type: 'view',
                view: {
                  key: 'organization.person.detail',
                  dicKey: 'Person',
                  titleKey: '人员详情',
                  descriptionKey: '维护人员主数据',
                  mode: 'detail',
                  sections: ['basic'],
                },
              },
            ],
          }}
        >
          <StandardCrudDetailPageHost viewKey="organization.person.detail" id="Person_1" />
        </RuntimeMetadataProvider>,
      );
    });
  }

  it('renders standard form when registration declares fields and sections', async () => {
    await renderHost();
    await act(async () => undefined);

    expect(container.textContent).toContain('基础信息');
    expect(container.querySelector('input[value="张三"]')).not.toBeNull();
    expect(container.textContent).toContain('保存修改');
  });
});
```

- [ ] **Step 2: 运行详情编辑测试确认失败**

Run:

```powershell
pnpm --dir .\frontend --filter @icp-platform/shell test -- standard-crud-detail-editor
```

Expected: FAIL，详情 Host 还不会根据 `fields/formSections` 渲染标准表单。

- [ ] **Step 3: 扩展 detail schema sections**

Modify `frontend/packages/shell/src/metadata/standardCrudSchemaResolver.ts`:

```ts
export interface StandardCrudDetailSectionSchema {
  key: string;
  titleKey: string;
  fields: string[];
}
```

Change `StandardCrudDetailRuntimeSchema.sections` from `string[]` to `StandardCrudDetailSectionSchema[]`.

When `view.sections` contains strings, map each string to `{ key, titleKey: key, fields: [] }`. When it contains records, read `key/titleKey/fields`. This keeps first-batch metadata compatible.

- [ ] **Step 4: 实现详情 Host 标准表单分支**

Modify `frontend/packages/shell/src/pages/StandardCrudDetailPageHost.tsx`:

Add imports:

```ts
import {
  normalizeStandardCrudSubmitValues,
  readStandardCrudFieldValue,
  type StandardCrudFieldDefinition,
} from '@icp-platform/sdk';
import { StandardCrudForm } from '@icp-platform/ui';
```

Inside `StandardCrudDetailPageContent`, add state:

```ts
const [formValues, setFormValues] = useState<Record<string, unknown>>({});
const [saveError, setSaveError] = useState<string | null>(null);
const [saving, setSaving] = useState(false);
```

After detail loads, initialize form values:

```ts
const fields = registration.fields ?? [];
const nextValues = Object.fromEntries(fields.map((field) => [field.key, readStandardCrudFieldValue(normalized.fields, field)]));
setFormValues(nextValues);
```

Add submit handler:

```ts
async function handleSave(fields: StandardCrudFieldDefinition[]) {
  try {
    setSaving(true);
    setSaveError(null);
    const values = registration.buildMutationValues
      ? registration.buildMutationValues(formValues)
      : normalizeStandardCrudSubmitValues(fields, formValues);
    await client.update(id, values);
    refresh();
  } catch (submitError) {
    setSaveError(submitError instanceof Error ? submitError.message : '保存标准 CRUD 详情失败');
  } finally {
    setSaving(false);
  }
}
```

Render `StandardCrudForm` when `registration.fields` exists and `registration.renderDetail` is absent:

```tsx
<StandardCrudForm
  formId={`${schema.resourceKey}-detail-form`}
  mode="edit"
  fields={registration.fields}
  sections={registration.formSections ?? []}
  values={formValues}
  saving={saving}
  error={saveError}
  titleForSection={(titleKey) => titleKey}
  onChange={(key, value) => setFormValues((current) => ({ ...current, [key]: value }))}
  onSubmit={() => handleSave(registration.fields ?? [])}
/>
```

- [ ] **Step 5: 运行详情编辑测试**

Run:

```powershell
pnpm --dir .\frontend --filter @icp-platform/shell test -- standard-crud-detail-editor standard-crud-detail-host standard-crud-schema-and-registry
```

Expected: PASS。

- [ ] **Step 6: Commit**

```powershell
git add frontend/packages/shell/src/pages/StandardCrudDetailPageHost.tsx frontend/packages/shell/src/metadata/standardCrudSchemaResolver.ts frontend/packages/shell/src/__tests__/standard-crud-detail-editor.test.tsx
git commit -m "功能：标准CRUD详情接入通用表单"
```

---

### Task 7: Organization 人员资源接入第二批能力

**Files:**
- Modify: `frontend/modules/organization/src/standard-crud/personStandardCrudResource.tsx`
- Modify: `frontend/modules/organization/src/components/PersonEditorForm.tsx`
- Modify: `frontend/modules/organization/src/pages/PersonCreatePage.tsx`
- Modify: `frontend/modules/organization/src/services/organizationService.ts`
- Create or modify: `frontend/packages/shell/src/__tests__/organization-person-phase2-shrink.test.tsx`

- [ ] **Step 1: 写人员收缩失败测试**

Create `frontend/packages/shell/src/__tests__/organization-person-phase2-shrink.test.tsx`:

```tsx
import { describe, expect, it } from 'vitest';
import { personStandardCrudResource } from '@icp-modules/organization';

describe('organization person standard CRUD phase 2 registration', () => {
  it('declares platform fields, form sections, lookup providers and summary renderers', () => {
    expect(personStandardCrudResource.fields?.map((field) => field.key)).toEqual([
      'personNo',
      'name',
      'companyId',
      'departmentId',
      'positionId',
      'employmentStatus',
      'isEnabled',
    ]);
    expect(personStandardCrudResource.formSections?.map((section) => section.key)).toEqual(['basic', 'organization']);
    expect(personStandardCrudResource.lookupProviders?.map((provider) => provider.key)).toEqual([
      'organization.companies',
      'organization.departments',
      'organization.positions',
    ]);
    expect(Object.keys(personStandardCrudResource.summaryRenderers ?? {})).toEqual(['userLink', 'authorizationSummary']);
  });

  it('normalizes person mutation values through platform field definitions', () => {
    const values = personStandardCrudResource.buildMutationValues?.({
      personNo: ' P001 ',
      name: ' 张三 ',
      companyId: '',
      departmentId: '',
      positionId: '',
      employmentStatus: 'FullTime',
      isEnabled: true,
    });

    expect(values).toEqual({
      PersonNo: 'P001',
      ENAME: '张三',
      CompanyId: null,
      DepartmentId: null,
      PositionId: null,
      EmploymentStatus: 'FullTime',
      IsEnabled: true,
    });
  });
});
```

- [ ] **Step 2: 运行人员收缩测试确认失败**

Run:

```powershell
pnpm --dir .\frontend --filter @icp-platform/shell test -- organization-person-phase2-shrink
```

Expected: FAIL，人员资源尚未声明第二批字段、lookup providers 或 summary renderers。

- [ ] **Step 3: 在人员资源注册中声明字段与 sections**

Modify `frontend/modules/organization/src/standard-crud/personStandardCrudResource.tsx`:

```ts
const personFields = [
  { key: 'personNo', labelKey: '工号', modelField: 'PersonNo', kind: 'text', required: true, trim: true, readOnly: true },
  { key: 'name', labelKey: '姓名', modelField: 'ENAME', fallbackFields: ['Name'], kind: 'text', required: true, trim: true },
  {
    key: 'companyId',
    labelKey: '所属公司',
    modelField: 'CompanyId',
    kind: 'lookup',
    emptyAsNull: true,
    lookup: { providerKey: 'organization.companies', placeholder: '请选择公司', currentValueLabelField: 'CompanyName' },
  },
  {
    key: 'departmentId',
    labelKey: '所属部门',
    modelField: 'DepartmentId',
    kind: 'lookup',
    emptyAsNull: true,
    lookup: {
      providerKey: 'organization.departments',
      dependsOn: ['companyId'],
      placeholder: '请先选择公司',
      currentValueLabelField: 'DepartmentName',
    },
  },
  {
    key: 'positionId',
    labelKey: '岗位',
    modelField: 'PositionId',
    kind: 'lookup',
    emptyAsNull: true,
    lookup: {
      providerKey: 'organization.positions',
      dependsOn: ['departmentId'],
      placeholder: '可先保持空白',
      currentValueLabelField: 'PositionName',
    },
  },
  {
    key: 'employmentStatus',
    labelKey: '用工状态',
    modelField: 'EmploymentStatus',
    kind: 'enum',
    emptyAsNull: true,
    options: employmentStatusOptions,
  },
  { key: 'isEnabled', labelKey: '启用状态', modelField: 'IsEnabled', kind: 'boolean', defaultValue: true },
] satisfies StandardCrudFieldDefinition[];

const personFormSections = [
  { key: 'basic', titleKey: '基础信息', fields: ['personNo', 'name'] },
  { key: 'organization', titleKey: '组织信息', fields: ['companyId', 'departmentId', 'positionId', 'employmentStatus', 'isEnabled'] },
];
```

Use SDK helper:

```ts
buildMutationValues: (values) => normalizeStandardCrudSubmitValues(personFields, values),
```

- [ ] **Step 4: 声明组织 lookup providers**

In the same file:

```ts
const organizationLookupProviders: StandardCrudLookupProvider[] = [
  {
    key: 'organization.companies',
    load: async () => (await getCompanies()).map((company) => ({ value: company.id, label: company.name })),
  },
  {
    key: 'organization.departments',
    load: async ({ dependencyValues }) => {
      const companyId = dependencyValues.companyId;
      if (typeof companyId !== 'string' || companyId.length === 0) {
        return [];
      }

      return (await getDepartments(companyId)).map((department) => ({ value: department.id, label: department.name }));
    },
  },
  {
    key: 'organization.positions',
    load: async ({ dependencyValues }) => {
      const departmentId = dependencyValues.departmentId;
      if (typeof departmentId !== 'string' || departmentId.length === 0) {
        return [];
      }

      return (await getPositions(departmentId)).map((position) => ({ value: position.id, label: position.name }));
    },
  },
];
```

- [ ] **Step 5: 声明 summary renderers 与专属动作**

In the same file:

```tsx
const personSummaryRenderers = {
  userLink: ({ block, title }) => <PersonUserLinkCard summary={block} title={title} />,
  authorizationSummary: ({ block, title }) => <AuthorizationSummaryCard summary={block} title={title} />,
};
```

Replace ad hoc disable buttons with action definitions and handlers:

```ts
detailActions: [
  { key: 'disable', labelKey: '停用', variant: 'danger', confirmMessage: '确认停用当前人员吗？' },
],
actionHandlers: {
  disable: async ({ id, refresh }) => {
    if (!id) {
      throw new Error('缺少人员标识，无法停用。');
    }

    await disablePerson(id);
    refresh();
  },
},
```

Keep `renderRowActions` only if row-level navigation still needs a link; otherwise move view/edit/create into action definitions.

- [ ] **Step 6: 收缩 `PersonEditorForm`**

If detail and create flows both use `StandardCrudForm`, delete `PersonEditorForm.tsx`. If create flow needs a compatibility wrapper for this task, replace the file with a thin wrapper that imports `StandardCrudForm` and passes `personFields/personFormSections`. The wrapper must not call `getCompanies/getDepartments/getPositions` directly.

Allowed wrapper shape:

```tsx
export default function PersonEditorForm() {
  return <ErrorState message="人员编辑表单已由标准 CRUD 表单接管。" />;
}
```

Expected: no lookup loading `useEffect` remains in `PersonEditorForm.tsx`.

- [ ] **Step 7: 收缩 `PersonCreatePage`**

Modify `frontend/modules/organization/src/pages/PersonCreatePage.tsx` to use `StandardCrudForm` with `personStandardCrudResource.fields` and `personStandardCrudResource.formSections`. On submit call `createPerson` with values mapped from `personStandardCrudResource.buildMutationValues`, then navigate to `/organization/persons/{created.id}`.

Expected create page responsibilities:

- route compatibility
- initial empty values
- save state and save error
- successful create redirect

It must not load company/department/position options directly; lookup must come through the standard CRUD lookup runtime.

- [ ] **Step 8: Remove service-level generic form mapping**

Modify `frontend/modules/organization/src/services/organizationService.ts`:

- Keep `getCompanies`, `getDepartments`, `getPositions`.
- Keep `mapPersonListItem`, `mapPersonDetailView`, `createPerson`, `updatePerson`, `disablePerson`.
- Remove `toStandardPersonValues` if all call sites now use `personStandardCrudResource.buildMutationValues`.
- Keep `employmentStatusOptions` only as enum option data referenced by field definitions.

- [ ] **Step 9: 运行人员收缩测试**

Run:

```powershell
pnpm --dir .\frontend --filter @icp-platform/shell test -- organization-person-phase2-shrink organization-person-create-flow organization-person-center-detail-layout
```

Expected: PASS。

- [ ] **Step 10: Commit**

```powershell
git add frontend/modules/organization/src/standard-crud/personStandardCrudResource.tsx frontend/modules/organization/src/components/PersonEditorForm.tsx frontend/modules/organization/src/pages/PersonCreatePage.tsx frontend/modules/organization/src/services/organizationService.ts frontend/packages/shell/src/__tests__/organization-person-phase2-shrink.test.tsx
git commit -m "重构：人员中心接入标准CRUD第二批能力"
```

---

### Task 8: 第二批验证记录与交接回填

**Files:**
- Create: `docs/verification/frontend-standard-crud-platformization-phase-2-verification.md`
- Modify: `docs/phases/current-handoff.md`

- [ ] **Step 1: 执行第二批验证命令**

Run:

```powershell
pnpm --dir .\frontend --filter @icp-platform/sdk build
pnpm --dir .\frontend --filter @icp-platform/ui build
pnpm --dir .\frontend --filter @icp-platform/shell test
pnpm --dir .\frontend --filter @icp-platform/shell build
pnpm --dir .\frontend --filter @icp-modules/organization build
rg -n "PersonEditorForm|getCompanies|getDepartments|getPositions|employmentStatusOptions|resolveListText|resolveDetailText|AuthorizationSummaryCard|PersonUserLinkCard" frontend/modules/organization/src frontend/packages
```

Expected:

- SDK、UI、Shell 测试、Shell 构建和 Organization 构建通过。
- 残留扫描命中项必须能解释为专属语义、lookup provider 或兼容包装；不能再出现通用表单字段渲染和 lookup 加载状态机散落在人员组件中。

- [ ] **Step 2: 创建第二批验证记录**

Create `docs/verification/frontend-standard-crud-platformization-phase-2-verification.md`:

```md
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

## 3. 验证结论

- 标准 CRUD 字段、表单、lookup、动作、summary 和 i18n 契约已由 `@icp-platform/sdk` 提供。
- UI 提供无业务语义的标准 CRUD 字段和表单骨架。
- Shell 已接管 lookup 加载、动作状态、summary 状态协议和详情编辑流程。
- Organization 人员中心不再持有通用字段渲染、级联 lookup 加载和 summary 状态协议。

## 4. Organization 暂留项与原因

- `getCompanies / getDepartments / getPositions`：作为 Organization lookup provider 的数据来源保留，业务语义仍属于组织模块。
- `employmentStatusOptions`：作为人员资源 enum option 数据保留，平台只消费 option 契约。
- `PersonUserLinkCard`：人员账号绑定是 Organization 专属摘要内容。
- `AuthorizationSummaryCard`：授权摘要来源和业务含义由模块声明，平台只处理 summary 状态协议。

## 5. 已知风险

- 第二批以 Person 资源为试点，下一批资源接入前仍需逐个确认字段、lookup、动作和 summary 边界。
- 平台表单只承接标准字段和基础提交规范化，不替代模块专属业务校验。

## 6. 回滚思路

- 回滚第二批 SDK/UI/Shell 运行时和 Organization 接入提交。
- 恢复模块私有 `PersonEditorForm`、lookup 加载和 summary 状态渲染。
- 保留第一批列表/详情 Host 接管结果不变。
```

- [ ] **Step 3: 回填 current handoff**

Modify `docs/phases/current-handoff.md`:

- 在“最近完成”中新增“前端标准 CRUD 平台化第二批”，状态为“已完成并验证”。
- 主要依据引用本计划、设计第 14.2 节和第二批验证记录。
- 完成内容写明 SDK/UI/Shell/Organization 四层变化。
- 当前推荐下一步改为“选择下一批标准 CRUD 资源试点，或继续拆解后端 required field / 数据一致性主线”。
- 验证快照追加第二批执行命令和残留扫描结论。
- 回滚思路追加“第二批可独立回滚，不影响第一批列表/详情 Host”。

- [ ] **Step 4: 自检文档无空洞标记**

Run:

```powershell
rg -n "T[B]D|T[O]DO|待[补]|占[位]|稍[后]" docs/verification/frontend-standard-crud-platformization-phase-2-verification.md docs/phases/current-handoff.md
```

Expected: no output。

- [ ] **Step 5: Commit**

```powershell
git add docs/verification/frontend-standard-crud-platformization-phase-2-verification.md docs/phases/current-handoff.md
git commit -m "验证：记录前端标准CRUD第二批收口结果"
```

---

## 最终验证

Run:

```powershell
pnpm --dir .\frontend --filter @icp-platform/sdk build
pnpm --dir .\frontend --filter @icp-platform/ui build
pnpm --dir .\frontend --filter @icp-platform/shell test
pnpm --dir .\frontend --filter @icp-platform/shell build
pnpm --dir .\frontend --filter @icp-modules/organization build
rg -n "PersonEditorForm|getCompanies|getDepartments|getPositions|employmentStatusOptions|resolveListText|resolveDetailText|AuthorizationSummaryCard|PersonUserLinkCard" frontend/modules/organization/src frontend/packages
```

Expected:

- 所有 build/test 命令 PASS。
- 残留扫描命中项均已在验证记录中解释为模块专属语义、lookup provider、enum option 数据或兼容包装。
- `server/**`、`apps/host-api/**`、部署脚本、CI 和 Shell 布局文件没有 diff。

---

## Plan Self-Review

- **Spec coverage:** Task 1 覆盖 SDK 契约、i18n、字段读取、提交规范化和 summary helper；Task 2 覆盖通用字段渲染器与编辑表单；Task 3 覆盖 lookup provider、value list/model lookup 入口和显式级联依赖；Task 4 覆盖动作扩展、确认、pending、错误提示和权限过滤；Task 5 覆盖 summary block 状态协议；Task 6 覆盖详情编辑策略；Task 7 覆盖 Organization 人员中心继续收缩；Task 8 覆盖验证记录、handoff 和残留扫描解释。
- **Placeholder scan:** 本计划不包含空洞标记；所有任务均有具体文件、测试、实现入口、命令和期望结果。
- **Type consistency:** `StandardCrudFieldDefinition`、`StandardCrudLookupProvider`、`StandardCrudActionDefinition`、`StandardCrudSummaryRenderer` 在 SDK 中定义，后续 UI、Shell 和 Organization 任务均复用同一命名。
