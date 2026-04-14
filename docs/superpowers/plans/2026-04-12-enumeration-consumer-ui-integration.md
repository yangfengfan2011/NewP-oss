# Enumeration 消费层 UI / 接入 Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** 为前端补齐最小通用枚举消费能力，让 `shell` 能基于真实静态枚举资源展示可切语言、可回退的枚举样例，而不再手工拼接枚举 key。

**Architecture:** 在现有 `LocalizationProvider` 上最小扩展，向上下文注入枚举结构与枚举本地化 catalogs，并在 `core` 暴露统一的 `getEnumerationItems / getEnumerationLabel / useEnumeration`。`shell` 只负责聚合真实静态资源并在 `Home` 页面做最小样例接入。

**Tech Stack:** React, TypeScript, Vitest, Vite, JSON static resources

---

## 文件结构

- 修改 `frontend\packages\core\src\localization\LocalizationContext.tsx`
  - 扩展 Provider 入参、上下文能力和 `useEnumeration`
- 修改 `frontend\packages\core\src\localization\index.ts`
  - 导出新增类型与 Hook
- 修改 `frontend\packages\core\src\index.ts`
  - 从包根导出枚举能力
- 修改 `frontend\packages\shell\src\localization\catalogs.ts`
  - 聚合枚举结构资源与本地化资源
- 修改 `frontend\packages\shell\src\App.tsx`
  - 把枚举资源传入 `LocalizationProvider`
- 修改 `frontend\packages\shell\src\pages\Home.tsx`
  - 新增枚举资源样例区块
- 修改 `frontend\packages\shell\src\__tests__\localization-sample.test.tsx`
  - 增补枚举样例断言
- 新增 `frontend\packages\shell\src\__tests__\enumeration-sample.test.tsx`
  - 专门覆盖 `useEnumeration`、语言切换和 fallback

## Task 1: 扩展 Core 枚举消费基座

**Files:**
- Modify: `frontend\packages\core\src\localization\LocalizationContext.tsx`
- Modify: `frontend\packages\core\src\localization\index.ts`
- Modify: `frontend\packages\core\src\index.ts`
- Test: `frontend\packages\shell\src\__tests__\enumeration-sample.test.tsx`

- [ ] **Step 1: 写失败中的枚举基座测试**

```tsx
// frontend/packages/shell/src/__tests__/enumeration-sample.test.tsx
// @vitest-environment jsdom

import { afterEach, describe, expect, it } from 'vitest';
import { act } from 'react';
import { createRoot, Root } from 'react-dom/client';
import { LocalizationProvider, useEnumeration, useLocalization } from '@icp-platform/core';

function EnumerationProbe() {
  const { setLanguage } = useLocalization();
  const gender = useEnumeration('Gender');
  const selectMode = useEnumeration('SelectMode');
  const missing = useEnumeration('NotExists');

  return (
    <div>
      <button type="button" onClick={() => setLanguage('en')}>切换</button>
      <div data-testid="gender-zhs">{gender.items.map(item => item.label).join(',')}</div>
      <div data-testid="select-mode">{selectMode.items.map(item => item.label).join(',')}</div>
      <div data-testid="missing-count">{String(missing.items.length)}</div>
    </div>
  );
}

describe('Enumeration localization sample', () => {
  let container: HTMLDivElement;
  let root: Root;

  afterEach(async () => {
    if (root) {
      await act(async () => {
        root.unmount();
      });
    }

    if (container) {
      container.remove();
    }
  });

  it('returns localized labels, fallback item names, and empty result for missing enumeration', async () => {
    container = document.createElement('div');
    document.body.appendChild(container);

    await act(async () => {
      root = createRoot(container);
      root.render(
        <LocalizationProvider
          defaultLanguage="zhs"
          supportedLanguages={[
            { code: 'zhs', displayName: '简体中文' },
            { code: 'en', displayName: 'English' },
          ]}
          catalogs={{ zhs: {}, en: {} }}
          enumerationDefinitions={{
            Gender: { name: 'Gender', items: [{ name: 'Female' }, { name: 'Male' }] },
            SelectMode: { name: 'SelectMode', items: [{ name: 'MULTI' }, { name: 'SINGLE' }] },
          }}
          enumerationCatalogs={{
            zhs: { Gender: { 'Gender.Female': '女', 'Gender.Male': '男' } },
            en: { Gender: { 'Gender.Female': 'Female', 'Gender.Male': 'Male' } },
          }}
        >
          <EnumerationProbe />
        </LocalizationProvider>,
      );
    });

    expect(container.textContent).toContain('女,男');
    expect(container.textContent).toContain('MULTI,SINGLE');
    expect(container.textContent).toContain('0');
  });
});
```

