# Worktree 使用指南

本文档说明如何在本项目中使用 `git worktree` 做相对独立的任务开发。

## 核心原则

- 一个 worktree 只负责一个大任务。
- 一个大任务只对应一个明确目标。
- 不要和其他 worktree 同时修改同一批核心文件。

## 什么时候适合新建 worktree

适合：

- 当前任务会持续多轮。
- 当前任务会改多个目录。
- 当前任务与其他任务在文件层面可以分开。
- 希望把不同模块并行推进。

不适合：

- 只改一两个文件的小修。
- 还没搞清楚任务边界。
- 当前任务会频繁和其他线程改同一批文件。

## 开始之前先确认

先阅读并确认：

- [../../AGENTS.md](../../AGENTS.md)
- [../phases/post-phase-6-stage-map.md](../phases/post-phase-6-stage-map.md)
- [../architecture/scope-v1.md](../architecture/scope-v1.md)
- 与当前任务直接相关的模块边界文档

然后明确：

- 这个 worktree 的唯一任务是什么
- 本轮成功标准是什么
- 明确不做什么
- 预计会改哪些目录
- 预计要跑哪些验证命令
- 是否需要 subagents 协助

## 推荐执行流程

### 阶段 1：摸底

- 阅读相关文档
- 看相关目录和关键文件
- 确认任务是否在 `scope-v1` 范围内

### 阶段 2：收敛方案

- 用 5 到 10 条要点写清方案
- 说明为什么这样做
- 标记风险点

### 阶段 3：实施改动

- 主线程实施核心改动
- 子代理只做探索、审查或最后阶段验证

### 阶段 4：验证

- 后端 build/test
- 前端 build/test
- smoke 验证

### 阶段 5：输出

- 改动摘要
- 影响范围
- 验证结果
- 风险点
- 回滚方式

## 常见任务的验证建议

### 后端主干 / Host 任务

- `dotnet build ICP-Platform.sln`
- Host 能启动
- 健康检查接口可访问

### 前端 Shell 任务

- `cd frontend && pnpm install`
- `cd frontend && pnpm build`
- 登录页可访问

### 模块任务

- 后端 build
- 相关测试
- 前端 build
- 对应功能可通过最小路径访问

## 参考

详细任务模板见：
[../prompts/14_通用Worktree任务模板.md](../prompts/14_通用Worktree任务模板.md)
