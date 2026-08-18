# 连续交付与审查门禁

本门禁管理多 child issue 的交付顺序、终局验收、审查收敛和关闭。GitHub 操作遵循 `docs/agents/issue-tracker.md`；被调用 skill 仍负责其单次流程。

## 核心术语

- **验收资产**：Acceptance Gate 使用的 runner、场景、Fixture、测试数据和环境/进程编排；新增场景类别、数据模型或编排属于实质性开发，既有场景内修正不属于。
- **开发矩阵**：当前 child 在 Development 环境完成的定向测试、正式 API、必要浏览器 smoke、受影响构建，以及该 child 拥有的 Acceptance runner 最窄场景。
- **资格运行 / 冻结候选**：高成本正式 Gate 前，用相同输入跑遍必需场景但不计入正式样本；资格运行只裁决已形成的候选，不承担 runner 或产品调试。Gate-only 通过后冻结 commit/tree，并在其干净输入上执行后续门禁。
- **终局尝试预算**：一个父票交付默认最多执行两次进入被测场景的完整资格运行，即首次运行和一次收敛重跑；重置预算需要任务发起者明确决策。
- **封闭复审**：只核对原阻塞 finding、修复差异和完整 Spec 遗漏，不重新扫描原始全范围 Standards。
- **浏览器证据层级**：开发 smoke、诊断和人工接管在执行包中选择一个受支持的浏览器 harness，并记录 owner 与续接方式；Qualification、Gate 和 verify 的正式浏览器证据由仓库 Acceptance runner 产生。两层分别记录证据，不拼接为同一正式样本。

## 1. 定界与逐票交付

1. 查询 GitHub map、child、原生阻塞关系和实时 frontier；口头“完成”只触发当前代码、证据和关闭状态复核。
2. 第一票前记录 baseline、范围内 issues、设计/ADR、非目标和所需 Gate，并按行为区分平台已有能力、模块 adapter 和新公共 seam。任何新增公共契约、SDK seam、Host/Shell 入口或模块边界变化，都先核对已确认设计；设计未覆盖时暂停实现并确认范围。
3. 父票与 child 的验收责任矩阵必须把每项行为映射到最小充分证明层级（unit/component、API/integration、browser-smoke、browser-gate、database/log 或 Gate）、命令或 journey、成功信号、执行 owner 和首次可运行的 child；缺口归给最早使其可运行的 child，只有跨票组合场景归终局 child。
4. 每个 child 使用一个新执行上下文，同一 child 的开发、诊断和复测持续复用该上下文；上下文不可恢复时才在同一 child 内移交。浏览器旅程记录 harness、owner、页面或 session 来源和续接方式；控制不可用时保留受管服务、页面与已通过阶段，由持有者、接管者或用户从现有 checkpoint 继续。
5. 开发矩阵固定复用当前 worktree 已配置的 Development 数据库和一个受管环境，当前 child 使用唯一数据前缀并清理自身数据；不得仅为重跑 RED、正式 API、浏览器 smoke 或最窄 Gate slice 新建 catalog。只有测试对象本身是空库启动、迁移或回滚、建库或删库、破坏性 DDL、并发或故障隔离时，才使用一次性 catalog；正式 Qualification、Gate 和 verify 仍按第 4 节使用隔离 catalog。
6. 每票建立可复现 RED，完成最小实现，运行定向测试、正式 API 和必要浏览器 smoke；完整 Gate 保留给终局裁决。Gate 行为首次可运行时，同票交付 runner/Fixture 并运行该场景或最窄 Gate slice；原子切换 child 关闭前运行生产入口和受影响核心旅程 slice，完整资格运行仍归终局阶段。
7. 修复前先分类失败并回到相应 owner：产品 RED 回到行为 owner；runner、locator、等待或 Fixture 缺陷回到验收资产 owner；浏览器控制失败保留现场并 handoff；环境失败清理归属资源后在同一上下文至多重试一次；GitHub 等外部控制面故障保留本地现场并在同一上下文至多重试一次；遥测只有在证据证明与被测行为无关且不影响结果时才作为噪声记录。
8. 开发矩阵转绿前不做过程性微提交；转绿后运行受影响集成验证与构建，检查生成物 `--check`、范围、行尾、秘密和工作区，默认形成一个单一意图的范围提交。确有独立回滚边界时才拆分，并在 child 中说明。
9. 协调者默认只核对 blocker、验收条件、commit/tree、工作区、范围和可复跑证据与实施报告一致。只有证据矛盾、关键边界缺少不变量覆盖、发生接管或范围漂移、需要移交 expected RED，或用户明确要求时，才独立复核触发风险的最小范围；稳定的范围内 P0/P1 回到 owning child 完成 RED→GREEN。
10. 核对通过并获得写权限后，回写证明层级、命令或 journey、结果、commit/tree、限制、回滚和清理状态，关闭 child，再查询 frontier。核心旅程若尚未由既有 runner 实际执行，不得以“留给终局 child”关闭。

完成条件：当前票无 blocker，能力与验收责任矩阵完整，适用的浏览器 handoff 明确，开发矩阵和对应 Acceptance runner/Gate slice 有可复跑证据，范围提交与关闭核对通过，GitHub 状态一致且无该票遗留差异。

## 2. 终局 Gate 与冻结审查