- [ ] **Step 2: 运行测试，确认它先失败**

Run:

```powershell
pnpm --dir .\frontend\packages\shell exec vitest --run src/__tests__/enumeration-sample.test.tsx
```

Expected:

- FAIL
- 错误应集中在 `LocalizationProvider` 尚不接受 `enumerationDefinitions / enumerationCatalogs`
- 或 `useEnumeration` 尚未导出

- [ ] **Step 3: 在 Core 中补最小实现**

```tsx
// frontend/packages/core/src/localization/LocalizationContext.tsx
export interface EnumerationDefinitionItem {
  name: string;
}

export interface EnumerationDefinition {
  name: string;
  items: EnumerationDefinitionItem[];
}

export type EnumerationDefinitions = Record<string, EnumerationDefinition>;
export type EnumerationCatalog = Record<string, string>;
export type EnumerationCatalogGroup = Record<string, EnumerationCatalog>;
export type EnumerationCatalogs = Record<string, EnumerationCatalogGroup>;

export interface LocalizedEnumerationItem {
  value: string;
  label: string;
}

export interface LocalizedEnumeration {
  name: string;
  items: LocalizedEnumerationItem[];
}

export interface LocalizationContextValue {
  currentLanguage: string;
  defaultLanguage: string;
  supportedLanguages: LocalizationLanguage[];
  setLanguage: (languageCode: string) => void;
  t: (key: string, fallback?: string) => string;
  getEnumerationItems: (enumerationName: string) => EnumerationDefinitionItem[];
  getEnumerationLabel: (enumerationName: string, itemName: string, fallback?: string) => string;
}

export interface LocalizationProviderProps {
  children: ReactNode;
  defaultLanguage: string;
  supportedLanguages: LocalizationLanguage[];
  catalogs: LocalizationCatalogs;
  initialLanguage?: string;
  enumerationDefinitions?: EnumerationDefinitions;
  enumerationCatalogs?: EnumerationCatalogs;
}

const emptyEnumerationDefinitions: EnumerationDefinitions = {};
const emptyEnumerationCatalogs: EnumerationCatalogs = {};

// 在 LocalizationProvider 中：
const getEnumerationItems = (enumerationName: string) => {
  return enumerationDefinitions[enumerationName]?.items ?? [];
};

const getEnumerationLabel = (enumerationName: string, itemName: string, fallback?: string) => {
  const resourceKey = `${enumerationName}.${itemName}`;
  const currentCatalog = enumerationCatalogs[currentLanguage]?.[enumerationName] ?? {};
  if (currentCatalog[resourceKey] !== undefined) {
    return currentCatalog[resourceKey];
  }

  const defaultCatalog = enumerationCatalogs[defaultLanguage]?.[enumerationName] ?? {};
  if (defaultCatalog[resourceKey] !== undefined) {
    return defaultCatalog[resourceKey];
  }

  return fallback ?? itemName;
};

export function useEnumeration(enumerationName: string): LocalizedEnumeration {
  const { getEnumerationItems, getEnumerationLabel } = useLocalization();
  const items = getEnumerationItems(enumerationName).map(item => ({
    value: item.name,
    label: getEnumerationLabel(enumerationName, item.name, item.name),
  }));

  return {
    name: enumerationName,
    items,
  };
}
```

