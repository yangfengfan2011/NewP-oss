# 连续交付与审查门禁

本门禁管理多 child issue 的交付顺序、终局验收、审查收敛和关闭。GitHub 操作遵循 `docs/agents/issue-tracker.md`；被调用 skill 仍负责其单次流程。

## 核心术语

- **验收资产**：Acceptance Gate 使用的 runner、场景、Fixture、测试数据和环境/进程编排；新增场景类别、数据模型或编排属于实质性开发，既有场景内修正不属于。
- **资格运行 / 冻结候选**：高成本正式 Gate 前，用相同输入跑遍必需场景但不计入正式样本；通过 Gate-only 后记录 commit/tree，并在其干净输入上执行后续门禁。
- **封闭复审**：只核对原阻塞 finding、修复差异和完整 Spec 遗漏，不重新扫描原始全范围 Standards。

## 1. 定界与逐票交付

1. 查询 GitHub map、child、原生阻塞关系和实时 frontier；口头“完成”只触发当前代码、证据和关闭状态复核。
2. 第一票前记录 baseline、范围内 issues、设计/ADR、非目标和所需 Gate。将每项 Gate 行为映射到精确命令、runner 场景、成功信号和负责 child；缺口归给最早使其可运行的 child，只有跨票组合场景归终局 child。
3. 实质性验收资产开发与高成本正式运行可独立验收时拆成顺序 child；不可拆时以资格运行隔开。每次选票前重新查询 frontier，一次只实施一票；工作流要求时使用新上下文。
4. 每票建立可复现 RED，完成最小实现，运行定向测试、受影响集成验证和必需的 smoke/真实基础设施验证。Gate 行为首次可运行时，同票交付 runner/Fixture 并运行该场景或最窄 Gate slice。
5. 提交前检查范围、生成物 `--check`、行尾、秘密和工作区，只提交该票拥有的文件或 hunk。
6. 协调者默认只核对 blocker、验收条件、commit/tree、工作区、提交范围和可复跑证据与实施报告一致；证据一致时接受已通过的验证，不重复完整测试、整票源码审查或 `$code-review`。
7. 只有证据与源码、产物或验收条件矛盾；关键公共边界变化但证据未覆盖不变量；发生接管、范围/候选漂移或未声明的大范围迁移；需要移交 expected RED；或用户明确要求时，协调者才做独立定向复核。
8. 独立复核只覆盖触发风险的关键不变量和最小验证。稳定的范围内 P0/P1 回到当前 child 完成 RED→GREEN；已实施但未关闭的 child 仅核对现有交付，证据不足时补最小验证。
9. 核对通过并获得写权限后，回写命令、结果、commit/tree、已知限制和回滚方式，关闭 child，再查询 frontier。

完成条件：当前票无 blocker，验收和 Gate slice 有可复跑证据，范围提交与关闭核对通过，GitHub 状态一致且无该票遗留差异。

## 2. 终局 Gate 与冻结审查

1. 计划内实现和验收资产完成后，记录 baseline、候选 commit/tree、Gate-only 命令、输入和资源状态；工作区有既有修改时，从候选 commit 创建干净 worktree。
2. 高成本 Gate-only 先做资格运行，覆盖每个必需场景并验证观察终点、原子证据、稳定失败码和 ownership-safe cleanup。资产缺陷出现时，候选未冻结；RED→GREEN 后重跑完整资格运行。
3. 在干净 commit/tree 上运行 Gate-only。产品失败回到对应切片；实质性资产缺口按第 1 节重新归属；既有场景修正需有定向测试。随后重跑受影响验证、资格运行和 Gate-only。正式样本因资产缺陷失败时保留整次证据，修复后从零重跑，不拼接样本。
4. Gate-only 转绿后冻结 commit/tree，对同一输入运行一次 `scripts/verify.ps1`；需要真实基础设施时显式传入 `-AcceptanceGate`，以 verify 内的 Gate 重跑作为终局证据。
5. 在冻结候选的干净 worktree 中，以执行起始 baseline 为 fixed point、当前 HEAD 为候选，执行一次完整双轴 `$code-review`；Standards 与 Spec 使用同一输入，审查期间候选不变，两轴报告分别保留。

