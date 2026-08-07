# 任务跟踪器：GitHub

本仓库的 issue 与规格存放在 GitHub Issues 中。所有操作默认使用 `gh` CLI，并从当前仓库的 `git remote` 自动识别目标仓库。

## 约定

- 创建 issue：`gh issue create --title "..." --body-file <file>`
- 读取 issue：`gh issue view <number> --comments`
- 列出 issue：`gh issue list --state open --json number,title,body,labels,comments`
- 添加评论：`gh issue comment <number> --body "..."`
- 添加或移除标签：`gh issue edit <number> --add-label "..."` 或 `--remove-label "..."`
- 关闭 issue：`gh issue close <number> --comment "..."`
- 处理多行正文时，在 PowerShell 中优先使用临时正文文件配合 `--body-file`，避免复杂的命令行转义。

需要筛选标签、评论或状态时，使用 `gh` 的 `--json` 与 `--jq` 参数，只保留当前任务需要的字段。

## 是否将 Pull Request 作为分流入口

**PRs as a request surface: no.**

如果以后希望把外部 Pull Request 也纳入 `/triage`，可将上面的值改为 `yes`。启用后：

- 读取 PR：`gh pr view <number> --comments`，并按需执行 `gh pr diff <number>`
- 列出外部 PR：`gh pr list --state open --json number,title,body,labels,author,authorAssociation,comments`
- 仅保留 `CONTRIBUTOR`、`FIRST_TIME_CONTRIBUTOR` 或 `NONE`
- 评论、标签和关闭分别使用 `gh pr comment`、`gh pr edit` 和 `gh pr close`

GitHub Issues 与 Pull Requests 共用编号空间。遇到裸编号 `#42` 时，先执行 `gh pr view 42`；若不是 PR，再执行 `gh issue view 42`。

## 技能操作映射

当技能要求“发布到任务跟踪器”时，创建 GitHub issue。

当技能要求“获取相关 ticket”时，执行：

`gh issue view <number> --comments`

## Wayfinder 操作

`/wayfinder` 使用一个 map issue 管理多个 child issue：

- Map：单个带有 `wayfinder:map` 标签的 issue，正文保存 Notes、Decisions-so-far 和 Fog。
- Child ticket：优先使用 GitHub sub-issue 关联；不可用时，在 map 正文中维护任务列表，并在 child 顶部写入 `Part of #<map>`。
- Child 标签：使用 `wayfinder:<type>`，类型包括 `research`、`prototype`、`grilling` 和 `task`。
- 阻塞关系：优先使用 GitHub 原生 issue dependencies；不可用时，在 child 顶部记录 `Blocked by: #<n>`。
- Frontier：从 map 的未关闭 child 中排除仍被阻塞或已有负责人者，再按 map 顺序选择第一个。
- 认领：`gh issue edit <n> --add-assignee @me`。这是执行会话的第一次写操作。
- 完成：向 child 写入结论、关闭 child，并在 map 的 Decisions-so-far 中追加上下文链接。