- [ ] **Step 4: 更新导出入口**

```ts
// frontend/packages/core/src/localization/index.ts
export {
  LocalizationProvider,
  useLocalization,
  useEnumeration,
} from './LocalizationContext';

export type {
  LocalizationLanguage,
  LocalizationCatalog,
  LocalizationCatalogs,
  EnumerationDefinition,
  EnumerationDefinitions,
  EnumerationCatalog,
  EnumerationCatalogs,
  LocalizedEnumeration,
  LocalizedEnumerationItem,
} from './LocalizationContext';
```

```ts
// frontend/packages/core/src/index.ts
export * from './localization';
```

- [ ] **Step 5: 重新运行测试，确认转绿**

Run:

```powershell
pnpm --dir .\frontend\packages\shell exec vitest --run src/__tests__/enumeration-sample.test.tsx
```

Expected:

- PASS
- 至少 `1` 个测试通过

- [ ] **Step 6: Commit**

```bash
git add frontend/packages/core/src/localization/LocalizationContext.tsx frontend/packages/core/src/localization/index.ts frontend/packages/core/src/index.ts frontend/packages/shell/src/__tests__/enumeration-sample.test.tsx
git commit -m "feat: add frontend enumeration localization primitives"
```

## Task 2: 接入 Shell 真实枚举资源

**Files:**
- Modify: `frontend\packages\shell\src\localization\catalogs.ts`
- Modify: `frontend\packages\shell\src\App.tsx`
- Test: `frontend\packages\shell\src\__tests__\localization-sample.test.tsx`

- [ ] **Step 1: 扩展现有样例测试，先让真实资源接线失败**

```tsx
// frontend/packages/shell/src/__tests__/localization-sample.test.tsx
expect(container.textContent).toContain('Gender');
expect(container.textContent).toContain('女');
expect(container.textContent).toContain('已读');
expect(container.textContent).toContain('SINGLE');
```

- [ ] **Step 2: 运行测试，确认它因真实枚举资源尚未接入而失败**

Run:

```powershell
pnpm --dir .\frontend\packages\shell exec vitest --run src/__tests__/localization-sample.test.tsx
```

Expected:

- FAIL
- `Home` 页面中还看不到枚举样例内容

- [ ] **Step 3: 在 Shell 聚合真实枚举资源**

```ts
// frontend/packages/shell/src/localization/catalogs.ts
import genderDefinition from '../../../../../resources/enumerations/Gender.json';
import messageStateDefinition from '../../../../../resources/enumerations/MessageState.json';
import selectModeDefinition from '../../../../../resources/enumerations/SelectMode.json';
import genderZhsCatalog from '../../../../../resources/localization/zhs/enumerations/Gender.json';
import genderEnCatalog from '../../../../../resources/localization/en/enumerations/Gender.json';
import messageStateZhsCatalog from '../../../../../resources/localization/zhs/enumerations/MessageState.json';
import messageStateEnCatalog from '../../../../../resources/localization/en/enumerations/MessageState.json';

export const enumerationDefinitions = {
  Gender: genderDefinition,
  MessageState: messageStateDefinition,
  SelectMode: selectModeDefinition,
};

export const enumerationCatalogs = {
  zhs: {
    Gender: genderZhsCatalog,
    MessageState: messageStateZhsCatalog,
  },
  en: {
    Gender: genderEnCatalog,
    MessageState: messageStateEnCatalog,
  },
};
```

- [ ] **Step 4: 把真实枚举资源注入 Provider**

