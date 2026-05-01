# Superpowers 文档入口

本目录作为后续新增设计规格与实施计划的默认入口。

## 目录约定

- `specs/`：新增详细设计、设计规格、正式实施前需要 review 的设计文档。
- `plans/`：实施计划、工作包拆解、可执行步骤与验证安排。

`specs/` 与 `plans/` 下后续新增文档优先按同一组主题分类：

- `platform/`：跨模块、跨能力的平台级设计或计划。
- `modules/`：具体内建模块、试点模块或模块级能力。
- `tooling/`：脚本、校验、生成器、CI/CD 与工程工具链。
- `roadmap/`：阶段规划、路线图、阶段收口与治理节奏。

## 默认规则

- 后续新增详细设计默认放在 `docs/superpowers/specs/<category>/`；如果暂时无法稳定归类，才允许短期放在 `docs/superpowers/specs/` 根下。
- 使用 `scripts/new-design-doc.ps1` 创建设计时，文件名默认自动带当天日期前缀：`YYYY-MM-DD-<topic>-design.md`；如果传入名称已经带 `YYYY-MM-DD-` 前缀，则不会重复添加。
- 脚本支持 `-Category platform|modules|tooling|roadmap`，会自动创建对应子目录。
- 后续新增实施计划默认放在 `docs/superpowers/plans/<category>/`；如果计划横跨多个主题且尚未拆分，才允许短期放在 `docs/superpowers/plans/` 根下。
- `docs/designs/` 下既有历史设计不要求一次性迁移；后续修改时可按工作包需要逐步迁入 `specs/`。
- 设计文档仍需回答入口、边界、验证、失败信号和回滚这 5 项 AI-first 最小检查项。

## 创建与校验

```powershell
powershell -ExecutionPolicy Bypass -File .\scripts\new-design-doc.ps1 -DesignName <your-design-name>
powershell -ExecutionPolicy Bypass -File .\scripts\new-design-doc.ps1 -DesignName <your-design-name> -Category platform
powershell -ExecutionPolicy Bypass -File .\scripts\validate-ai-first-design.ps1 -Path .\docs\superpowers\specs\<your-design>.md
powershell -ExecutionPolicy Bypass -File .\scripts\check-ai-first-design-targets.ps1
```
