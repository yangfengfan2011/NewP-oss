# Enumeration 消费层 UI / 接入设计

## 1. 背景

`WP4 Stage 9` 已完成 `Dictionary` 静态资源转换与最小 i18n 基座，`WP4 Stage 10` 已完成 `Enumeration` 静态结构清单与本地化资源转换。当前仓库已经具备：

- `resources/enumerations/*.json` 作为静态枚举结构清单
- `resources/localization/<language>/enumerations/*.json` 作为枚举本地化资源
- `frontend/packages/core` 中最小 `LocalizationProvider / useLocalization`

但前端消费层尚未形成统一枚举读取入口，页面如果要使用枚举资源，仍需要自己拼接资源键或直接读取原始 JSON。这会让后续页面接入重复、分散，也不利于 AI-first 的统一消费模式。

因此需要在当前最小 i18n 基座上补一个最小、可复用的枚举消费入口，让前端页面能够统一读取：

- 枚举项结构
- 当前语言下的显示文本
- 缺翻译时的回退值

## 2. 目标

- 在 `frontend/packages/core` 中补齐最小通用枚举消费能力。
- 让 `frontend/packages/shell` 能以真实生成资源完成最小枚举消费样例。
- 让消费侧不再手工拼接 `{EnumerationName}.{ItemName}` 键。
- 明确缺翻译时回退到 `ItemName` 的统一行为。
- 保持当前 `LocalizationProvider` 单一入口，不新增第二套国际化上下文。

## 3. 非目标

- 不做代码生成或强类型枚举 SDK。
- 不做独立的枚举管理页面。
- 不做后端 API、数据库投影或 `ConfigCenter` 扩展。
- 不处理 DB Enumeration。
- 不做复杂表单控件、筛选控件或通用下拉组件封装。
- 不重构现有 `Dictionary` 资源接入方式。

## 4. 设计原则

- **沿用现有 Provider**：在当前 `LocalizationProvider` 上最小扩展，不新增第二套 Provider。
- **结构与文案分离**：`resources/enumerations/*.json` 是结构权威，`resources/localization/<language>/enumerations/*.json` 是显示文本来源。
- **消费统一收口**：页面通过统一 Hook / Helper 消费，不直接散落在页面里拼接 key。
- **缺翻译可回退**：如果本地化资源缺失，则统一回退到 `ItemName`，不阻塞页面渲染。
- **实现范围最小**：本轮只形成“通用入口 + 最小页面样例 + 定向测试”的闭环。

### 4.1 AI-first 最小检查

- 入口在哪里：当前设计入口是本文档；当前实施状态入口是 `docs/current-status.md` 与 `docs/phases/post-phase-6-stage-map.md`；后续实现入口将落在 `frontend/packages/core` 与 `frontend/packages/shell`。
- 边界在哪里：本设计只覆盖枚举消费层的最小 UI / 接入，不包含枚举转换脚本、DB Enumeration、后端 API、数据库投影、代码生成体系与独立枚举管理页面。
- 如何验证：通过 `vitest` 定向测试验证正常翻译、语言切换、缺翻译回退与空枚举结果，通过 `pnpm --dir .\\frontend build` 验证前端构建仍然通过。
- 失败信号是什么：若页面仍需手工拼接枚举 key、缺翻译返回空白、语言切换后枚举 label 不更新、或接入方案引入第二套 Provider，则本轮视为未达成目标。
- 如何回滚：本轮回滚以删除新增的枚举消费入口与样例接线为主，不触碰 `Stage 9 / Stage 10` 已生成的静态资源与转换脚本。

## 5. 方案对比

### 5.1 方案 A：仅在 Shell 中实现专用枚举 Hook

优点：

- 改动最少
- 最快出页面样例

缺点：

- 通用能力停留在 `shell`
- 后续其他前端包要消费枚举时会重复实现

### 5.2 方案 B：在 Core 中实现通用枚举基座，由 Shell 提供真实资源并接入样例

优点：

- 与现有 `LocalizationProvider` 边界一致
- 后续其他前端包可以直接复用
- 页面层只负责使用，不关心底层资源细节

缺点：

- 比方案 A 多一层通用接口设计

### 5.3 方案 C：从静态资源自动生成 TS 常量 / 类型 / SDK

优点：

- 长期可获得更强类型约束

缺点：

- 明显超出本轮最小接入范围
- 会把消费接入扩成代码生成体系

### 5.4 结论

本轮采用 **方案 B**：

- 在 `frontend/packages/core` 中补通用枚举消费能力
- 在 `frontend/packages/shell` 中接入真实资源与最小页面样例

## 6. 目标结构

### 6.1 枚举结构资源

文件示例：

```json
{
  "name": "Gender",
  "items": [
    { "name": "Female" },
    { "name": "Male" }
  ]
}
```

### 6.2 枚举本地化资源

文件示例：

```json
{
  "Gender.Female": "女",
  "Gender.Male": "男"
}
```

### 6.3 前端消费目标形态