```tsx
// frontend/packages/shell/src/App.tsx
import {
  dictionaryCatalogs,
  dictionaryLanguageConfig,
  enumerationCatalogs,
  enumerationDefinitions,
} from './localization/catalogs';

<LocalizationProvider
  defaultLanguage={dictionaryLanguageConfig.defaultLanguage}
  supportedLanguages={dictionaryLanguageConfig.supportedLanguages}
  catalogs={dictionaryCatalogs}
  enumerationDefinitions={enumerationDefinitions}
  enumerationCatalogs={enumerationCatalogs}
>
```

- [ ] **Step 5: 重新运行样例测试，确认真实资源已接入**

Run:

```powershell
pnpm --dir .\frontend\packages\shell exec vitest --run src/__tests__/localization-sample.test.tsx
```

Expected:

- PASS
- 页面渲染可以读到真实枚举资源

- [ ] **Step 6: Commit**

```bash
git add frontend/packages/shell/src/localization/catalogs.ts frontend/packages/shell/src/App.tsx frontend/packages/shell/src/__tests__/localization-sample.test.tsx
git commit -m "feat: wire enumeration static resources into shell localization"
```

## Task 3: 在 Home 页面展示枚举样例并完成回归

**Files:**
- Modify: `frontend\packages\shell\src\pages\Home.tsx`
- Modify: `frontend\packages\shell\src\__tests__\localization-sample.test.tsx`
- Modify: `frontend\packages\shell\src\__tests__\enumeration-sample.test.tsx`

- [ ] **Step 1: 写页面级失败断言**

```tsx
// frontend/packages/shell/src/__tests__/localization-sample.test.tsx
expect(container.textContent).toContain('枚举资源样例');
expect(container.textContent).toContain('Gender');
expect(container.textContent).toContain('Female');
expect(container.textContent).toContain('MessageState');
expect(container.textContent).toContain('Read');
expect(container.textContent).toContain('SelectMode');
expect(container.textContent).toContain('SINGLE');
```

- [ ] **Step 2: 运行测试，确认页面样例尚未渲染**

Run:

```powershell
pnpm --dir .\frontend\packages\shell exec vitest --run src/__tests__/localization-sample.test.tsx src/__tests__/enumeration-sample.test.tsx
```

Expected:

- `enumeration-sample.test.tsx` 可能已通过
- `localization-sample.test.tsx` 因页面没有枚举样例而 FAIL

- [ ] **Step 3: 在 Home 页面接入统一枚举 Hook**

```tsx
// frontend/packages/shell/src/pages/Home.tsx
import { useEnumeration, useLocalization } from '@icp-platform/core';

const genderEnumeration = useEnumeration('Gender');
const messageStateEnumeration = useEnumeration('MessageState');
const selectModeEnumeration = useEnumeration('SelectMode');

<section className="sh-page-panel">
  <div className="sh-page-panel-header">
    <div>
      <h2 className="sh-page-panel-title">枚举资源样例</h2>
      <div className="sh-page-panel-meta">缺翻译项统一回退到 ItemName</div>
    </div>
  </div>
  <div className="sh-page-panel-body">
    <ul className="sh-feature-list">
      <li className="sh-feature-item">
        <span className="sh-feature-icon">→</span>
        <span>Gender: {genderEnumeration.items.map(item => `${item.value}=${item.label}`).join(' / ')}</span>
      </li>
      <li className="sh-feature-item">
        <span className="sh-feature-icon">→</span>
        <span>MessageState: {messageStateEnumeration.items.map(item => `${item.value}=${item.label}`).join(' / ')}</span>
      </li>
      <li className="sh-feature-item">
        <span className="sh-feature-icon">→</span>
        <span>SelectMode: {selectModeEnumeration.items.map(item => `${item.value}=${item.label}`).join(' / ')}</span>
      </li>
    </ul>
  </div>
</section>
```

- [ ] **Step 4: 运行定向测试，确认页面与 Hook 一起转绿**

Run:

```powershell
pnpm --dir .\frontend\packages\shell exec vitest --run src/__tests__/localization-sample.test.tsx src/__tests__/enumeration-sample.test.tsx
```

