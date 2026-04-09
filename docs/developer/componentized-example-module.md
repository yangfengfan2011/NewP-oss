# 组件化 Example：ModuleTemplateSample

本文档用一个尽量简单、但覆盖前后端接入链路的真实 Example，说明“组件化开发”在当前仓库里应该怎么落地。

这个 Example 对应的真实代码是：

- 后端：`server/modules/ModuleTemplateSample`
- 前端：`frontend/modules/module-template-sample`

它不是伪代码，也不是概念图，而是仓库里已经存在、可构建、可运行的示例。

## 1. 这个 Example 解决什么问题

如果你现在最想搞清楚的是下面这些问题，这个 Example 就是给你看的：

- 一个新模块的后端入口写在哪里
- 服务注册、权限声明、配置声明放在哪里
- 前端模块的 `FrontendModule` 应该怎么写
- 页面层和服务层应该怎么拆
- Shell 到底通过什么入口接模块
- 什么时候改模块，什么时候不应该去改 Host / Shell

这个 Example 的目标不是展示复杂业务，而是展示一条最小、清晰、可复制的组件化路径。

## 2. 先看整体结构

### 后端

```text
server/modules/ModuleTemplateSample/
├── ModuleTemplateSampleModule.cs
├── Controllers/
│   └── SampleStatusController.cs
├── Services/
│   ├── ISampleStatusService.cs
│   └── SampleStatusService.cs
├── DTOs/
│   └── SampleStatusResponse.cs
└── README.md
```

### 前端

```text
frontend/modules/module-template-sample/
├── src/
│   ├── index.ts
│   ├── pages/
│   │   └── ModuleTemplateSamplePage.tsx
│   └── services/
│       └── moduleTemplateSampleService.ts
├── package.json
└── README.md
```

## 3. 后端怎么理解

### 3.1 `ModuleTemplateSampleModule.cs`

这是后端模块入口，也是整个后端模块最重要的文件。

它只负责三件事：

- 声明模块身份
- 注册模块服务
- 暴露模块公共声明

对应到代码里通常就是：

- `ModuleId`
- `DisplayName`
- `Description`
- `ConfigureServices(...)`
- `GetPermissions()`
- `GetConfigSchema()`

为什么这些应该放在这里：

- 这就是“模块对宿主的声明”
- Host 只需要知道模块是谁、要注册什么、暴露哪些公共能力
- Host 不应该在这里看到模块私有业务流程

所以这个文件要尽量保持薄。

### 3.2 `Services/*`

这里放模块内部逻辑。

当前 Example 里只有一个最小服务：

- `ISampleStatusService`
- `SampleStatusService`

它虽然只是返回一条状态消息，但结构上已经体现了正确做法：

- Controller 不直接写业务细节
- 模块入口只做注册
- 实际逻辑放进 `Services/*`

如果以后模块变复杂，优先继续往 `Services/*` 长，而不是把逻辑堆回模块入口或 Controller。

### 3.3 `Controllers/SampleStatusController.cs`

这里展示的是“薄 Controller”。

它只做：

- 接收 HTTP 请求
- 调用服务
- 返回 DTO

当前接口是：

- `GET /api/module-template-sample/status`

它很适合做 Example，因为足够简单，但已经能把后端模块接入链路跑通。

### 3.4 `DTOs/SampleStatusResponse.cs`

DTO 的作用，是把“对外返回结构”从服务实现里分离出来。

在组件化开发里，DTO 的意义很重要：

- 它是模块对外暴露的接口形状
- 它不应该和宿主内部耦合
- 它也不应该直接等于某个数据库实体

## 4. 前端怎么理解

### 4.1 `src/index.ts`

这是前端模块入口，也是前端模块最关键的文件。

它导出的是一个 `FrontendModule`，集中声明：

- `metadata`
- `routes`
- `menus`
- `permissions`
- `i18n`
- `initialize`
- `cleanup`

这正对应组件化里的核心原则：

- Shell 负责组合
- 模块负责声明

Shell 不需要知道这个模块页面怎么写、服务怎么调，只需要读取这些声明，然后聚合成路由和菜单。

### 4.2 `src/pages/ModuleTemplateSamplePage.tsx`

页面组件只负责页面本身：

- 展示文案
- 调用前端服务
- 渲染加载、成功、失败状态

它没有去碰 Shell 内部，也没有直接修改全局结构，这就是“模块页面应该留在模块内部”的具体体现。

### 4.3 `src/services/moduleTemplateSampleService.ts`

这个文件只负责 API 访问：

- 请求 `/api/module-template-sample/status`
- 校验响应
- 返回页面需要的数据

这也是当前仓库推荐的前端模块拆法：

