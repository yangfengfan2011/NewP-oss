# WP4 Stage 9 Dictionary 静态资源转换验证记录

## 1. 本轮目标

本轮用于完成 `Stage 9` 的最小可执行闭环：

- 将 `Common.dic` 与 `Platform.dic` 转换为受控静态资源
- 将启用语言集合正式收敛到 `resources/localization/languages.json`
- 在 `frontend/packages/core` 中补最小 i18n Provider / hook
- 在 `frontend/packages/shell` 中接入真实静态资源消费样例

## 2. 本轮改动摘要

- 新增 `scripts/migration/dictionary-to-i18n.ps1`
- 新增 `tests/Kernel.Tests/DictionaryToI18nScriptTests.cs`
- 生成 `resources/localization/{zhs,en}/dictionary/*.json`
- 新增 `frontend/packages/core/src/localization/LocalizationContext.tsx`
- 新增 `frontend/packages/core/src/localization/index.ts`
- 更新 `frontend/packages/core/src/index.ts`
- 新增 `frontend/packages/shell/src/localization/catalogs.ts`
- 更新 `frontend/packages/shell/src/App.tsx`
- 更新 `frontend/packages/shell/src/pages/Home.tsx`
- 新增 `frontend/packages/shell/src/__tests__/localization-sample.test.tsx`
- 更新 `frontend/packages/shell/src/__tests__/home-page-shortcuts.test.tsx`
- 更新 `frontend/packages/shell/tsconfig.json`

## 3. 定向验证

### 3.1 脚本级回归测试

执行：

```powershell
dotnet test .\tests\Kernel.Tests\Kernel.Tests.csproj --filter FullyQualifiedName~DictionaryToI18nScriptTests -nr:false -v minimal
```

结果：

- 通过
- `1` 个测试通过
- 已覆盖：启用语言输出、未启用语言跳过、后输入覆盖前输入

### 3.2 真实冻结样本转换

执行：

```powershell
powershell -NoProfile -ExecutionPolicy Bypass -Command "& '.\scripts\migration\dictionary-to-i18n.ps1' -InputPath @('D:\Github\ICP-X\OldP\Runtime\1_Server\Base\Config\Dictionary\Common.dic','D:\Github\ICP-X\OldP\Runtime\1_Server\Platform\Config\Dictionary\Platform.dic') -OutputRoot '.\resources\localization' -LanguagesConfigPath '.\resources\localization\languages.json'"
```

结果：

- 通过
- 输出 `14` 个字典文件（`zhs/en` 各 `7` 个 context 文件）
- 关键样本结果：
  - `resources/localization/zhs/dictionary/Common.json` 共 `51` 个键
  - `resources/localization/zhs/dictionary/Platform.json` 共 `82` 个键
  - `Common.OK = 确定`
  - `Platform.Prompt = Prompt box`（英文）

### 3.3 前端最小 i18n 基座定向测试

执行：

```powershell
pnpm --dir .\frontend\packages\shell exec vitest --run src/__tests__/localization-sample.test.tsx src/__tests__/home-page-shortcuts.test.tsx
```

结果：

- 通过
- `2` 个测试通过
- 已确认 `Home` 页面可消费生成后的静态 Dictionary 资源

备注：

- `vitest` 输出包含 React `act(...)` 与 React Router future flag warning
- 当前不影响测试通过，但后续可单独清理

### 3.4 前端构建验证

执行：

```powershell
pnpm --dir .\frontend build
```

结果：

- 通过
- `packages/core`、`packages/shell` 及前端 workspace 构建通过

## 4. 结论

`Stage 9` 已完成并通过，当前已具备：

1. 受控静态字典资源转换脚本
2. 仓库内正式语言配置入口
3. 最小 i18n Provider / hook
4. Shell 对真实静态字典资源的最小消费闭环

## 5. 当前边界

- 本轮只处理 `Dictionary`，未进入 `Enumeration`
- 本轮未引入语言切换 UI，只完成最小 Provider 与真实资源消费
- `Platform.dic` 除 `Platform` context 外还额外产出若干其他 context 文件，这是样本现状，不视为超范围

## 6. 下一步建议

- 下一包进入 `Stage 10：Enumeration 静态结构清单与本地化资源转换`