Expected:

- PASS
- 两个测试文件全部通过

- [ ] **Step 5: 跑前端构建回归**

Run:

```powershell
pnpm --dir .\frontend build
```

Expected:

- PASS
- `packages/core` 与 `packages/shell` 构建通过

- [ ] **Step 6: Commit**

```bash
git add frontend/packages/shell/src/pages/Home.tsx frontend/packages/shell/src/__tests__/localization-sample.test.tsx frontend/packages/shell/src/__tests__/enumeration-sample.test.tsx
git commit -m "feat: add enumeration localization sample to shell home"
```

## Task 4: 文档与状态收口

**Files:**
- Modify: `docs\current-status.md`
- Modify: `docs\phases\post-phase-6-stage-map.md`
- Modify: `docs\verification\verification-matrix.md`
- Add: `docs\verification\enumeration-consumer-ui-integration-verification.md`

- [ ] **Step 1: 新增验证记录文档**

```md
# Enumeration 消费层 UI / 接入验证记录

## 本轮改动

- 扩展 `LocalizationProvider` 支持枚举结构与枚举本地化 catalogs
- 新增 `useEnumeration`
- 在 Shell 接入真实 `Gender / MessageState / SelectMode` 资源
- 在 Home 页面新增枚举资源样例区块

## 定向验证

- `pnpm --dir .\frontend\packages\shell exec vitest --run src/__tests__/localization-sample.test.tsx src/__tests__/enumeration-sample.test.tsx`
- `pnpm --dir .\frontend build`
```

- [ ] **Step 2: 更新状态文档与阶段总控**

```md
// docs/current-status.md
- 当前已完成枚举消费层最小 UI / 接入闭环
- 后续建议转向真实业务页消费或下一批枚举样本扩展
```

```md
// docs/phases/post-phase-6-stage-map.md
- 在 Stage 10 后补充“枚举消费接入”作为后续已完成子包或新里程碑记录
```

- [ ] **Step 3: 更新验证矩阵**

```md
### Enumeration 消费层 UI / 接入

- `pnpm --dir .\frontend\packages\shell exec vitest --run src/__tests__/localization-sample.test.tsx src/__tests__/enumeration-sample.test.tsx`
- `pnpm --dir .\frontend build`
```

- [ ] **Step 4: 运行文档相关最小核对**

Run:

```powershell
Get-Content .\docs\current-status.md,.\docs\phases\post-phase-6-stage-map.md,.\docs\verification\verification-matrix.md,.\docs\verification\enumeration-consumer-ui-integration-verification.md | Select-String -Pattern '枚举消费|useEnumeration|enumeration-sample'
```

Expected:

- PASS
- 四份文档都能查到本轮入口和验证记录

- [ ] **Step 5: Commit**

```bash
git add docs/current-status.md docs/phases/post-phase-6-stage-map.md docs/verification/verification-matrix.md docs/verification/enumeration-consumer-ui-integration-verification.md
git commit -m "docs: record enumeration consumer integration status"
```

## Self-Review

- 规格覆盖：设计文档中的核心目标“通用消费入口、真实资源接线、最小页面样例、缺翻译 fallback、最小定向验证”都已映射到任务 1-4。
- 占位词扫描：计划中未保留 `TODO / TBD / 待补` 这类占位项。
- 类型一致性：统一使用 `enumerationDefinitions / enumerationCatalogs / useEnumeration / getEnumerationLabel / getEnumerationItems` 这一组命名，不混用其他别名。

## Execution Handoff

Plan complete and saved to `docs/superpowers/plans/2026-04-12-enumeration-consumer-ui-integration.md`. Two execution options:

**1. Subagent-Driven (recommended)** - 我按任务拆给独立子智能体执行，我来做中间审查和整合

**2. Inline Execution** - 我在当前会话里按这个计划直接实现，并在关键节点做验证
