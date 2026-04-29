# 新平台 Subagents 协作配置一致性 Review

## 1. Review 目的

本 review 用于满足 `AGENTS.md` 中“详细设计文档或实施计划完成后，在进入正式实施前，必须先做一次一致性 review”的要求。

被 review 文档：

- `docs/designs/subagents-new-platform-collaboration-design.md`

## 2. Review 依据

- `AGENTS.md`
- `docs/architecture/ai-first-execution-standard.md`
- `docs/architecture/componentization-guardrails.md`
- `docs/guides/subagents-guide.md`
- 当前 `.codex/agents/*.toml` 配置

## 3. 结论

设计可以进入实施。

该设计把协作配置从迁移盘点口径调整为新平台设计开发口径，范围限定在 Codex agent 配置和协作文档，不触碰 Host、Shell、Kernel、模块运行时代码或基础设施脚本，符合当前仓库“单任务单目标”和“设计先行”的约束。

本 review 覆盖设计列明的 subagents 协作配置、`.codex` 基线模型配置和本地临时帧忽略规则变更。

## 4. 一致性检查

### 4.1 与项目定位一致

设计明确不新增迁移盘点型 agent，也不鼓励旧平台等比例重做。当前七类角色面向新平台能力抽象、设计审查、契约治理、资料核对、明确边界后的局部实现和验证修复，符合平台基础能力与扩展内核仓库定位。

### 4.2 与 AI-first 执行标准一致

设计回答了入口在哪里、边界在哪里、如何验证、失败信号是什么、如何回滚五项最小检查项。

设计还明确了 subagents 的并行边界：主线程保留最终决策权，真正写代码的线程保持单一，设计和审查型 agent 默认只读。

### 4.3 与组件化护栏一致

本轮不修改 Host / Shell 内部结构，不新增模块能力，不改变前端受控接入点。`capability_designer` 的说明中需要强调能力归属判断，避免把模块私有能力推入 Host/Shell。

### 4.4 与元数据边界一致

本轮不新增元数据执行能力。设计中已明确 `metadata_kernel_reviewer` 暂不新增，避免在没有阶段主线时过早扩大角色集合。

### 4.5 与变更规则一致

本轮只聚焦 subagents 协作配置及其本地执行基线，不混入运行时代码、CI、部署或具体模块开发。允许修改范围清晰，可验证、可回滚。

## 5. 实施前确认项

- 新增 agent 配置应使用只读沙箱，除非角色明确承担受控实现。
- `module_worker` 必须只在父线程给出明确文件集合、实施边界和验证方式后使用。
- `platform_explorer` 只能调整描述和指导语，不应扩大到架构建议角色。
- `docs/guides/subagents-guide.md` 应同步列出 `module_worker`，避免配置存在但指南缺失。
- `.codex/config.toml`、`contract-reviewer.toml` 和 `test-fixer.toml` 的模型基线调整不得改变角色职责边界。
- `.gitignore` 只记录本地临时帧忽略规则，不应掩盖需要提交的设计、配置或验证产物。

## 6. 风险与处理

风险：新增角色后调度复杂度上升。

处理：指南中继续保留“默认最多同时使用 2 到 3 个 subagents，通常不超过 4 个”的规则。

风险：`capability_designer` 被误用为最终架构决策者。

处理：agent 指令和指南中明确它只产出能力草案，最终判断由主线程负责。

风险：`module_worker` 被误用为默认写入线程，或在文件边界不清时进入实现。

处理：agent 指令和指南中明确它必须收到父线程分配的文件集合，并在回报中说明验证结果、风险和回滚说明。

风险：模型基线调整被误读为扩大 agent 职责。

处理：设计中明确模型调整只服务当前复杂度，不改变线程数、沙箱、审批策略或职责边界。

## 7. Review 结论

本设计与当前仓库约束一致，可以按设计进入配置和文档实施。
