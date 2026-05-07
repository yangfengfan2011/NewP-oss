# NewP Plan Runner Skill 验证记录

## 验证日期

2026-05-07

## 验证目标

验证 `newp-plan-runner` skill 在任务 4 范围内的可读取性、状态脚本关键路径、安全锁路径约束、样例文件清理状态、skill 文本护栏和 frontmatter 校验兜底结果。

本记录只覆盖任务 4 验证记录，不声明任务 5 的 AI-first 设计校验或目标清单检查已经通过。

## 验证命令

### 入口文件可读取

```powershell
Test-Path -LiteralPath '.codex\skills\newp-plan-runner\SKILL.md'
Test-Path -LiteralPath '.codex\skills\newp-plan-runner\agents\openai.yaml'
Get-Content -LiteralPath '.codex\skills\newp-plan-runner\SKILL.md' -TotalCount 20
Get-Content -LiteralPath '.codex\skills\newp-plan-runner\agents\openai.yaml' -TotalCount 20
```

### 状态脚本 smoke 验证

```powershell
powershell -ExecutionPolicy Bypass -File .\.codex\skills\newp-plan-runner\scripts\plan-run-state.ps1 init `
  -PlanPath docs/superpowers/plans/2026-05-02-standard-crud-backend-kernel.md `
  -LockPath .codex/automation-locks/sample-plan.lock `
  -TaskTitle "Task 1: Smoke" `
  -TaskIndex 1 `
  -TaskHeadingHash "sample-hash"

$state = Get-Content -Raw -LiteralPath '.codex-temp\plan-runs\2026-05-02-standard-crud-backend-kernel.json' | ConvertFrom-Json
$state.status
$state.targetTaskTitle
$state.verificationFailureCount
$state.reviewFailureCount

powershell -ExecutionPolicy Bypass -File .\.codex\skills\newp-plan-runner\scripts\plan-run-state.ps1 block `
  -PlanPath docs/superpowers/plans/2026-05-02-standard-crud-backend-kernel.md `
  -LockPath .codex/automation-locks/sample-plan.lock `
  -Reason "verification failed twice" `
  -NextAction "manual review" `
  -LastFailedCommand "dotnet test" `
  -IncrementVerificationFailure

$before = (Get-Item -LiteralPath '.codex\automation-locks\sample-plan.lock').LastWriteTime
Start-Sleep -Milliseconds 1200
powershell -ExecutionPolicy Bypass -File .\.codex\skills\newp-plan-runner\scripts\plan-run-state.ps1 update `
  -PlanPath docs/superpowers/plans/2026-05-02-standard-crud-backend-kernel.md `
  -LockPath .codex/automation-locks/sample-plan.lock
$after = (Get-Item -LiteralPath '.codex\automation-locks\sample-plan.lock').LastWriteTime
$after -gt $before

Remove-Item -LiteralPath '.codex\automation-locks\sample-plan.lock' -Force
Remove-Item -LiteralPath '.codex-temp\plan-runs\2026-05-02-standard-crud-backend-kernel.json' -Force
Test-Path -LiteralPath '.codex\automation-locks\sample-plan.lock'
Test-Path -LiteralPath '.codex-temp\plan-runs\2026-05-02-standard-crud-backend-kernel.json'
```

### 安全补充验证

该验证是任务 2 review finding 后补跑的额外安全验证，不是计划模板原始步骤。

```powershell
powershell -ExecutionPolicy Bypass -File .\.codex\skills\newp-plan-runner\scripts\plan-run-state.ps1 init `
  -PlanPath docs/superpowers/plans/2026-05-02-standard-crud-backend-kernel.md `
  -LockPath AGENTS.md `
  -TaskTitle "Task 1: Smoke" `
  -TaskIndex 1 `
  -TaskHeadingHash "sample-hash"
```

### Skill 文本护栏检查

```powershell
Select-String -LiteralPath '.codex\skills\newp-plan-runner\SKILL.md' -Pattern 'BLOCKED'
Select-String -LiteralPath '.codex\skills\newp-plan-runner\SKILL.md' -Pattern '只读|read-only|review'
Select-String -LiteralPath '.codex\skills\newp-plan-runner\SKILL.md' -Pattern '调度|frequency|频率'
```

### quick_validate 与 fallback frontmatter 检查

```powershell
$validator = Join-Path $env:USERPROFILE ".codex\skills\.system\skill-creator\scripts\quick_validate.py"
python --version
python -c "import sys; print(sys.executable); import yaml; print('yaml ok')"
python $validator .\.codex\skills\newp-plan-runner

$py = "C:\Users\yangf\.cache\codex-runtimes\codex-primary-runtime\dependencies\python\python.exe"
& $py -c "import sys; print(sys.executable); import yaml; print('yaml ok')"
& $py $validator .\.codex\skills\newp-plan-runner

$content = Get-Content -Raw .\.codex\skills\newp-plan-runner\SKILL.md
if ($content -notmatch "(?s)^---\s*`r?`nname:\s*newp-plan-runner\s*`r?`ndescription:\s*.+?`r?`n---") { throw "Invalid skill frontmatter." }
"PASS"
```