- `src/index.ts` 做模块声明
- `src/pages/*` 做页面入口
- `src/services/*` 做 API 访问

## 5. 这个 Example 怎么接进宿主

### 5.1 后端接入

当前仓库研发期里，后端模块仍可通过项目引用参与宿主构建。

相关入口是：

- `apps/host-api/HostApi.csproj`

但从正式装配模型看，后端主线已经在往插件目录和插件清单装配走。

这里的重点是：

- 模块内部仍按组件化方式写代码
- 宿主如何装配，是宿主层自己的职责
- 模块本身不要反向侵入宿主

### 5.2 前端接入

当前研发期前端接入入口是：

- `frontend/packages/shell/src/builtin-runtime-plugins.ts`

`module-template-sample` 会在这里进入 Shell 的内建模块清单。

这说明了一条非常重要的规则：

- 正常新增前端模块时，默认只修改 `builtin-runtime-plugins.ts`
- 不要直接去改 Shell 其他内部结构

## 6. 这个 Example 和运行时 manifest 的关系

现在前端已经有正式的运行时装配入口：

- `frontend/packages/shell/src/plugin-manifest-loader.ts`

这意味着这个 Example 需要按两条路径来理解。

### 研发期

- 通过 `builtin-runtime-plugins.ts` 的内建模块清单接入
- 适合仓库内联调开发

### 正式运行时

- Shell 优先读取 `frontend-plugin-manifest.json`
- manifest 中优先声明 `moduleId`
- 若 manifest 可用，则按运行时清单动态装配模块

### 一句话记忆

- 正式运行时：优先走 manifest
- 研发期联调：manifest 不可用时回退到 builtin 清单

这层说明很重要，因为它决定了你在“开发期接线”时看的是 `builtin-runtime-plugins.ts`，但在“正式交付模型”里看的是运行时 manifest。

同时也要注意：

- 当前 Example 主要演示“组件内部应该怎么写”
- 不代表前后端插件包已经完成统一交付

## 7. 如果你要照着抄，最小步骤是什么

假设你要做一个新模块 `ExampleModule`，可以直接按这个 Example 走。

### 步骤 1：复制后端结构

至少准备：

- `ExampleModule.cs`
- `Controllers/*`
- `Services/*`
- `DTOs/*`

先让 `ExampleModule.cs` 只负责：

- 模块信息
- 服务注册
- 权限声明
- 配置声明

### 步骤 2：复制前端结构

至少准备：

- `src/index.ts`
- `src/pages/ExamplePage.tsx`
- `src/services/exampleService.ts`

先让 `src/index.ts` 只负责模块声明。

### 步骤 3：接入受控入口

默认只接这些入口：

- 后端：`HostApi.csproj`
- 前端：`builtin-runtime-plugins.ts`

如果你发现还得改 Host / Shell 其他内部文件，先停一下，检查是不是设计没补，或者契约不够。

### 步骤 4：跑最小验证

至少执行：

```powershell
dotnet build .\apps\host-api\HostApi.csproj
dotnet test .\tests\Kernel.Tests\Kernel.Tests.csproj
```

```bash
cd frontend
pnpm --filter @icp-platform/shell build
```

如果涉及前端模块装配，再补：

```bash
pnpm --filter @icp-platform/shell test
```

## 8. 这个 Example 体现了哪些组件化规则

把它再压缩一下，其实就是 5 条：

1. 模块入口只声明，不承载私有业务。
2. 私有逻辑留在模块内部的 `Services/*`、`pages/*`。
3. Controller 保持轻量。
4. 前端模块通过 `FrontendModule` 声明接入 Shell。
5. 正常接模块时，只触达受控入口，不乱改宿主内部。

## 9. 什么时候这个 Example 不够用

这个 Example 故意保持得很小，所以它不适合直接回答下面这些问题：

- 跨模块协作怎么做
- 数据库访问层怎么组织
- 插件包如何正式发布
- 前后端插件包如何一体化交付
- 复杂页面状态管理怎么做

遇到这些问题时，就不能只照抄 Example 了，需要回到设计文档和契约文档。

## 10. 建议阅读顺序

如果你是第一次看这个仓库，建议按下面顺序读：

1. 本文档
2. [componentized-development.md](componentized-development.md)
3. [../architecture/componentization-guardrails.md](../architecture/componentization-guardrails.md)
4. [../architecture/module-contract.md](../architecture/module-contract.md)
5. [../modules/module-template-sample-implementation.md](../modules/module-template-sample-implementation.md)

然后再直接打开代码看：

- `server/modules/ModuleTemplateSample`
- `frontend/modules/module-template-sample`
