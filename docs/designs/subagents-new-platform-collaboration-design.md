# 新平台 Subagents 协作配置设计

## 1. 背景

当前仓库已登记 `platform_explorer`、`contract_reviewer`、`docs_researcher`、`test_fixer` 四类 subagents，能够覆盖摸底、契约审查、资料核对和验证修复。受控实现角色 `module_worker` 已在协作口径中被使用，但本地 agent 配置尚未形成可提交真源，需要在本轮补齐。

后续路线不再以完整迁移旧平台为目标，而是吸收旧平台中有价值的内容和思想，按新平台目标逐步设计、开发和验证。因此 subagents 的默认分工也应从“迁移盘点”转向“新平台能力设计、设计一致性审查、契约治理和局部实现”。

## 2. 目标

- 新增 `design_reviewer`，在正式实施前审查详细设计或实施计划是否满足仓库约束。
- 新增 `capability_designer`，把需求、旧平台经验或产品诉求整理成新平台能力设计草案。
- 补齐 `module_worker` 本地配置，使“明确文件边界后的受控实现”也有稳定调度入口。
- 调整 `platform_explorer` 的描述，避免继续把旧平台迁移作为默认心智。
- 更新 subagents 指南，让后续 agent 能在小上下文内判断何时使用这些角色。

## 3. 非目标

- 不新增迁移盘点型 agent。
- 不调整主模型、线程数、沙箱和审批策略。
- 不改变现有 `contract_reviewer`、`docs_researcher`、`test_fixer` 的模型配置。
- 不让任何新增 agent 取代主线程的最终架构决策权。
- 不把当前工作区中已有的其它模型配置调整纳入本轮设计验收。

## 4. 范围

本轮允许修改：

- `.codex/agents/design-reviewer.toml`
- `.codex/agents/capability-designer.toml`
- `.codex/agents/module-worker.toml`
- `.codex/agents/platform-explorer.toml`
- `docs/guides/subagents-guide.md`
- `docs/prompts/13_通用subagents触发模板.md`
- `docs/architecture/ai-first-design-check-targets.txt`
- 本设计文档及对应一致性 review

本轮禁止修改：

- Host / Shell / Kernel / Module 运行时代码
- 构建、部署、CI 和密钥相关脚本
- 已有未提交的模型配置调整

## 5. 设计约束

- 主线程保留架构判断和最终决策权。
- 子 agent 默认承担并行只读探索、设计草案、契约审查、文档核对或验证修复。
- 正式实现仍应由主线程或被明确分配文件边界的 `module_worker` 执行。
- 新增角色不能鼓励“旧平台等比例重做”。
- 所有 agent 说明必须能帮助后续 agent 判断入口、边界、验证方式、失败信号和回滚方式。

## 6. 总体方案

采用“补充两个设计治理角色 + 收窄一个探索角色措辞”的方案。

`design_reviewer` 作为设计阶段守门角色，只读审查详细设计、实施计划、范围边界、受控入口和 AI-first 检查项。它不负责产出最终方案，也不直接改代码。

`capability_designer` 作为设计前置整理角色，把需求、产品诉求和可借鉴的旧平台思想抽象为新平台能力草案。它强调平台能力归属、阶段拆分、最小可验证切片和非目标，不做最终拍板。

`module_worker` 作为受控实现角色，只能在父线程已经明确文件集合、实施边界和验证方式后工作。它不是默认写入线程，不能主动扩大到 Host/Shell 内部结构，也不能与其它 live thread 同时修改同一批文件。

`platform_explorer` 继续保留只读摸底能力，但描述从“old platform hosts”调整为“current platform hosts”，避免把默认工作模式带回迁移盘点。

## 7. 关键模型

本轮没有新增运行时模型。新增的是 subagent 配置模型中的角色定义：

- `name`：稳定角色名。
- `description`：用于调度前快速判断适用范围。
- `model` 与 `model_reasoning_effort`：按角色复杂度选择。
- `sandbox_mode`：审查和设计型角色默认只读。
- `developer_instructions`：约束角色边界、输出重点和禁做事项。
- `nickname_candidates`：保留可读的候选昵称。

## 8. 核心流程

推荐流程：

