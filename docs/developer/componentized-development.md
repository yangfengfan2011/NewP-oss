# 组件化开发指南

本文档把当前仓库里与“组件化开发”直接相关的规则、契约、接入方式和验证要求整理成一份统一入口，供日常开发直接使用。

适用范围：

- 新增后端模块
- 新增前端模块
- 现有模块扩展
- 模块接入 Host / Shell
- 与模块相关的迁移、导入和插件装配

如果只先看一份文档再开始动手，默认先看这份。

如果更希望先看一个最小、可照着抄的例子，再回来理解规则，直接看：

- [componentized-example-module.md](componentized-example-module.md)
- [code-commenting-guidelines.md](code-commenting-guidelines.md)

## 1. 先记住的核心原则

当前仓库的组件化开发原则可以先记成一句话：

- `Host / Shell` 负责组合与装配，模块负责声明与实现

展开后就是下面这些硬规则：

- 公共能力优先进入 `Kernel / SDK / Contracts`，不要复制到单个模块里
- 模块私有业务规则、私有状态、私有数据访问实现留在模块内部
- 不要为了接一个模块，直接改 `Host` 或 `Shell` 的内部结构
- 如果现有契约或受控入口不够用，先补设计文档，再改宿主层

## 2. 先判断这次改动该落在哪里

开始实现前，先做一次简单分流。

### 落到 `Kernel / SDK / Contracts`

当你新增的是下面这些公共能力时，优先放这里：

- 模块元数据契约
- 菜单、路由、权限、配置声明契约
- 迁移或导入钩子契约
- 可复用的模块接入约束
- 多个模块共享的注册帮助器或扩展点

### 落到模块内部

当你新增的是模块自己的能力时，优先留在模块内部：

- 模块私有 DTO
- 模块私有服务
- 模块私有页面
- 模块私有查询 / 命令逻辑
- 模块私有映射与规则

### 不应直接落到 `Host / Shell`

以下内容默认不应直接放进宿主层：

- 模块私有业务流程
- 为单个模块定制的宿主内部特判
- 模块私有状态管理
- 临时迁移逻辑
- 只服务一个模块的 Shell 内部改造

## 3. Host / Shell 的职责边界

### Host 只负责

- 启动
- 组装
- 模块加载
- 全局中间件
- 统一认证与宿主级接入

### Shell 只负责

- 布局
- 全局 Provider
- 菜单聚合
- 路由聚合
- 模块受控注册

这意味着：

- Host 不承载模块业务规则
- Shell 不承载模块私有页面逻辑
- 模块通过契约接入宿主，而不是反向侵入宿主

## 4. 后端模块怎么开发

后端模块默认按下面的结构理解：

- `*Module.cs`
  只做模块声明与服务注册
- `Controllers/*`
  只做轻量接口入口
- `Services/*`
  只做模块内部服务

后端模块最小应提供：

- `module id`
- `display name`
- `version`
- 服务注册入口
- 权限声明
- 菜单 / 路由声明
- 配置声明
- 如有需要的迁移 / 导入钩子

推荐开发顺序：

1. 先确认边界和职责
2. 再补契约或复用现有契约
3. 再写模块声明与服务注册
4. 最后接入宿主装配并验证

## 5. 前端模块怎么开发

前端模块当前仍以 `FrontendModule` 作为统一合同。

一个前端模块至少需要提供：

- `module id`
- 路由注册
- 菜单注册
- 权限点声明
- i18n 注册
- 页面挂载入口

前端模块默认按下面结构理解：

- `src/index.ts`
  只做模块声明
- `src/pages/*`
  只做页面入口与最小加载逻辑
- `src/services/*`
  只做接口访问

推荐默认保留：

- `default` 导出，便于运行时清单直接装配
- 如确有需要，再补命名导出并通过 manifest 的 `exportName` 指定

## 6. 当前允许修改的受控接入点

### 后端

脚手架默认只允许自动更新：

- `apps/host-api/HostApi.csproj`

### 前端

前端模块正常接入时，默认允许修改的 Shell 级入口是：

- `frontend/packages/shell/src/builtin-runtime-plugins.ts`

当前前端插件化收口之后，还新增了一个正式运行时入口：

- `frontend/packages/shell/src/plugin-manifest-loader.ts`

理解方式是：

- 研发期本地联调：仍可通过 `builtin-runtime-plugins.ts` 的内建模块清单完成
- 正式运行时装配：优先通过 `frontend-plugin-manifest.json` + 动态导入完成

如果一个任务需要改动 Shell 其他内部文件，应该先说明为什么现有契约、清单入口或受控接入点不够用。

## 7. 什么情况下必须先补设计

遇到下面这些情况，不要直接写代码，先补设计文档：

- 需要修改 `Host / Shell` 的内部结构
- 现有 `Contracts / SDK / 模板` 不足以承载需求
- 需要新增新的宿主级接入点
- 需要把一个模块能力提升为公共平台能力
- 需要改变前后端插件装配模型

简单判断法：

1. 这次改动是否只触达允许的接入点？
2. 这次能力是否能通过现有 `Contracts / SDK / 模板` 完成？
3. 如果不能，是否已经有单独设计文档？

只要有一项是否定的，就不应该直接进入实现。

## 8. 当前前端插件化该怎么理解

现在前端的正式方向已经不是“只能源码接线”，而是：

- Shell 优先读取运行时 manifest
- manifest 不可用时回退到研发期内建模块清单
- 清单存在时，单个插件失败不会拖垮整个 Shell

当前 manifest 示例：

```json
{
  "plugins": [
    { "moduleId": "demo" },
    { "moduleId": "files" }
  ]
}
```

但要注意：

- 日常接入内建业务模块时，优先声明 `moduleId`
- `url` / `exportName` 仍然保留，主要用于未来独立前端插件包或外部资源入口
- 这不等于前后端插件包已经完全统一
- 也不等于已经有远程插件市场、版本矩阵或签名治理
- 现在只是先把前端的正式运行时入口收出来

## 9. 开发完成后至少做哪些验证

### 后端改动

至少尝试：

- `dotnet build`
- `dotnet test`

如果涉及插件装配或宿主启动，再补：

- 宿主启动验证
- 插件目录 / 清单 / 兼容行为验证

### 前端改动

至少尝试：

- `pnpm --filter @icp-platform/shell build`

如果涉及前端插件装配，再补：

- `pnpm --filter @icp-platform/shell test`
- 无 runtime manifest 时的本地回退验证
- 有 runtime manifest 时的动态装配验证

## 10. 最常见的误区

- 为了让一个模块跑起来，顺手改 Shell 内部结构
- 把模块私有规则塞进 Host
- 在模块里复制一份本该放进 `SDK / Contracts` 的公共声明
- 把迁移期临时方案固化成长线架构
- 认为“当前能跑”就等于“组件化边界是对的”

## 11. 相关文档

如果要继续往下看，按这个顺序最省时间：

1. [componentized-example-module.md](componentized-example-module.md)
2. [../architecture/componentization-guardrails.md](../architecture/componentization-guardrails.md)
3. [../architecture/module-contract.md](../architecture/module-contract.md)
4. [../architecture/module-boundaries-v1.md](../architecture/module-boundaries-v1.md)
5. [../architecture/frontend-plugin-loading.md](../architecture/frontend-plugin-loading.md)
6. [frontend-plugin-integration.md](frontend-plugin-integration.md)

如果涉及具体模块，再继续看对应的：

- `docs/modules/*-module-boundary-v1.md`
- `docs/designs/*-design.md`
- `docs/modules/*-implementation.md`