`quick_validate.py` 在系统 Python 和 Codex bundled Python 中均尝试运行；两者均因缺少 `yaml` 模块失败。随后使用计划中的 frontmatter regex fallback 检查 `SKILL.md`，结果为 `PASS` / no exception。

## 验证结果

- `SKILL.md` 可读取，路径为 `.codex/skills/newp-plan-runner/SKILL.md`。
- `openai.yaml` 可读取，路径为 `.codex/skills/newp-plan-runner/agents/openai.yaml`。
- `plan-run-state.ps1 init` 使用 `docs/superpowers/plans/2026-05-02-standard-crud-backend-kernel.md`、`.codex/automation-locks/sample-plan.lock` 和 `.codex-temp/plan-runs/2026-05-02-standard-crud-backend-kernel.json` 写入 `RUNNING` checkpoint；读取字段输出为 `RUNNING` / `Task 1: Smoke` / `0` / `0`。
- `plan-run-state.ps1 block` 写入 `BLOCKED`，并记录 `blockerReason` 对应的真实值 `verification failed twice`、`nextAction` 对应的真实值 `manual review`、`lastFailedCommand` 对应的真实值 `dotnet test`，`verificationFailureCount` 为 `1`。
- `plan-run-state.ps1 update` 刷新锁文件；通过比较 `$after -gt $before` 验证锁更新时间，输出 `True`。
- 样例 lock / checkpoint 已清理；删除 `.codex/automation-locks/sample-plan.lock` 和 `.codex-temp/plan-runs/2026-05-02-standard-crud-backend-kernel.json` 后，两次 `Test-Path` 均输出 `False`。
- 任务 2 review finding 后补跑的额外安全验证中，`-LockPath AGENTS.md` 被拒绝；错误包含 `Lock path is not allowed...Expected .codex/automation-locks/*.lock`，且未留下 checkpoint。
- `SKILL.md` 三类 `Select-String` 均命中：
  - `BLOCKED` 启动拦截和阻塞处理命中。
  - 只读 review 降级/只读 review prompt 约束命中。
  - 调度频率说明命中。
- 系统 Python 输出版本为 `Python 3.12.10`，解释器路径为 `C:\Users\yangf\AppData\Local\Programs\Python\Python312\python.exe`；`import yaml` 失败，错误为 `ModuleNotFoundError: No module named 'yaml'`，因此 `quick_validate.py` 未通过系统 Python 完整校验。
- Codex bundled Python 路径为 `C:\Users\yangf\.cache\codex-runtimes\codex-primary-runtime\dependencies\python\python.exe`；`import yaml` 失败，错误为 `ModuleNotFoundError: No module named 'yaml'`，因此 `quick_validate.py` 未通过 bundled Python 完整校验。
- fallback frontmatter regex 使用计划中的表达式检查 `SKILL.md`，输出 `PASS` / no exception。
- 不记录 validator 通过结论；本次只能确认 fallback regex 覆盖了必要 frontmatter 字段的最低限度检查。

## 未完成/待任务 5 验证项

- `scripts/validate-ai-first-design.ps1` 尚未在任务 4 阶段运行。
- `scripts/check-ai-first-design-targets.ps1` 尚未在任务 4 阶段运行。
- AI-first 设计校验和目标清单检查属于任务 5，本记录不声称两项检查已经通过。

## 已知风险

- `quick_validate.py` 依赖 `yaml` 模块；系统 Python 和 Codex bundled Python 当前均缺少该模块，因此 YAML/frontmatter 的完整解析校验没有执行成功。
- fallback frontmatter regex 只能确认必要字段形状存在，不能替代 `yaml` 模块提供的完整语法、类型和嵌套结构校验。
- 状态脚本 smoke 验证使用既有计划 `docs/superpowers/plans/2026-05-02-standard-crud-backend-kernel.md` 和 sample lock/checkpoint 路径，覆盖的是关键状态流和安全拒绝路径，不代表完整无人值守执行循环已经端到端跑完。

## 提交范围提醒

任务 4 提交时只应纳入 `docs/verification/newp-plan-runner-skill-verification.md`，不得混入 `docs/architecture/ai-first-design-check-targets.txt`、计划/设计文档、`.codex/automation-locks/*.lock` 或 `.codex-temp/plan-runs/*.json`。

## 回滚思路

如需回滚任务 4，只删除 `docs/verification/newp-plan-runner-skill-verification.md`。该文档是验证记录，不改变 skill、计划、设计、脚本或运行时代码。