完成条件：资格运行（如需）、Gate-only 和完整 verify 均通过，双轴报告已返回，且全部对应同一冻结 commit/tree。

## 3. Finding 收敛

finding 只有同时满足以下条件才阻塞关闭：

1. 由执行范围引入，或使本次需求无法安全成立。
2. 有稳定 RED、可重复命令、静态检查或明确调用链，而非理论可能性。
3. 属于 P0（安全、秘密、数据、不可逆迁移或关键平台不可用）或 P1（确定违反 Issue、Spec、ADR 或仓库硬规则，并影响认证、授权、迁移、数据完整性、必需验收或核心行为）。
4. 可在已确认设计和当前边界内修复；否则进入设计升级。

其余 finding 作唯一分类：**后续**（受支持场景有具体影响且现有约束不足，获授权后创建非阻塞 issue）、**不立项**（只在违规、非支持或攻击假设下成立，或成本收益不相称）或 **证据不足**（无稳定复现或调用链）。

将阻塞清单作为一个批次逐项 RED→GREEN。全部转绿后形成干净 commit/tree，运行受影响验证和 Gate-only；转绿后冻结该候选，再运行完整 test/build、真实基础设施验证和最终 Gate。Standards 只封闭复审原阻塞 finding 和修复差异；Spec 从原 baseline 到新候选检查完整遗漏；两轴只报告范围内 P0/P1。修复差异产生新 P0/P1 时只继续复审该差异。

同一底层 seam 连续两次产生同类 finding，或修复需要改变公共 Contracts/SDK、Host/Shell 入口、跨模块边界、元数据协议、持久化/迁移内核、安全模型或任务范围时，停止逐点修补。现有设计覆盖则记录条款和最小边界；未覆盖则回退风险改动，或获授权后创建独立设计/实施 issue。当前候选引入的 P0/P1 在解决或回退前保持阻塞。

完成条件：每个 finding 有唯一分类和证据；阻塞项已关闭；Gate-only、终局验证和封闭复审绑定同一新候选；无未决范围扩张。

## 4. 长运行验证与接管

启动长运行验证或交给子代理前，提供执行包：工作目录、baseline、候选 commit/tree、文件范围、命令、阶段/单元时限、日志路径和成功信号；资源身份、所有权和 cleanup；以及包含 run identity、进度、阶段、最后进展时间、结果/日志和 owned process identity 的原子 checkpoint。

数据库连接默认由 Acceptance 按验收覆盖、开发覆盖、本地 Development 配置解析；只记录非默认来源名和一次性 catalog，不传递、输出或落盘秘密。

1. 同一工作区一次只运行一个 `scripts/verify.ps1` 或 Acceptance。
2. 以 `.codex-temp/verify/runs/<run>/result.json`、阶段日志和退出码为事实；MSBuild 异常同时读取 `msbuildDiagnosticPath`。证据区分缺失、不可读和合法空文件，只有契约要求非空时才以空文件判失败。
3. 超过声明时限或历史成功时长且 checkpoint 无进展时，核对归属进程、结果清单和日志尾部。
4. 已无归属进程或无法恢复子代理边界时，停止当前 turn，由新上下文接管现有差异和已完成阶段；末段 Gate 失败回到第 2、3 节的 Gate-only 收敛。

完成条件：命令、checkpoint 和日志可复查，run-owned 资源已 cleanup，接管不重复已完成阶段。

## 5. 关闭与交付

关闭最终 child 和父 issue 前，确认实时 GitHub 状态与阻塞关系正确；最终 commit/tree 与 test/build、真实基础设施和 Gate 证据一致；范围内 P0/P1 为零且后续项已立项或记录；工作区、归属进程、临时 catalog 和一次性资源处于预期终态。

按依赖顺序先回写并关闭最终 child，再汇总并关闭父 issue。最终报告列出候选 commit/tree、实际验证、后续 issue、未执行项及复跑方式。

完成条件：GitHub 状态、提交证据、验证证据和本地终态一致。