1. 主线程明确当前任务目标、非目标和允许修改范围。
2. 如需求还未形成平台能力边界，先让 `capability_designer` 产出能力草案。
3. 如需要摸清现有实现入口和影响范围，使用 `platform_explorer` 只读扫描。
4. 详细设计或计划形成后，使用 `design_reviewer` 做一致性审查。
5. 涉及契约、Host/Shell、SDK 或模块边界时，使用 `contract_reviewer` 审查长期耦合风险。
6. 实施阶段由主线程或单个 `module_worker` 在明确文件边界内完成。
7. 验证失败时才让 `test_fixer` 做最小修复。

异常流程：

- 如果 `design_reviewer` 发现没有详细设计或 AI-first 五项缺失，应停止正式实现，回到设计补齐。
- 如果 `capability_designer` 的草案把产品专属流程放进 Host/Shell，应退回重分层。
- 如果 `module_worker` 没有收到明确文件集合或发现目标文件已被其它 live thread 修改，应停止实现并回报父线程。
- 如果多个 agent 都需要写同一批文件，应收敛为单一写入线程。

## 9. 接口与集成点

本轮集成点是 Codex 本地 agent 配置和项目协作文档：

- `.codex/agents/*.toml` 暴露给 Codex subagent 调度。
- `docs/guides/subagents-guide.md` 作为人和 agent 的协作入口。
- `docs/prompts/13_通用subagents触发模板.md` 作为可直接复制的任务提示模板。
- `docs/architecture/ai-first-design-check-targets.txt` 将本设计纳入 AI-first 文档检查。

## 10. 数据与状态

本轮不引入运行时数据状态。配置状态以仓库内可提交文件为 authoring 真源。

如果后续 Codex 运行时加载这些配置失败，应回到 `.codex/agents/*.toml` 检查 TOML 语法、角色名和说明边界。若失败只出现在本轮外模型配置调整，应先隔离为单独任务，不与本轮 subagents 协作配置混合处理。

## 11. 错误处理与风险

主要风险：

- 角色过多导致调度犹豫。
- `capability_designer` 被误用为架构拍板者。
- `design_reviewer` 与 `contract_reviewer` 职责重叠。
- `module_worker` 被误用为默认实现线程，导致多个 live thread 共享写入现场。

控制方式：

- 指南中明确推荐默认最多同时使用 2 到 3 个 subagents。
- 指南中明确主线程保留最终决策权。
- `design_reviewer` 聚焦“是否允许开工”，`contract_reviewer` 聚焦“契约和耦合风险”。
- `module_worker` 的配置和指南都要求父线程先给出明确文件边界，并报告验证与回滚说明。

## 12. 验证方案

本轮最小验证：

```powershell
powershell -ExecutionPolicy Bypass -File .\scripts\validate-ai-first-design.ps1 -Path .\docs\designs\subagents-new-platform-collaboration-design.md
```

受控目标集验证：

```powershell
powershell -ExecutionPolicy Bypass -File .\scripts\check-ai-first-design-targets.ps1
```

配置级验证：

```powershell
Get-ChildItem .\.codex\agents -Filter *.toml | ForEach-Object { Get-Content -Raw -LiteralPath $_.FullName | Out-Null }
```

## 13. 未决问题

当前没有需要阻塞本轮配置落地的未决问题。`metadata_kernel_reviewer` 暂不新增，等后续元数据 authoring/runtime 能力成为阶段主线时再单独设计。

## 14. 实施拆分建议

1. 新增本设计文档和一致性 review。
2. 新增两个设计治理 agent 配置文件，并补齐 `module_worker` 配置文件。
3. 调整 `platform_explorer` 描述。
4. 更新 subagents 指南。
5. 登记本设计到 AI-first 目标清单。
6. 执行设计校验和受控目标集校验。

## 15. AI-First 最小检查清单

- 入口在哪里：从 `.codex/agents/*.toml` 和 `docs/guides/subagents-guide.md` 开始实施。
- 边界在哪里：只允许修改本轮列出的 agent 配置、协作指南、subagents 提示模板和本轮设计/review 文档，不触碰运行时代码和部署脚本；其它未提交模型配置调整不属于本轮。
- 如何验证：运行 `validate-ai-first-design.ps1`、`check-ai-first-design-targets.ps1`，并读取所有 `.codex/agents/*.toml` 确认文件可访问。
- 失败信号是什么：设计校验报缺失项、目标集校验失败、TOML 文件不可读取、指南仍把迁移盘点作为默认方向。
- 如何回滚：删除新增的 `capability_designer`、`design_reviewer` 和 `module_worker` 配置，恢复 `platform_explorer`、`subagents-guide.md` 和提示模板的本轮改动，并从 `ai-first-design-check-targets.txt` 移除本设计路径。
