# 领域文档

本文件定义 engineering skills 在探索 NewP 仓库前如何读取领域知识和架构决策。

## 探索前读取

1. 读取根目录 `CONTEXT-MAP.md`。
2. 根据 map 只读取与当前任务有关的上下文 `CONTEXT.md`。
3. 检查系统级 ADR：`docs/adr/`。
4. 检查目标上下文自己的 ADR 目录。

如果这些文件尚不存在，应静默继续，不要因此阻断任务，也不要主动要求预先创建。`/domain-modeling` 会在术语或架构决策真正形成时按需创建它们。

## 多上下文布局

本仓库采用多上下文布局：

```text
/
├── CONTEXT-MAP.md
├── docs/
│   ├── adr/                         # 跨模块、平台级决策
│   └── contexts/
│       └── <context>/
│           ├── CONTEXT.md           # 当前上下文的术语、职责和边界
│           └── adr/                 # 当前上下文专属决策
├── apps/
├── server/
├── modules/
└── frontend/
```

`CONTEXT-MAP.md` 是唯一的上下文导航入口，负责把逻辑上下文映射到实际代码和文档路径。

不要仅凭目录名把每个 package 或工程自动视为领域上下文。NewP 的一个逻辑上下文可能同时覆盖：

- `server/modules/<ModuleId>/`
- `modules/<ModuleId>/metadata/`
- 对应的前端注册或页面
- `docs/modules/` 下的既有边界与实现说明

只有当某个区域形成独立术语、职责边界或决策集合时，才在 `docs/contexts/<context>/` 下创建上下文文档。

## 使用词汇表术语

输出中的领域概念，包括 issue 标题、规格、重构建议、假设和测试名称，应使用相关 `CONTEXT.md` 定义的术语。

如果所需概念尚未进入词汇表，应先判断：

- 是否正在引入项目并未使用的新说法；
- 是否确实发现了需要由 `/domain-modeling` 补充的领域缺口。

不要在不同模块间随意使用近义词替换已定义术语。

## 标明 ADR 冲突

如果建议与现有 ADR 冲突，必须明确指出，不得静默覆盖。例如：

> 与 ADR-0007 的既有决定冲突；只有重新评审该决策后才能实施。

系统级决定写入 `docs/adr/`；只影响单个上下文的决定写入对应的 `docs/contexts/<context>/adr/`。
