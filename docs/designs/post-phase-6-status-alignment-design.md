# Post-Phase 6 状态与计划对齐设计

## 1. 背景

仓库已完成 Phase 0 到 Phase 6（含 6.5、6.6、6.7）的主要交付，并在 `docs/designs/post-phase-6-governance-and-ai-first-roadmap-design.md` 中确定了后续路线：以平台治理和 AI-first 收口为优先，拆分为 WP1–WP4 四个工作包。

WP1（插件治理增强设计）和 WP2（AI-first 执行标准设计）已产出对应设计文档。但 `Plan.md` 和 `docs/current-status.md` 尚未与后续路线对齐，两份文档的"推荐下一步"仍停留在 Phase 6.6 阶段性口径上。

此外，部分核心能力虽然已有代码和测试基础，但 design → implementation/guide → verification → current-status 之间的文档链路存在缺口，需要在收口时明确记录。

本设计的目的是完成上述对齐，让状态、计划和文档链路形成统一口径。

## 2. 目标

- 将 `docs/current-status.md` 的"当前推荐下一步"与后续路线设计的 WP1–WP4 优先级对齐。
- 将 `Plan.md` 补入与四个工作包一致的后续路线说明。
- 对三项核心能力（插件平台、多数据库持久化、插件运维中心）完成 design / implementation or guide / verification / current-status 四类链路核对，记录已有和缺口。

## 3. 非目标

- 不重写 `Plan.md` 或 `docs/current-status.md` 的既有内容。
- 不为了制造一致性而删减已有阶段成果。
- 不补写缺口文档本身（缺口只需记录，由后续工作包或专项任务补齐）。
- 不修改 `AGENTS.md`、`docs/architecture/*`、任何代码或测试。

## 4. 范围

- 修改：`docs/current-status.md`
- 修改：`Plan.md`
- 新增：本设计文档 `docs/designs/post-phase-6-status-alignment-design.md`

## 5. 对齐原则

### 5.1 保留事实优先

已有阶段成果（Phase 0–6、6.5、6.6、6.7）的描述以仓库内已有文档和验证记录为准，不缩减、不拔高。

### 5.2 推荐路线以路线设计文档为准

当 `Plan.md` 或 `docs/current-status.md` 的"推荐下一步"与 `post-phase-6-governance-and-ai-first-roadmap-design.md` 的工作包模型不一致时，以路线设计文档为准进行调整。

### 5.3 未完成不写成已完成

WP1、WP2 已有设计文档，但尚未进入代码实现。WP3、WP4 处于设计或更早阶段。状态文档只记录设计产出，不升级为"已实现"或"已验证"。

### 5.4 缺口记录而非假设补齐

文档链路存在缺口时，在本设计文档中明确列出缺口，不靠假设或口头说明来补齐。

## 6. `docs/current-status.md` 收口方案

### 6.1 "当前推荐下一步"调整

将原有的"当前推荐下一步"部分替换为与路线设计一致的四个工作包优先级：

1. 平台治理收口（WP1）
2. AI-first 规范落地（WP2）
3. 文档与状态一致性治理（WP3）
4. 最小迁移连续性（WP4）

### 6.2 WP1、WP2 设计产出记录

在"当前推荐下一步"中注明 WP1 和 WP2 的设计文档已产出，但实现尚未启动。

### 6.3 不改动已有阶段描述

Phase 0 到 Phase 6.7 的已有描述保持不变，不删减事实。

## 7. `Plan.md` 收口方案

### 7.1 补入后续路线说明

在 `Plan.md` 的"Phase 6 之后建议拆分"部分，将原有的阶段列表前插入一段"Post-Phase 6 治理与 AI-First 收口"说明，包含 WP1–WP4 的简要目标。

### 7.2 口径对齐

工作包名称和目标描述与 `post-phase-6-governance-and-ai-first-roadmap-design.md` 保持一致。

