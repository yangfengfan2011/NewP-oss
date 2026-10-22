# 领域文档

本文件定义 engineering skills 如何读取和维护 NewP 的领域知识、架构决策，以及新旧技能文档名称的兼容映射。

## 探索前读取

1. 读取根目录 `CONTEXT-MAP.md`；技能要求的 `GLOSSARY-MAP.md` 导航映射到此文件。
2. 根据 map 只读取与当前任务有关的上下文 `CONTEXT.md`；技能要求的 `GLOSSARY.md` 术语读写映射到所选文档的术语部分（如 `## Language`）。
3. 检查系统级 ADR：`docs/adr/`。
4. 检查目标上下文自己的 ADR 目录。

以上仅为技能名称兼容，不重命名现有文档，也不因 `GLOSSARY-MAP.md` 或 `GLOSSARY.md` 缺失而创建重复真源、别名文件。映射后的实际文档尚不存在时静默继续，不要求预建；术语或架构决策真正形成后，在任务授权范围内按下述现有布局补充。

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
└── frontend/
    └── modules/                    # 可移动组件源码根，支持组合嵌套
```

`CONTEXT-MAP.md` 是唯一的上下文导航入口，负责把逻辑上下文映射到实际代码和文档路径。

不要仅凭目录名把每个 package 或工程自动视为领域上下文。NewP 的一个逻辑上下文可能同时覆盖：

- `server/modules/` 下按组件身份定位的后端 owner 目录
- `frontend/modules/` 下组件源码根的 `metadata/`（由 `ComponentSourceCatalog` 按身份定位；slug 与嵌套布局见[组件开发指南](../developer/componentized-development.md)）
- 对应的前端注册或页面
- `docs/contexts/<context>/CONTEXT.md` 中已确认的术语与边界（如已建立）

只有当某个区域形成独立术语、职责边界或决策集合且属于本次授权范围时，才在 `docs/contexts/<context>/` 下按需创建上下文文档，并纳入现有 map。

## 使用词汇表术语

输出中的领域概念，包括 issue 标题、规格、重构建议、假设和测试名称，应使用相关 `CONTEXT.md` 术语部分定义的用语。技能的词汇表写入仅更新该部分；保留文档中的职责、边界、行为契约和 ADR 引用，不把整份 `CONTEXT.md` 当作纯词汇表覆盖。新增术语只在概念已确认时写入，缺少术语部分则在现有上下文文档中按需补充。

如果所需概念尚未进入词汇表，应先判断：

- 是否正在引入项目并未使用的新说法；
- 是否确实发现了需要由 `/domain-modeling` 补充的领域缺口。

不要在不同模块间随意使用近义词替换已定义术语。

## 标明 ADR 冲突

如果建议与现有 ADR 冲突，必须明确指出，不得静默覆盖。例如：

> 与 ADR-0007 的既有决定冲突；只有重新评审该决策后才能实施。

系统级决定写入 `docs/adr/`；只影响单个上下文的决定写入对应的 `docs/contexts/<context>/adr/`。