消费侧不直接操作原始 JSON，而是统一得到：

```ts
[
  { value: 'Female', label: '女' },
  { value: 'Male', label: '男' }
]
```

若某项缺失翻译，则：

```ts
{ value: 'SINGLE', label: 'SINGLE' }
```

## 7. 组件与职责

### 7.1 `frontend/packages/core`

负责提供通用消费入口。

建议在现有 `LocalizationContext` 中扩展以下能力：

- `getEnumerationItems(name: string)`
- `getEnumerationLabel(name: string, itemName: string, fallback?: string)`
- `useEnumeration(name: string)`

其中：

- `getEnumerationItems` 返回结构清单原始项
- `getEnumerationLabel` 根据当前语言获取枚举项显示文本
- `useEnumeration` 返回当前页面最常用的结构化结果，例如：

```ts
{
  name: 'Gender',
  items: [
    { value: 'Female', label: '女' },
    { value: 'Male', label: '男' }
  ]
}
```

### 7.2 `frontend/packages/shell`

负责接入真实枚举资源并提供最小演示页面样例。

建议在 `catalogs.ts` 旁边统一聚合：

- 结构资源：`resources/enumerations/*.json`
- 本地化资源：`resources/localization/{zhs,en}/enumerations/*.json`

首批接入样本限定为：

- `Gender`
- `MessageState`
- `SelectMode`

原因：

- `Gender` 适合验证正常翻译
- `MessageState` 适合验证常规双语切换
- `SelectMode` 适合验证缺翻译 fallback

## 8. 数据流

### 8.1 初始化

1. `shell` 从仓库静态资源导入枚举结构与枚举本地化 catalogs
2. `shell` 将这些资源传入 `LocalizationProvider`
3. `LocalizationProvider` 将语言信息、字典 catalogs、枚举结构与枚举本地化 catalogs 一起放入上下文

### 8.2 页面消费

1. 页面通过 `useEnumeration('Gender')` 获取结构化枚举结果
2. `useEnumeration` 从结构清单读取项列表
3. 对每一项调用 `getEnumerationLabel`
4. `getEnumerationLabel` 先查当前语言，再查默认语言
5. 若仍无翻译，则回退到 `fallback ?? itemName`

## 9. 文件边界

建议修改或新增以下文件：

- 修改 `frontend/packages/core/src/localization/LocalizationContext.tsx`
- 修改 `frontend/packages/core/src/localization/index.ts`
- 修改 `frontend/packages/core/src/index.ts`
- 修改 `frontend/packages/shell/src/localization/catalogs.ts`
- 修改 `frontend/packages/shell/src/App.tsx`
- 修改 `frontend/packages/shell/src/pages/Home.tsx`
- 修改 `frontend/packages/shell/src/__tests__/localization-sample.test.tsx`
- 新增 `frontend/packages/shell/src/__tests__/enumeration-sample.test.tsx`

本轮不新增新的前端包，也不改后端代码。

## 10. 错误处理与回退规则

- 若请求不存在的枚举名，返回空项列表，不抛出页面级异常。
- 若当前语言不存在对应枚举翻译，回退到默认语言。
- 若默认语言也不存在对应枚举翻译，回退到 `fallback ?? itemName`。
- 若结构资源存在、本地化资源完全缺失，页面仍应能展示基础值列表。
- 若 Provider 未注入枚举资源，`useEnumeration` 应表现为安全空结果，而不是导致整个页面崩溃。

## 11. 验证方案

### 11.1 红灯测试

先补前端测试，至少覆盖：

- `Gender` 在当前语言下返回正确 label
- 切换语言后 `Gender` label 变化
- `SelectMode` 缺翻译时回退到 `ItemName`
- 请求不存在的枚举时返回空结果

### 11.2 最小定向验证

建议执行：

```powershell
pnpm --dir .\frontend\packages\shell exec vitest --run src/__tests__/localization-sample.test.tsx src/__tests__/enumeration-sample.test.tsx
```

```powershell
pnpm --dir .\frontend build
```

## 12. 风险与控制

| 风险 | 影响 | 控制方式 |
|------|------|----------|
| 将枚举能力拆成第二套 Provider | 上下文边界混乱、接入重复 | 明确要求在现有 `LocalizationProvider` 上最小扩展 |
| 页面层继续手工拼 key | 后续接入不一致 | 强制通过 `useEnumeration / getEnumerationLabel` 消费 |
| 缺翻译直接显示空白 | 页面体验不稳定 | 明确回退到默认语言，再回退到 `ItemName` |
| 一次接入过多枚举样本 | 本轮范围扩大 | 首批只接 `Gender`、`MessageState`、`SelectMode` |

## 13. 实施边界结论

本轮“枚举消费层 UI / 接入”应被收敛为：

- `core` 层的最小通用枚举消费能力
- `shell` 层的真实资源接线
- 一个最小页面样例
- 一组定向前端验证

不应扩展为：

- 枚举管理系统
- 枚举代码生成体系
- 数据库或 API 路线重开