1. 进入终局前审计父票每项核心旅程，确认负责 child、已存在的 runner/journey、实际运行证据和成功信号齐全；任一缺口都先回到行为或验收资产 owner。责任 child 已关闭时重新打开；缺口属于终局 child 自有的集成资产时保留同一 child 并回到 Development；只有形成独立新范围且获得授权时才新增顺序 child。
2. 入口审计通过后记录 baseline、候选 commit/tree、资格运行、Gate-only、verify 命令、证据路径、输入和资源状态。候选必须来自开发矩阵转绿后的范围提交；工作区有既有修改时，从候选 commit 创建干净 worktree。
3. 在候选上执行第一次完整资格运行。运行只读取和裁决候选，覆盖必需场景、观察终点、原子证据、稳定失败码和 ownership-safe cleanup；正式浏览器证据由仓库 Acceptance runner 产生，开发 smoke 只作为独立补充证据。
4. 第一次资格运行失败时，完整保留失败样本并立即退出终局阶段，候选保持未冻结；按第 1 节确认行为或验收资产 owner，再回到 Development。
5. 在 Development 中用定向复现一次性收敛全部已知问题；runner 可观测性不足时，一次性补齐阶段、请求、响应、页面和 cleanup checkpoint。开发矩阵重新转绿后形成一个新的范围候选提交，才允许第二次完整资格运行。
6. 第二次完整资格运行仍暴露新的稳定 runner 缺陷或产品 RED 时，停止并报告候选、两次运行、失败分类、归属和可选方案；获得任务发起者明确决策后才重置预算。资格运行、Gate-only 或 verify 的修复需要再次进入资格运行且预算已用尽时，同样停止报告。
7. 资格运行通过后，在干净 commit/tree 上运行 Gate-only；通过后冻结 commit/tree，并对同一输入运行一次 `scripts/verify.ps1`。需要真实基础设施时显式传入 `-AcceptanceGate`，以 verify 内的 Gate 重跑作为终局证据。
8. 在冻结候选的干净 worktree 中，以执行起始 baseline 为 fixed point、当前 HEAD 为候选，执行一次完整双轴 `$code-review`；Standards 与 Spec 使用同一输入，审查期间候选不变，两轴报告分别保留。

完成条件：资格运行（如需）、Gate-only 和完整 verify 均通过，双轴报告已返回，且全部对应同一冻结 commit/tree。

### 性能预算超限处置

任何性能预算或性能 Gate 超限时，先保留失败证据并停止会改变裁决口径、依赖图或产品架构的处置。报告必须列出候选 commit/tree、测量对象、实际值、预算值、差值、复现命令、样本与环境、稳定性、证据位置、已确认原因或调用链，以及可选方案的收益、风险、影响范围和回滚方式；尚未确认的原因必须明确标为假设。

在任务发起者或预算负责人明确选择方案前，可以继续执行无修改的复现、测量和原因分析，但不得降低或取消预算、把 `enforced` 改为 `report-only`、提高警告阈值、改变统计口径或 baseline，也不得自行调整分块、依赖、组件、懒加载边界或缓存条件来绕过失败。获得决策后，只修改对应的唯一事实源；预算与执行规则进入配置、runner 和测试，长期架构例外进入 ADR，并按受影响范围重跑 Gate。

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

启动长运行验证或交给子代理前，提供执行包：工作目录、baseline、候选 commit/tree、文件范围、命令、终局尝试序号与剩余预算、阶段/单元时限、日志路径和成功信号；资源身份、所有权和 cleanup；以及包含 run identity、进度、阶段、最后进展时间、结果/日志和 owned process identity 的原子 checkpoint。

正式 Acceptance 按验收覆盖和开发覆盖解析隔离 catalog。秘密通过已声明的进程环境或被 Git 忽略的本地来源注入，不进入命令参数、工具消息、截图/页面快照、日志、Issue 或提交；证据只记录非默认来源名、数据前缀和一次性 catalog。

1. 同一工作区一次只运行一个 `scripts/verify.ps1` 或 Acceptance。
2. 以 `.codex-temp/verify/runs/<run>/result.json`、阶段日志和退出码为事实；MSBuild 异常同时读取 `msbuildDiagnosticPath`。证据区分缺失、不可读和合法空文件，只有契约要求非空时才以空文件判失败。
3. 超过声明时限或历史成功时长且 checkpoint 无进展时，核对归属进程、结果清单和日志尾部。
4. 已无归属进程时按失败分类处置；子代理边界不可恢复时，保留现有差异与 checkpoint，在同一 child 内 handoff，接管者不重复已完成阶段。末段 Gate 失败回到第 2、3 节收敛。

完成条件：命令、checkpoint 和日志可复查，run-owned 资源已 cleanup，接管不重复已完成阶段。

## 5. 关闭与交付

关闭最终 child 和父 issue 前，确认实时 GitHub 状态与阻塞关系正确；最终 commit/tree 与 test/build、真实基础设施和 Gate 证据一致；范围内 P0/P1 为零且后续项已立项或记录；工作区、归属进程、临时 catalog 和一次性资源处于预期终态。

按依赖顺序先回写并关闭最终 child，再汇总并关闭父 issue。最终报告列出候选 commit/tree、实际验证、后续 issue、未执行项及复跑方式。

完成条件：GitHub 状态、提交证据、验证证据和本地终态一致。
