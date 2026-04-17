# 用户可见文本多语言基础第一包验证

## 1. 范围

本轮验证只覆盖设计文档中的第一包实施范围：

- 宿主启动阶段产出纯 `localization manifest`
- 宿主暴露 `/plugins/localization-manifest.json` 静态路由
- `frontend/packages/shell` 切换为消费 runtime manifest
- 清理 `modules/Platform/metadata` 下的过渡 `dic/enum` 资产
- 下线首页旧多语言/枚举样例面板并收口相关测试

本轮不覆盖：

- 后端用户消息契约 DTO 的正式落地
- 历史模块全量补翻译
- 独立枚举管理页面或更复杂的多语言治理门禁

## 2. 实际执行命令与结果

### 2.0 AI-first 设计校验

执行：

```powershell
powershell -ExecutionPolicy Bypass -File .\scripts\validate-ai-first-design.ps1 -Path .\docs\designs\user-visible-text-localization-foundation-design.md
```

结果：

- 通过
- 已确认设计文档满足当前 AI-first 设计校验要求

### 2.1 Kernel runtime manifest 定向测试

执行：

```powershell
dotnet test .\tests\Kernel.Tests\Kernel.Tests.csproj --filter FullyQualifiedName~RuntimeViewMaterializerTests -nr:false -v minimal
```

结果：

- 通过
- `17` 个测试通过，`0` 失败
- 已覆盖：
  - `runtime/localization-manifest.json` 正常产出
  - `languages.json` 缺失时输出最小 manifest
  - 词典缺少受支持语言时报错
  - `public` swap 失败后 `localization-manifest.json` 随 `runtime` 一起回滚

### 2.2 HostApi 多语言 manifest 路由测试

执行：

```powershell
dotnet test .\tests\Kernel.Tests\Kernel.Tests.csproj --filter FullyQualifiedName~HostApiPluginManifestRouteTests -nr:false -v minimal
```

结果：

- 通过
- `3` 个测试通过，`0` 失败
- 已确认：
  - 宿主在最小测试环境下可启动
  - `/plugins/frontend-plugin-manifest.json` 仍正常返回
  - `/plugins/localization-manifest.json` 可返回 JSON

说明：

- 测试进程环境显式设置了 `Persistence__AutoMigrate=false` 与最小 `ConnectionStrings__SqlServer`，用于避免路由测试被数据库迁移依赖阻塞。

### 2.3 Shell 定向测试

执行：

```powershell
pnpm --dir .\frontend\packages\shell exec vitest --run src/__tests__/runtime-localization-manifest.test.ts src/__tests__/localization-sample.test.tsx src/__tests__/enumeration-sample.test.tsx src/__tests__/home-page-shortcuts.test.tsx src/__tests__/ui-showcase-page.test.tsx
```

结果：

- 通过
- `5` 个测试文件、`9` 个测试全部通过
- 已确认：
  - runtime localization manifest loader 可加载宿主输出
  - `App` 会先加载 manifest，再进入页面渲染
  - 首页旧“语言资源样例 / 枚举资源样例”面板已下线
  - `Home` 门户快捷入口仍可正常渲染
  - 核心枚举 fallback helper 仍保留最小独立测试

备注：

- 输出中仍包含 React Router future-flag warning 与个别 `act(...)` warning
- 当前不影响测试通过，本轮未在同一批处理中额外清理

### 2.4 前端工作区构建

执行：

```powershell
pnpm --dir .\frontend build
```

结果：

- 通过
- `packages/core`、`packages/shell` 及前端 workspace 构建通过

### 2.5 后端解决方案构建

执行：

```powershell
dotnet build .\ICP-Platform.sln -nr:false -v minimal
```

结果：

- 通过
- 解决方案构建成功，`0` warning / `0` error

## 3. 结论

本轮第一包已形成以下闭环：

1. 宿主 runtime 阶段会稳定产出纯 `localization manifest`
2. 前端 `App` 已切换为只消费宿主输出的 manifest
3. `modules/Platform/metadata/languages.json` 保留为全局语言入口
4. `modules/Platform/metadata/dictionaries/*.dic.json` 与 `enumerations/*.json` 已清理
5. Shell 首页不再保留依赖 Platform 过渡资产的样例面板

## 4. 当前边界与已知风险

- 本轮只完成第一包，不包含后端用户消息契约 DTO 的正式实现
- 当前 Shell 已不再在运行时依赖 Platform 过渡枚举样例，但 `core` 层最小枚举 helper 仍通过独立单测保留
- Shell 定向测试仍有非阻塞 warning，后续若继续收口测试噪音，可单独拆包处理

## 5. 对应文档

- [docs/designs/user-visible-text-localization-foundation-design.md](../designs/user-visible-text-localization-foundation-design.md)
- [docs/current-status.md](../current-status.md)
