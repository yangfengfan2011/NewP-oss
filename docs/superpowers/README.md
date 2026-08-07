# Superpowers 文档入口（历史）

> 退场说明：本目录保留旧 Superpowers 工作流生成的设计规格与实施计划，不再作为新任务入口或产物目录。新任务遵守 `AGENTS.md`、当前 GitHub issue/spec 和按需触发的 Matt Pocock skill。

## 历史目录约定

- `specs/`：旧流程中的详细设计、设计规格和实施前 review 文档。
- `plans/`：旧流程中的实施计划、工作包拆解、可执行步骤和验证安排。

`specs/` 与 `plans/` 下的历史文档按同一组主题分类：

- `platform/`：跨模块、跨能力的平台级设计或计划。
- `modules/`：具体内建模块、试点模块或模块级能力。
- `tooling/`：脚本、校验、生成器、CI/CD 与工程工具链。
- `roadmap/`：阶段规划、路线图、阶段收口与治理节奏。

## 历史默认规则

- 当时的详细设计默认放在 `docs/superpowers/specs/<category>/`；无法稳定归类时短期放在 `docs/superpowers/specs/` 根下。
- `scripts/new-design-doc.ps1` 创建的历史设计文件默认带 `YYYY-MM-DD-<topic>-design.md` 日期前缀。
- 脚本支持 `-Category platform|modules|tooling|roadmap` 并创建对应子目录。
- 当时的实施计划默认放在 `docs/superpowers/plans/<category>/`；横跨多个主题时短期放在 `docs/superpowers/plans/` 根下。
- `docs/designs/` 与本目录均继续作为历史证据保留，不再迁移或接收新任务产物。
- 旧设计文档使用入口、边界、验证、失败信号和回滚这 5 项 AI-first 检查项。

## 历史创建与校验

```powershell
pwsh -NoProfile -ExecutionPolicy Bypass -File .\scripts\new-design-doc.ps1 -DesignName <your-design-name>
pwsh -NoProfile -ExecutionPolicy Bypass -File .\scripts\new-design-doc.ps1 -DesignName <your-design-name> -Category platform
pwsh -NoProfile -ExecutionPolicy Bypass -File .\scripts\validate-ai-first-design.ps1 -Path .\docs\superpowers\specs\<your-design>.md
pwsh -NoProfile -ExecutionPolicy Bypass -File .\scripts\check-ai-first-design-targets.ps1
```