### 7.3 不替代既有迁移计划

Phase 7A / 7B / 7C 的既有说明保留不变，WP4 作为桥接入口而非替代。

## 8. 文档链路核对

### 8.1 插件平台（Phase 6.6）

| 链路类型 | 状态 | 文档路径 |
|----------|------|----------|
| Design | 有 | `docs/designs/plugin-governance-enhancement-design.md`、`docs/superpowers/specs/2026-04-06-runtime-layer-plugin-platform-design.md`、`docs/superpowers/specs/2026-04-06-unified-plugin-package-design.md` |
| Architecture / Guide | 有 | `docs/architecture/plugin-package-format.md`、`docs/architecture/plugin-runtime-loading.md`、`docs/architecture/plugin-compatibility-policy.md`、`docs/guides/plugin-packaging.md`、`docs/guides/unified-plugin-package-operations.md`、`docs/developer/external-plugin-development.md`、`docs/guides/external-plugin-debugging.md` |
| Verification | 缺 | 无独立的 `docs/verification/phase-6-6-*.md`。测试存在于 `tests/Kernel.Tests`，但无正式验证矩阵。 |
| Current Status | 有 | `docs/current-status.md` Phase 6.6 段落 |

链路缺口：缺少独立验证文档。

### 8.2 多数据库持久化（Phase 6.5）

| 链路类型 | 状态 | 文档路径 |
|----------|------|----------|
| Design | 有 | `docs/superpowers/specs/2026-04-09-phase-6-5-multi-db-persistence-design.md` |
| Developer Guide | 有 | `docs/developer/persistence-public-api.md`、`docs/developer/persistence-usage-guide.md`、`docs/developer/persistence-development-workflow.md`、`docs/guides/local-multi-db-container-setup.md` |
| Verification | 缺 | 无独立的 `docs/verification/phase-6-5-*.md`。三库 smoke 脚本存在于 `tests/integration/persistence/`，但无正式验证文档。 |
| Current Status | 有 | `docs/current-status.md` Phase 6.5 段落 |

链路缺口：缺少独立验证文档。

### 8.3 插件运维中心（Phase 6.7）

| 链路类型 | 状态 | 文档路径 |
|----------|------|----------|
| Design | 有 | `docs/superpowers/specs/2026-04-08-plugin-operations-center-v1-design.md` |
| Implementation / Guide | 弱 | `docs/guides/unified-plugin-package-operations.md` 有部分提及，但无独立的 `docs/modules/plugin-ops-implementation.md` 或专属用户指南。 |
| Verification | 缺 | 无独立的 `docs/verification/phase-6-7-*.md`。 |
| Current Status | 有 | `docs/current-status.md` Phase 6.7 段落 |

链路缺口：缺少独立实现说明文档和独立验证文档。

### 8.4 缺口汇总

| 能力 | 缺口 |
|------|------|
| 插件平台 Phase 6.6 | 缺独立验证文档 |
| 多数据库持久化 Phase 6.5 | 缺独立验证文档 |
| 插件运维中心 Phase 6.7 | 缺独立实现说明文档、缺独立验证文档 |

这些缺口应由后续专项任务补齐，不在本工作包中直接创建。

## 9. 风险

- `Plan.md` 和 `docs/current-status.md` 的修改如果口径不统一，会制造新的不一致。控制方式：两处修改在同一工作包内完成，使用相同术语。
- 已有阶段描述在对齐过程中被误改或误删。控制方式：只追加或调整"推荐下一步"，不修改已有阶段正文。
- 缺口记录被误读为"阶段未完成"。控制方式：缺口在链路核对表中明确标记为"文档缺口"而非"功能缺口"。

## 10. 回滚思路

- `Plan.md` 和 `docs/current-status.md` 的修改可通过 git 回退到修改前版本。
- 本设计文档是新增文件，删除即可回退。
- 不涉及代码或数据变更，无需额外数据回滚。
