# Developer Docs Directory Reorg Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** 新建 `docs/developer/` 目录并把开发者导向文档迁入其中，同时修复最小闭环入口链接。

**Architecture:** 这次重组只影响“指导开发者如何开发”的文档，不改架构规范和运行/运维文档的归属。通过移动 8 篇文档、修复文档间相对链接，以及更新 `docs/README.md`、`docs/current-status.md`、`tests/README.md`，形成可用的新入口。

**Tech Stack:** Markdown、PowerShell、仓库文档结构整理

---

### Task 1: 确认迁移范围与入口影响面

**Files:**
- Read: `D:/Github/ICP-X/NewP/docs/README.md`
- Read: `D:/Github/ICP-X/NewP/docs/current-status.md`
- Read: `D:/Github/ICP-X/NewP/tests/README.md`
- Read: `D:/Github/ICP-X/NewP/docs/guides/componentized-development-quickstart.md`
- Read: `D:/Github/ICP-X/NewP/docs/guides/componentized-development.md`
- Read: `D:/Github/ICP-X/NewP/docs/guides/componentized-example-module.md`
- Read: `D:/Github/ICP-X/NewP/docs/guides/external-plugin-development.md`
- Read: `D:/Github/ICP-X/NewP/docs/guides/frontend-plugin-integration.md`
- Read: `D:/Github/ICP-X/NewP/docs/guides/persistence-public-api.md`
- Read: `D:/Github/ICP-X/NewP/docs/guides/persistence-usage-guide.md`
- Read: `D:/Github/ICP-X/NewP/docs/guides/persistence-development-workflow.md`

- [ ] **Step 1: 确认需要迁移的 8 篇文档**

Run:

```powershell
Get-ChildItem .\docs\guides -Filter *.md | Where-Object {
  $_.Name -in @(
    'componentized-development-quickstart.md',
    'componentized-development.md',
    'componentized-example-module.md',
    'external-plugin-development.md',
    'frontend-plugin-integration.md',
    'persistence-public-api.md',
    'persistence-usage-guide.md',
    'persistence-development-workflow.md'
  )
}
```

Expected:

- 能看到 8 篇待迁移文档。

- [ ] **Step 2: 识别入口引用位置**

Run:

```powershell
Get-ChildItem .\docs -Recurse -Filter *.md | Select-String -Pattern 'guides/componentized-development-quickstart.md|guides/componentized-development.md|guides/componentized-example-module.md|guides/external-plugin-development.md|guides/frontend-plugin-integration.md|guides/persistence-public-api.md|guides/persistence-usage-guide.md|guides/persistence-development-workflow.md'
```

Expected:

- 至少确认 `docs/README.md`、`docs/current-status.md` 和相关开发者入口引用位置。

### Task 2: 迁移文档并修复相对链接

**Files:**
- Move: `D:/Github/ICP-X/NewP/docs/guides/componentized-development-quickstart.md` -> `D:/Github/ICP-X/NewP/docs/developer/componentized-development-quickstart.md`
- Move: `D:/Github/ICP-X/NewP/docs/guides/componentized-development.md` -> `D:/Github/ICP-X/NewP/docs/developer/componentized-development.md`
- Move: `D:/Github/ICP-X/NewP/docs/guides/componentized-example-module.md` -> `D:/Github/ICP-X/NewP/docs/developer/componentized-example-module.md`
- Move: `D:/Github/ICP-X/NewP/docs/guides/external-plugin-development.md` -> `D:/Github/ICP-X/NewP/docs/developer/external-plugin-development.md`
- Move: `D:/Github/ICP-X/NewP/docs/guides/frontend-plugin-integration.md` -> `D:/Github/ICP-X/NewP/docs/developer/frontend-plugin-integration.md`
- Move: `D:/Github/ICP-X/NewP/docs/guides/persistence-public-api.md` -> `D:/Github/ICP-X/NewP/docs/developer/persistence-public-api.md`
- Move: `D:/Github/ICP-X/NewP/docs/guides/persistence-usage-guide.md` -> `D:/Github/ICP-X/NewP/docs/developer/persistence-usage-guide.md`
- Move: `D:/Github/ICP-X/NewP/docs/guides/persistence-development-workflow.md` -> `D:/Github/ICP-X/NewP/docs/developer/persistence-development-workflow.md`

- [ ] **Step 1: 创建新目录并迁移文档**

Expected:

- `docs/developer/` 已创建。
- 8 篇文档已迁入新目录。

- [ ] **Step 2: 修复文档间相对链接**

需要至少检查并修复：

- `persistence-public-api.md` 中指向 `guides/` 与 `tests/README.md` 的路径
- `persistence-usage-guide.md` 中指向 `guides/` 与 `tests/README.md` 的路径
- `persistence-development-workflow.md` 中指向 `guides/` 与 `tests/README.md` 的路径

Expected:

- 同目录开发者文档仍使用同级相对路径。
- 指向 `docs/guides/` 的链接改为 `../guides/...`

- [ ] **Step 3: 验证旧目录中不再保留这 8 篇文件**

Run:

```powershell
Get-ChildItem .\docs\guides -Filter *.md | Where-Object {
  $_.Name -in @(
    'componentized-development-quickstart.md',
    'componentized-development.md',
    'componentized-example-module.md',
    'external-plugin-development.md',
    'frontend-plugin-integration.md',
    'persistence-public-api.md',
    'persistence-usage-guide.md',
    'persistence-development-workflow.md'
  )
}
```

Expected:

- 无输出。

### Task 3: 修复入口链接并验证可发现性

**Files:**
- Modify: `D:/Github/ICP-X/NewP/docs/README.md`
- Modify: `D:/Github/ICP-X/NewP/docs/current-status.md`
- Modify: `D:/Github/ICP-X/NewP/tests/README.md`

- [ ] **Step 1: 更新 `docs/README.md`**

需要：

- 新增 `developer` 目录说明
- 把当前推荐入口中的开发者文档改为 `developer/` 路径
- 让 `guides` 说明回到运行、环境与协作辅助说明

- [ ] **Step 2: 更新 `docs/current-status.md`**

需要把当前开发者手册入口改成：

- `docs/developer/persistence-public-api.md`
- `docs/developer/persistence-usage-guide.md`
- `docs/developer/persistence-development-workflow.md`

并把涉及 `external-plugin-development.md` 的主入口引用改到 `docs/developer/`

- [ ] **Step 3: 更新 `tests/README.md`**

需要把持久化使用指南引用改到：

- `docs/developer/persistence-usage-guide.md`

- [ ] **Step 4: 验证入口文档中的新路径**

Run:

```powershell
Get-ChildItem .\docs -Recurse -Filter *.md | Select-String -Pattern 'developer/componentized-development-quickstart.md|developer/componentized-development.md|developer/componentized-example-module.md|developer/external-plugin-development.md|developer/frontend-plugin-integration.md|developer/persistence-public-api.md|developer/persistence-usage-guide.md|developer/persistence-development-workflow.md'
```

Expected:

- `docs/README.md`、`docs/current-status.md`、`tests/README.md` 至少已出现对应新路径。

- [ ] **Step 5: 完整回读关键入口**

Run:

```powershell
Get-Content .\docs\README.md -Encoding UTF8
Get-Content .\docs\current-status.md -Encoding UTF8 | Select-Object -Skip 270 -First 60
Get-Content .\tests\README.md -Encoding UTF8
```

Expected:

- 开发者目录说明存在。
- 当前主入口都已指向新位置。

