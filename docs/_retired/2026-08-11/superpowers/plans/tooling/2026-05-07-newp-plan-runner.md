# NewP 计划自动执行 Skill 实施计划

> **供后续 agent 执行时使用：** 按本计划逐任务执行；由于本计划明确禁止创建 worktree，默认使用 `superpowers:executing-plans` 或本计划产出的 `newp-plan-runner` 流程执行。步骤使用 checkbox（`- [ ]`）跟踪。

**目标：** 新增 `newp-plan-runner` 项目级 skill，让 Codex 在已有计划的情况下按“单任务 + 锁 + 检查点 + 阻塞终止 + 只读 review”方式无人值守执行。

**架构：** 第一版采用主 agent 串行执行，状态脚本只管理 `.codex-temp/plan-runs/*.json` 检查点，不修改计划 checkbox。默认 automation 调度频率为每 20 分钟执行一次；完整 subagent-driven implementation 不适用于本计划，执行时只允许在提交前使用只读 subagents 做 spec compliance review 和 code quality review。

**技术栈：** Codex Agent Skill、PowerShell、JSON checkpoint、Markdown 计划与验证记录。

---

## 0. 文件结构

- 新建： `.codex/skills/newp-plan-runner/SKILL.md`
  - 定义 skill 触发、启动流程、锁策略、检查点恢复、阻塞终止、单任务执行、只读 review 和完成输出。
- 新建： `.codex/skills/newp-plan-runner/agents/openai.yaml`
  - 提供 Codex app UI 元数据，默认允许隐式触发。
- 新建： `.codex/skills/newp-plan-runner/scripts/plan-run-state.ps1`
  - 管理 `.codex-temp/plan-runs/<plan-id>.json` 检查点，并在更新状态时刷新当前锁文件更新时间。
- 新建： `docs/verification/newp-plan-runner-skill-verification.md`
  - 记录 skill 与状态脚本的验证命令、结果、风险和回滚方式。
- 修改： `docs/superpowers/specs/tooling/2026-05-05-newp-plan-runner-design.md`
  - 仅当实现中发现设计与落点不一致时做最小回填。

## 任务 1: 创建 skill 骨架与执行规则

**文件：**
- 新建： `.codex/skills/newp-plan-runner/SKILL.md`
- 新建： `.codex/skills/newp-plan-runner/agents/openai.yaml`

- [x] **步骤 1: 创建 skill 目录**

执行：

```powershell
New-Item -ItemType Directory -Force -Path .\.codex\skills\newp-plan-runner\agents | Out-Null
New-Item -ItemType Directory -Force -Path .\.codex\skills\newp-plan-runner\scripts | Out-Null
```

预期：`.codex/skills/newp-plan-runner/` 下目录已存在。

- [x] **步骤 2: 新增 `SKILL.md`**

创建 `.codex/skills/newp-plan-runner/SKILL.md`，内容如下：

