# 组件化开发超短版

这是一份给“马上要开始写模块”的超短清单。

如果你没时间先看完整文档，先照这个做。

## 1. 先抄哪个 Example

直接参考这两个目录：

- 后端：`server/modules/ModuleTemplateSample`
- 前端：`frontend/modules/module-template-sample`

详细解释看：

- [componentized-example-module.md](componentized-example-module.md)
- [code-commenting-guidelines.md](code-commenting-guidelines.md)

## 2. 后端最少要有哪些文件

```text
server/modules/YourModule/
├── YourModule.cs
├── Controllers/
├── Services/
└── DTOs/
```

### 后端规则

- `YourModule.cs` 只做模块声明、服务注册、权限声明、配置声明
- `Controllers/*` 保持轻量
- `Services/*` 放模块内部逻辑
- 不要把业务逻辑堆进模块入口

## 3. 前端最少要有哪些文件

```text
frontend/modules/your-module/
├── src/index.ts
├── src/pages/
└── src/services/
```

### 前端规则

- `src/index.ts` 只做 `FrontendModule` 声明
- `src/pages/*` 放页面
- `src/services/*` 放 API 访问
- 不要直接改 Shell 内部结构

## 4. 默认只允许改哪些接入点

正常新增模块时，默认只改：

- 后端：`apps/host-api/HostApi.csproj`
- 前端：`frontend/packages/shell/src/builtin-runtime-plugins.ts`

如果你发现还要改 Host / Shell 其他内部文件，先停下来，先补设计。

## 5. 现在前端装配怎么理解

把当前前端装配记成两句话就够了：

- 正式运行时优先走 `frontend-plugin-manifest.json`
- 研发期联调在 manifest 不可用时回退到 `builtin-runtime-plugins.ts`

也就是说：

- 开发期加一个内建模块，通常改的是 `builtin-runtime-plugins.ts`
- 看正式交付路径时，关注的是运行时 manifest 和动态装配

## 6. 什么情况下不要直接写代码

遇到下面情况先补设计文档：

- 要改 Host / Shell 内部结构
- 现有 `Contracts / SDK / 模板` 不够用
- 要新增新的宿主级接入点
- 要改变前后端插件装配方式

## 7. 最小验证命令

后端：

```powershell
dotnet build .\apps\host-api\HostApi.csproj
dotnet test .\tests\Kernel.Tests\Kernel.Tests.csproj
```

前端：

```bash
cd frontend
pnpm --filter @icp-platform/shell build
```

如果涉及前端插件装配，再补：

```bash
pnpm --filter @icp-platform/shell test
```

## 8. 只记住这 5 句话

1. Host / Shell 负责组合，模块负责声明。
2. 公共能力进 `Kernel / SDK / Contracts`。
3. 模块私有逻辑留在模块内部。
4. 正常接模块时，只改受控入口。
5. 要改宿主内部时，先补设计再实现。

## 9. 推荐阅读顺序

1. 本文档
2. [componentized-example-module.md](componentized-example-module.md)
3. [componentized-development.md](componentized-development.md)
