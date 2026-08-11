# 开发者文档目录重组设计

## 1. 背景

当前 `docs/guides/` 目录同时承载了多种类型的文档：

- 指导开发者如何开发、接入和扩展的文档
- 本地环境准备文档
- 运行记录文档
- 打包、升级、回滚等运维操作说明
- 协作工具与工作方式说明

这种混放方式的直接问题是：

- 开发者导向文档不够集中
- 新成员很难快速分辨“先看开发指南”还是“先看运行/运维说明”
- 新补充的持久化开发者手册虽然已经落地，但仍然继续混在 `guides` 中

用户希望把“指导开发者如何开发”的文档单独收进一个文件夹下，而不对设计文档、架构文档和运行/运维文档做同样处理。

## 2. 目标

- 新建一个专门的开发者文档目录。
- 把当前仓库里“指导开发者如何开发、如何接入、如何扩展”的文档集中到该目录下。
- 修复这些文档之间的相对链接。
- 修复开发者实际会使用的主入口文档引用，使新目录可发现、可跳转。

## 3. 非目标

- 本轮不重组全部 `docs/` 分类。
- 本轮不迁移架构规范文档，例如 `docs/architecture/ai-metadata-authoring-guide.md`。
- 本轮不迁移本地环境、运行记录、插件运维、升级回滚、协作工具说明等非开发者开发指南文档。
- 本轮不大规模修复所有历史 `spec / plan` 文档中的旧路径引用；这些文档作为历史记录允许保留旧路径。

## 4. 归档范围

本轮新建目录：

- `docs/developer/`

首批迁入文档：

- `docs/guides/componentized-development-quickstart.md`
- `docs/guides/componentized-development.md`
- `docs/guides/componentized-example-module.md`
- `docs/guides/external-plugin-development.md`
- `docs/guides/frontend-plugin-integration.md`
- `docs/guides/persistence-public-api.md`
- `docs/guides/persistence-usage-guide.md`
- `docs/guides/persistence-development-workflow.md`

继续留在原位置的文档示例：

- `docs/guides/local-environment.md`
- `docs/guides/local-multi-db-container-setup.md`
- `docs/guides/2026-04-09-local-multi-db-runtime-record.md`
- `docs/guides/plugin-packaging.md`
- `docs/guides/plugin-upgrade-and-rollback.md`
- `docs/guides/unified-plugin-package-operations.md`
- `docs/guides/external-plugin-debugging.md`
- `docs/guides/subagents-guide.md`
- `docs/guides/worktree-guide.md`

## 5. 方案选择

### 5.1 方案 A：只移动持久化三篇文档

优点：

- 改动最小。

缺点：

- 开发者文档依然分散。
- 无法真正形成“开发者文档入口”。

### 5.2 方案 B：新建 `docs/developer/`，迁入开发者导向文档

优点：

- 符合用户“单独放在一个文件夹下”的目标。
- 边界清晰。
- 对现有目录结构冲击可控。

缺点：

- 需要修复一批入口链接。

### 5.3 方案 C：把 `guides` 完整拆成多套子目录

优点：

- 分类最彻底。

缺点：

- 改动范围明显扩大。
- 需要修复的引用会快速膨胀。

### 5.4 选择结论

本轮选择 **方案 B**。

## 6. 迁移策略

本轮按最小闭环执行：

1. 新建 `docs/developer/`
2. 迁移 8 篇开发者文档
3. 修复这些文档之间的相对链接
4. 修复主入口文档中的引用

本轮需要重点修复的入口：

- `docs/README.md`
- `docs/current-status.md`
- `tests/README.md`

## 7. 文档边界约定

本轮之后，目录口径建议如下：

- `docs/developer/`
  - 面向开发者的开发指南、接入指南、扩展指南、开发流程
- `docs/guides/`
  - 本地环境、运行记录、运维操作、协作辅助说明
- `docs/architecture/`
  - 长期有效的架构、契约和规范

这里特别强调：

- `ai-metadata-authoring-guide.md` 虽然会指导开发者，但它属于架构规范，不迁入 `docs/developer/`

## 8. 成功标准

完成后应满足：

1. 8 篇开发者导向文档已位于 `docs/developer/`
2. 这些文档之间的相对链接可用
3. `docs/README.md`、`docs/current-status.md`、`tests/README.md` 已指向新位置
4. 本轮未扩大到运行/运维/历史文档全面重写