```markdown
---
name: newp-plan-runner
description: Use when executing an existing NewP implementation plan in docs/superpowers/plans, especially for unattended automation that must run only the first unfinished Task, use a plan lock, persist checkpoints, stop on BLOCKED state, protect dirty worktrees, run task-local verification, use read-only two-stage subagent review before commit, and avoid branch switching or worktree creation.
---

# NewP Plan Runner

## 目的

每轮只执行一个既有 NewP 实施计划 Task。该 skill 面向长时间无人值守 automation，重点处理会话上下文被压缩或丢失后的恢复问题。

核心规则：计划文档是长期真源；`.codex-temp/plan-runs/*.json` 只是恢复检查点。

默认 automation 调度频率为每 20 分钟执行一次。低于 10 分钟的频率默认不建议使用；短任务或高优先级计划可以显式配置为每 10 分钟触发一次。如果用户或外部调度器显式覆盖频率，仍必须保留锁、检查点和 `BLOCKED` 启动拦截规则。

## 必要输入

- 计划路径，通常是 `docs/superpowers/plans/<name>.md`。
- 可选锁路径；未提供时，从计划文件名推导 `.codex/automation-locks/<plan-id>.lock`。

## 强边界

- 不创建 git worktree。
- 不切换分支。
- 无人值守执行时不使用交互式 MCP 工具、浏览器、Figma、GitHub connector 或 node_repl。
- 不自动删除 stale lock。
- 不执行超过第一个未完成任务 的范围。
- 检查点状态为 `BLOCKED` 时不得继续。
- 成功提交前不得跳过只读两阶段 subagent review。
- `git add` 不得包含锁文件或 `.codex-temp/plan-runs/*.json`。

## 启动检查

1. 读取 `AGENTS.md`。
2. 读取 `docs/superpowers/README.md`。
3. 读取目标计划文件。
4. 读取计划引用的相关设计文档。
5. 确认当前 automation 的调度频率；默认应为每 20 分钟执行一次。
6. 从计划文件名解析 `planId`。
7. 解析状态路径 `.codex-temp/plan-runs/<planId>.json`。
8. 解析锁路径 `.codex/automation-locks/<planId>.lock`；如果用户提供了具体锁路径，则使用用户提供的路径。

## 启动门禁

1. 如果状态文件存在且 `status` 为 `BLOCKED`，立即停止：
   - 报告 `计划存在阻塞检查点，需要人工处理或暂停 automation`。
   - 不扫描或执行未完成任务。
2. 如果锁文件存在且更新时间距离现在小于 1 小时，立即停止：
   - 报告 `已有任务运行中`。
3. 如果锁文件存在且更新时间距离现在大于等于 1 小时，立即停止：
   - 报告 `疑似 stale lock，需要人工确认`。
   - 不删除锁文件。
4. 运行 `git status --short`。
5. 记录已有未提交改动，并在初始化检查点后写入相关线索。
6. 识别计划中的第一个未完成任务。
7. 如果没有未完成任务：
   - 写入 `PLAN_COMPLETED` 检查点。
   - 不修改代码。
   - 如果可以暂停 automation，则暂停；否则报告 `计划已完成，请人工暂停 automation`。

## 锁与检查点

编辑文件前必须先创建锁。锁内容至少包含：

- 当前时间。
- 计划路径。
- 目标任务 标题。
- 当前 run id 或进程 id，如果可用。

初始化检查点时写入：

- `status: RUNNING`
- `planPath`
- `lockPath`
- `targetTaskTitle`
- `targetTaskIndex`
- `targetTaskHeadingHash`
- `startedAt`
- `updatedAt`
- `verificationFailureCount: 0`
- `reviewFailureCount: 0`

每完成一个步骤，都运行状态脚本更新检查点，并刷新锁文件更新时间。

## 执行循环

仅针对目标任务：

1. 按顺序执行 checkbox 步骤。
2. 每完成一个步骤 后更新检查点。
3. 运行计划中该 Task 要求的验证命令。
4. 如果验证失败：
   - 递增 `verificationFailureCount`。
   - 记录 `lastFailedCommand`。
   - 如果仍在 Task 范围内，只做一次聚焦修复。
   - 重新运行验证。
   - 两轮修复后仍失败则以 `BLOCKED` 停止。
5. 验证通过后，启动只读 spec compliance review subagent。
6. 如果无法启动 spec review，以 `BLOCKED` 停止。
7. 如果 spec review 发现问题，递增 `reviewFailureCount`，由主 agent 修复、重新验证并重新 review。
8. 启动只读 code quality review subagent。
9. 如果无法启动 quality review，以 `BLOCKED` 停止。
10. 如果 quality review 发现问题，递增 `reviewFailureCount`，由主 agent 修复、重新验证并重新 review。
11. 只勾选目标任务 和实际完成步骤 的 checkbox。
12. 自检最终 diff，包括计划 checkbox 改动。
13. 使用简体中文提交信息创建 commit。
14. 将检查点标记为 `COMPLETED_TASK`。
15. 只删除本轮创建的锁。

## 只读 Review Prompt 形状

两次 review 都使用以下形状：

```text
你是只读 reviewer。不得编辑文件、stage 文件、commit、切换分支、创建 worktree 或运行破坏性命令。

工作区：<repo root>
计划：<plan path>
设计：<design path>
目标任务：<task title>
Review 类型：spec compliance | code quality

只读范围：
- AGENTS.md
- 目标计划
- 引用的设计文档
- git diff
- 相关源码和测试文件
- 主 agent 提供的验证输出

返回：
- STATUS: APPROVED | CHANGES_REQUESTED | BLOCKED
- Findings
- 涉及文件/行号，如果适用
- 验证风险
```

## 阻塞处理

以下情况进入 `BLOCKED`：

- 既有检查点已经是 `BLOCKED`。
- 当前任务 与代码现状冲突。
- 两轮聚焦修复后验证仍失败。
- 修复需要范围外架构决策。
- 无法启动只读 review subagent。
- review finding 无法在当前任务 范围内修复。
- 当前未提交改动会被覆盖。
- 计划指令不清楚。

阻塞时：

1. 写入检查点 `status: BLOCKED`。
2. 记录 `blockerReason`、`lastFailedCommand`、相关文件和 `nextAction`。
3. 不勾选当前任务。
4. 不执行后续任务。
5. 不创建成功 commit。
6. 只删除本轮创建的锁。
7. 报告 automation 应暂停或等待人工处理。

## 完成输出

使用 `AGENTS.md` 要求的输出形状：

- 改了什么
- 影响了哪些文件或模块
- 验证结果
- 已知风险
- 回滚思路
```

预期：`SKILL.md` 已存在，frontmatter 只包含 `name` 和 `description`。

- [x] **步骤 3: 新增 `openai.yaml`**

创建 `.codex/skills/newp-plan-runner/agents/openai.yaml`，内容如下：

```yaml
interface:
  display_name: NewP Plan Runner
  short_description: 按锁、检查点、验证和只读 review 执行一个既有 NewP 计划任务。
  default_prompt: 使用 newp-plan-runner 执行指定 NewP 计划中的第一个未完成任务。
policy:
  allow_implicit_invocation: true
```

预期：Codex app 元数据已存在，且没有声明外部工具依赖。

- [x] **步骤 4: 验证 skill 基础文件可读**

执行：

```powershell
Get-Content .\.codex\skills\newp-plan-runner\SKILL.md | Select-Object -First 20
Get-Content .\.codex\skills\newp-plan-runner\agents\openai.yaml
```

预期：两个文件均能正常输出，PowerShell 无错误。

- [x] **步骤 5: 提交 skill 骨架**

执行：

```powershell
git add .codex/skills/newp-plan-runner/SKILL.md .codex/skills/newp-plan-runner/agents/openai.yaml
git commit -m "功能：新增计划自动执行skill骨架"
```

预期：commit 成功，且不包含 `.codex/automation-locks` 或 `.codex-temp`。

## 任务 2: 实现检查点状态脚本

**文件：**
- 新建： `.codex/skills/newp-plan-runner/scripts/plan-run-state.ps1`

- [x] **步骤 1: 新增 `plan-run-state.ps1`**

创建 `.codex/skills/newp-plan-runner/scripts/plan-run-state.ps1`，内容如下：

```powershell
param(
    [Parameter(Mandatory = $true, Position = 0)]
    [ValidateSet("init", "update", "block", "complete-task", "plan-completed", "show")]
    [string]$Action,

    [Parameter(Mandatory = $true)]
    [string]$PlanPath,

    [string]$LockPath = "",
    [string]$TaskTitle = "",
    [int]$TaskIndex = -1,
    [string]$TaskHeadingHash = "",
    [string]$Step = "",
    [string]$ChangedFile = "",
    [string]$VerificationCommand = "",
    [string]$ReviewResult = "",
    [string]$LastFailedCommand = "",
    [string]$Reason = "",
    [string]$NextAction = "",
    [switch]$IncrementVerificationFailure,
    [switch]$IncrementReviewFailure
)

$ErrorActionPreference = "Stop"

function Get-RepoRoot {
    $root = git rev-parse --show-toplevel 2>$null
    if ([string]::IsNullOrWhiteSpace($root)) {
        throw "Must run inside a git repository."
    }

    return (Resolve-Path -LiteralPath $root.Trim()).Path
}

function Convert-ToRepoRelativePath {
    param(
        [Parameter(Mandatory = $true)]
        [string]$RepoRoot,
        [Parameter(Mandatory = $true)]
        [string]$Path
    )

    $fullPath = $Path
    if (-not [System.IO.Path]::IsPathRooted($fullPath)) {
        $fullPath = Join-Path $RepoRoot $Path
    }

    $resolved = [System.IO.Path]::GetFullPath($fullPath)
    $rootWithSeparator = $RepoRoot.TrimEnd([System.IO.Path]::DirectorySeparatorChar, [System.IO.Path]::AltDirectorySeparatorChar) + [System.IO.Path]::DirectorySeparatorChar
    if (-not $resolved.StartsWith($rootWithSeparator, [System.StringComparison]::OrdinalIgnoreCase)) {
        throw "Path is outside repository: $Path"
    }

    return $resolved.Substring($rootWithSeparator.Length).Replace("\", "/")
}

function Get-PlanId {
    param(
        [Parameter(Mandatory = $true)]
        [string]$PlanPath
    )

    $fileName = [System.IO.Path]::GetFileNameWithoutExtension($PlanPath)
    $slug = $fileName.ToLowerInvariant() -replace "[^a-z0-9\-]+", "-"
    $slug = $slug.Trim("-")
    if ([string]::IsNullOrWhiteSpace($slug)) {
        throw "Unable to derive plan id from: $PlanPath"
    }

    return $slug
}

function New-EmptyState {
    param(
        [Parameter(Mandatory = $true)]
        [string]$PlanPath,
        [Parameter(Mandatory = $true)]
        [string]$LockPath
    )

    [ordered]@{
        planPath = $PlanPath
        lockPath = $LockPath
        status = "RUNNING"
        targetTaskTitle = ""
        targetTaskIndex = -1
        targetTaskHeadingHash = ""
        startedAt = (Get-Date).ToString("o")
        updatedAt = (Get-Date).ToString("o")
        completedSteps = @()
        changedFiles = @()
        verificationCommands = @()
        verificationFailureCount = 0
        reviewFailureCount = 0
        lastFailedCommand = ""
        reviewResults = @()
        blockerReason = ""
        nextAction = ""
    }
}

function Read-State {
    param(
        [Parameter(Mandatory = $true)]
        [string]$StatePath
    )

    if (-not (Test-Path -LiteralPath $StatePath)) {
        return $null
    }

    return Get-Content -Raw -LiteralPath $StatePath -Encoding UTF8 | ConvertFrom-Json
}

function Save-State {
    param(
        [Parameter(Mandatory = $true)]
        [string]$StatePath,
        [Parameter(Mandatory = $true)]
        $State
    )

    $State.updatedAt = (Get-Date).ToString("o")
    $directory = Split-Path -Parent $StatePath
    if (-not (Test-Path -LiteralPath $directory)) {
        New-Item -ItemType Directory -Force -Path $directory | Out-Null
    }

    $State | ConvertTo-Json -Depth 8 | Set-Content -LiteralPath $StatePath -Encoding UTF8
}

function Add-UniqueValue {
    param(
        [Parameter(Mandatory = $true)]
        [object[]]$Values,
        [string]$Value
    )

    if ([string]::IsNullOrWhiteSpace($Value)) {
        return @($Values)
    }

    if ($Values -contains $Value) {
        return @($Values)
    }

    return @($Values) + $Value
}

function Touch-Lock {
    param(
        [string]$LockPath
    )

    if ([string]::IsNullOrWhiteSpace($LockPath)) {
        return
    }

    if (Test-Path -LiteralPath $LockPath) {
        (Get-Item -LiteralPath $LockPath).LastWriteTime = Get-Date
    }
}

$repoRoot = Get-RepoRoot
$planRelativePath = Convert-ToRepoRelativePath -RepoRoot $repoRoot -Path $PlanPath
$planId = Get-PlanId -PlanPath $planRelativePath

if ([string]::IsNullOrWhiteSpace($LockPath)) {
    $LockPath = ".codex/automation-locks/$planId.lock"
}

$lockRelativePath = Convert-ToRepoRelativePath -RepoRoot $repoRoot -Path $LockPath
$statePath = Join-Path $repoRoot ".codex-temp/plan-runs/$planId.json"
$lockFullPath = Join-Path $repoRoot $lockRelativePath

$state = Read-State -StatePath $statePath
if ($null -eq $state) {
    $state = New-EmptyState -PlanPath $planRelativePath -LockPath $lockRelativePath
}

if ($Action -eq "init") {
    $state = New-EmptyState -PlanPath $planRelativePath -LockPath $lockRelativePath
    $state.targetTaskTitle = $TaskTitle
    $state.targetTaskIndex = $TaskIndex
    $state.targetTaskHeadingHash = $TaskHeadingHash
}
elseif ($Action -eq "update") {
    $state.status = "RUNNING"
}
elseif ($Action -eq "block") {
    $state.status = "BLOCKED"
    $state.blockerReason = $Reason
    $state.nextAction = $NextAction
}
elseif ($Action -eq "complete-task") {
    $state.status = "COMPLETED_TASK"
}
elseif ($Action -eq "plan-completed") {
    $state.status = "PLAN_COMPLETED"
}
elseif ($Action -eq "show") {
    $state | ConvertTo-Json -Depth 8
    return
}

if (-not [string]::IsNullOrWhiteSpace($TaskTitle)) {
    $state.targetTaskTitle = $TaskTitle
}

if ($TaskIndex -ge 0) {
    $state.targetTaskIndex = $TaskIndex
}

if (-not [string]::IsNullOrWhiteSpace($TaskHeadingHash)) {
    $state.targetTaskHeadingHash = $TaskHeadingHash
}

$state.completedSteps = Add-UniqueValue -Values @($state.completedSteps) -Value $Step
$state.changedFiles = Add-UniqueValue -Values @($state.changedFiles) -Value $ChangedFile
$state.verificationCommands = Add-UniqueValue -Values @($state.verificationCommands) -Value $VerificationCommand
$state.reviewResults = Add-UniqueValue -Values @($state.reviewResults) -Value $ReviewResult

if ($IncrementVerificationFailure) {
    $state.verificationFailureCount = [int]$state.verificationFailureCount + 1
}

if ($IncrementReviewFailure) {
    $state.reviewFailureCount = [int]$state.reviewFailureCount + 1
}

if (-not [string]::IsNullOrWhiteSpace($LastFailedCommand)) {
    $state.lastFailedCommand = $LastFailedCommand
}

Save-State -StatePath $statePath -State $state
Touch-Lock -LockPath $lockFullPath

Write-Host "Plan run state updated: $statePath"
Write-Host "Status: $($state.status)"
```

预期：脚本已存在；除刷新锁文件时间外，只写入 `.codex-temp/plan-runs/`。

- [x] **步骤 2: 运行 init smoke**

执行：

```powershell
New-Item -ItemType Directory -Force -Path .\.codex\automation-locks | Out-Null
Set-Content -Path .\.codex\automation-locks\sample-plan.lock -Value "sample lock"
powershell -ExecutionPolicy Bypass -File .\.codex\skills\newp-plan-runner\scripts\plan-run-state.ps1 init -PlanPath docs/superpowers/plans/2026-05-02-standard-crud-backend-kernel.md -LockPath .codex/automation-locks/sample-plan.lock -TaskTitle "Task 1: Smoke" -TaskIndex 1 -TaskHeadingHash "sample-hash"
```

预期：命令退出码为 0，并写入 `.codex-temp/plan-runs/2026-05-02-standard-crud-backend-kernel.json`。

- [x] **步骤 3: 验证 checkpoint 字段**

执行：

```powershell
$state = Get-Content -Raw .\.codex-temp\plan-runs\2026-05-02-standard-crud-backend-kernel.json | ConvertFrom-Json
$state.status
$state.targetTaskTitle
$state.verificationFailureCount
$state.reviewFailureCount
```

预期输出包含：

```text
RUNNING
Task 1: Smoke
0
0
```

- [x] **步骤 4: 验证失败计数和阻塞写入**

执行：

```powershell
powershell -ExecutionPolicy Bypass -File .\.codex\skills\newp-plan-runner\scripts\plan-run-state.ps1 block -PlanPath docs/superpowers/plans/2026-05-02-standard-crud-backend-kernel.md -LockPath .codex/automation-locks/sample-plan.lock -Reason "verification failed twice" -NextAction "manual review" -LastFailedCommand "dotnet test" -IncrementVerificationFailure
$state = Get-Content -Raw .\.codex-temp\plan-runs\2026-05-02-standard-crud-backend-kernel.json | ConvertFrom-Json
$state.status
$state.blockerReason
$state.lastFailedCommand
$state.verificationFailureCount
```

预期输出包含：

```text
BLOCKED
verification failed twice
dotnet test
1
```

- [x] **步骤 5: 验证锁刷新**

执行：

```powershell
$before = (Get-Item .\.codex\automation-locks\sample-plan.lock).LastWriteTimeUtc
Start-Sleep -Seconds 2
powershell -ExecutionPolicy Bypass -File .\.codex\skills\newp-plan-runner\scripts\plan-run-state.ps1 update -PlanPath docs/superpowers/plans/2026-05-02-standard-crud-backend-kernel.md -LockPath .codex/automation-locks/sample-plan.lock -Step "refresh lock"
$after = (Get-Item .\.codex\automation-locks\sample-plan.lock).LastWriteTimeUtc
$after -gt $before
```

预期输出：

```text
True
```

- [x] **步骤 6: 清理 smoke 临时状态**

执行：

```powershell
Remove-Item -LiteralPath .\.codex\automation-locks\sample-plan.lock
Remove-Item -LiteralPath .\.codex-temp\plan-runs\2026-05-02-standard-crud-backend-kernel.json
```

预期：示例锁和示例检查点已删除；不得删除 `.codex/automation-locks` 或 `.codex-temp` 下其他文件。

- [x] **步骤 7: 提交状态脚本**

执行：

```powershell
git add .codex/skills/newp-plan-runner/scripts/plan-run-state.ps1
git commit -m "功能：新增计划执行检查点脚本"
```

预期：commit 成功，且不包含 smoke 生成的锁文件或检查点文件。

## 任务 3: 补齐 skill 验证与只读 review 规则

**文件：**
- 修改： `.codex/skills/newp-plan-runner/SKILL.md`

- [x] **步骤 1: 核对 `SKILL.md` 包含阻塞启动拦截**

执行：

```powershell
Select-String -Path .\.codex\skills\newp-plan-runner\SKILL.md -Pattern "status.*BLOCKED|检查点状态为 `BLOCKED`|计划存在阻塞检查点" -CaseSensitive:$false
```

预期：输出中包含 `BLOCKED` 启动门禁相关内容。

- [x] **步骤 2: 核对 `SKILL.md` 包含只读 review 降级规则**

执行：

```powershell
Select-String -Path .\.codex\skills\newp-plan-runner\SKILL.md -Pattern "只读|无法启动|不得编辑文件|不得跳过只读" -CaseSensitive:$false
```

预期：输出中包含只读 review，以及无法启动 review 时进入阻塞的规则。

- [x] **步骤 3: 核对 `SKILL.md` 包含默认调度频率**

执行：

```powershell
Select-String -Path .\.codex\skills\newp-plan-runner\SKILL.md -Pattern "每 20 分钟|每 10 分钟|调度频率" -CaseSensitive:$false
```

预期：输出中包含默认每 20 分钟执行一次，以及每 10 分钟显式覆盖规则。

- [x] **步骤 4: 如果步骤 1、步骤 2 或步骤 3 缺失则补齐文字**

如果任一检查失败，编辑 `.codex/skills/newp-plan-runner/SKILL.md`，在最接近的既有标题下补入缺失规则。新增文字必须为：

```markdown
如果检查点状态为 `BLOCKED`，在检查锁或未完成任务前停止。

如果无法启动只读 review subagent，将本轮标记为 `BLOCKED`；不得跳过 review，也不得创建成功提交。

默认 automation 调度频率为每 20 分钟执行一次。低于 10 分钟的频率默认不建议使用；短任务或高优先级计划可以显式配置为每 10 分钟触发一次。
```

预期：编辑后，步骤 1、步骤 2 和步骤 3 的 `Select-String` 检查均通过。

- [x] **步骤 5: 运行 skill frontmatter 校验**

执行：

```powershell
$validator = Join-Path $env:USERPROFILE ".codex\skills\.system\skill-creator\scripts\quick_validate.py"
python $validator .\.codex\skills\newp-plan-runner
```

预期：validator 退出码为 0。如果当前环境不存在该 validator 路径，运行以下 fallback：

```powershell
$content = Get-Content -Raw .\.codex\skills\newp-plan-runner\SKILL.md
if ($content -notmatch "(?s)^---\s*`r?`nname:\s*newp-plan-runner\s*`r?`ndescription:\s*.+?`r?`n---") { throw "Invalid skill frontmatter." }
```

fallback 预期： no exception.

- [x] **步骤 6: 提交 skill 规则修正**

执行：

```powershell
git status --short
git add .codex/skills/newp-plan-runner/SKILL.md .codex/skills/newp-plan-runner/agents/openai.yaml
git commit -m "功能：完善计划执行skill阻塞与审查规则"
```

预期：仅当任务 3 修改了文件时 commit 成功。如果没有文件变化，在最终验证记录中写明“任务 3 无需提交，规则已在任务 1 覆盖”。

## 任务 4: 增加验证记录

**文件：**
- 新建： `docs/verification/newp-plan-runner-skill-verification.md`

- [x] **步骤 1: 新增验证记录**

创建 `docs/verification/newp-plan-runner-skill-verification.md`，内容如下：

```markdown
# NewP 计划自动执行 Skill 验证

验证日期：2026-05-07

## 验证目标

- 确认 `newp-plan-runner` skill 已形成可发现入口。
- 确认检查点脚本可以初始化、更新、阻塞和刷新锁。
- 确认 `BLOCKED` 启动拦截、失败计数、只读 review 约束已写入 skill。
- 确认默认 automation 调度频率已记录为每 20 分钟执行一次。

## 验证命令

```powershell
Get-Content .\.codex\skills\newp-plan-runner\SKILL.md | Select-Object -First 20
Get-Content .\.codex\skills\newp-plan-runner\agents\openai.yaml
powershell -ExecutionPolicy Bypass -File .\.codex\skills\newp-plan-runner\scripts\plan-run-state.ps1 init -PlanPath docs/superpowers/plans/2026-05-02-standard-crud-backend-kernel.md -LockPath .codex/automation-locks/sample-plan.lock -TaskTitle "Task 1: Smoke" -TaskIndex 1 -TaskHeadingHash "sample-hash"
powershell -ExecutionPolicy Bypass -File .\.codex\skills\newp-plan-runner\scripts\plan-run-state.ps1 block -PlanPath docs/superpowers/plans/2026-05-02-standard-crud-backend-kernel.md -LockPath .codex/automation-locks/sample-plan.lock -Reason "verification failed twice" -NextAction "manual review" -LastFailedCommand "dotnet test" -IncrementVerificationFailure
powershell -ExecutionPolicy Bypass -File .\scripts\validate-ai-first-design.ps1 -Path .\docs\superpowers\specs\tooling\2026-05-05-newp-plan-runner-design.md
powershell -ExecutionPolicy Bypass -File .\scripts\check-ai-first-design-targets.ps1
```

## 验证结果

- `SKILL.md` 和 `openai.yaml` 可读取。
- `plan-run-state.ps1` 可写入 `RUNNING` 检查点。
- `plan-run-state.ps1` 可写入 `BLOCKED`、`blockerReason`、`lastFailedCommand` 和失败计数。
- checkpoint 更新会刷新锁文件更新时间。
- skill 文档记录默认 automation 调度频率为每 20 分钟执行一次。
- AI-first 设计校验通过。

## 已知风险

- 第一版不会自动暂停所有环境中的 Codex automation；如果运行环境不支持自动暂停，需要人工暂停。
- 第一版不解析计划 checkbox，避免脚本误改长期计划；Task 识别和勾选仍由 Codex 按 skill 流程完成。

## 回滚思路

- 删除 `.codex/skills/newp-plan-runner/`。
- 删除 `docs/verification/newp-plan-runner-skill-verification.md`。
- 如本轮产生临时状态，人工确认后删除对应 `.codex-temp/plan-runs/*.json` 和 `.codex/automation-locks/*.lock`。
```

预期： verification record exists and uses Simplified Chinese.

- [x] **步骤 2: 根据真实执行结果修正验证记录**

运行任务 2 的 smoke 命令和任务 5 的校验命令后，更新 `docs/verification/newp-plan-runner-skill-verification.md`，确保每条验证结果都对应真实命令输出。

预期：验证记录不会声称未在本轮实施中运行过的命令已经通过。

- [x] **步骤 3: 提交验证记录**

执行：

```powershell
git add docs/verification/newp-plan-runner-skill-verification.md
git commit -m "验证：记录计划自动执行skill结果"
```

预期：commit 成功。

## 任务 5: 全量设计校验与收口

**文件：**
- 修改： `docs/superpowers/specs/tooling/2026-05-05-newp-plan-runner-design.md` 仅当实现过程中发现设计需要修正时修改。
- 修改： `docs/verification/newp-plan-runner-skill-verification.md`

- [x] **步骤 1: 运行 AI-first 设计校验**

执行：

```powershell
powershell -ExecutionPolicy Bypass -File .\scripts\validate-ai-first-design.ps1 -Path .\docs\superpowers\specs\tooling\2026-05-05-newp-plan-runner-design.md
```

预期：命令退出码为 0，并输出该设计文件校验通过。

- [x] **步骤 2: 运行设计目标清单检查**

执行：

```powershell
powershell -ExecutionPolicy Bypass -File .\scripts\check-ai-first-design-targets.ps1
```

预期：命令退出码为 0，并在已检查文件列表中包含 `2026-05-05-newp-plan-runner-design.md`。

- [x] **步骤 3: 核对 git diff 不包含临时状态**

执行：

```powershell
git status --short
git diff --name-only
```

预期：

- 不出现 `.codex/automation-locks/*.lock` 路径。
- 不出现 `.codex-temp/plan-runs/*.json` 路径。
- 变更文件只限于 skill、计划、设计目标登记、可选设计修正和验证记录。

- [x] **步骤 4: 如有设计回填则提交**

如果任务 5 修改了设计文档或验证记录，执行：

```powershell
git add docs/superpowers/specs/tooling/2026-05-05-newp-plan-runner-design.md docs/verification/newp-plan-runner-skill-verification.md
git commit -m "文档：收口计划自动执行skill设计验证"
```

预期：如果存在 staged changes，则 commit 成功；如果没有变化，跳过提交并在最终输出中说明。

- [x] **步骤 5: 输出 AGENTS.md 收口信息**

最终输出必须包含：

- 改了什么。
- 影响了哪些文件或模块。
- 验证结果。
- 已知风险。
- 回滚思路。

预期：最终输出同时说明未能运行的验证命令及原因。

## 自检

- **规格覆盖：** 任务 1 覆盖 skill 入口和只读 review 规则；任务 2 覆盖检查点、失败计数、锁刷新和阻塞状态；任务 3 覆盖规则验证和 skill 校验；任务 4 覆盖验证记录；任务 5 覆盖 AI-first 检查和清理。
- **占位扫描：** 本计划不包含占位标记或延后实现指令。
- **类型一致性：** 本计划对 `newp-plan-runner`、`plan-run-state.ps1`、`PlanRunState`、`BLOCKED`、`COMPLETED_TASK` 和 `PLAN_COMPLETED` 的使用与设计保持一致。




