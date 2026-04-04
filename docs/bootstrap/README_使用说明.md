# Codex 平台重写启动包 v2

这套材料是按 `OldP` 的真实情况整理的，不假设旧系统只是普通 Web 单体。

适用前提：
- 旧平台同时存在 Web、API、服务、定时器、文件服务、Studio、WinApp 等宿主
- 旧平台包含大量运行时元数据和页面配置资产，而不只是 C# 代码
- 团队准备新建平台内核，但希望 Codex 能稳定参与盘点、契约、脚手架、模块实现和验证

## 这套包解决什么问题

相比原始版本，这一版额外强调 4 件事：
- 先盘点全量资产，再做处置矩阵，不直接冲进“重写代码”
- 明确迁移对象包括代码、元数据、数据、宿主、工具链五类
- 把第一阶段重点收敛到主干、契约、Demo、首批公共模块
- 让提示词直接要求 Codex 输出可执行产物，而不是泛泛建议

## 目录说明

- `codex/.codex/`
  - 项目级 Codex 配置
  - 4 个常用 agent：盘点、契约审查、文档核对、测试修复
- `prompts/`
  - 建议按顺序使用的提示词
- `templates/`
  - 新仓库初始化可直接落地的模板

## 推荐启动顺序

1. 先把 `templates/` 与 `codex/.codex/` 复制到新仓库
2. 执行 `prompts/01_旧平台全量资产盘点.md`
3. 执行 `prompts/02_旧平台资产处置矩阵.md`
4. 在新仓库先落地 `docs/scope-v1.md`，冻结第一阶段范围
5. 执行 `prompts/03_新仓库骨架与基础规则.md`
6. 执行 `prompts/04_后端模块契约与Host骨架.md`
7. 执行 `prompts/05_前端Shell与前端SDK.md`
8. 执行 `prompts/07_DemoModule端到端贯通.md`
9. 再逐个执行公共模块提示词

## 第一轮不要做什么

- 不要要求 Codex 一轮内“把整个平台重写完”
- 不要在同一轮里混合改架构、重命名、加功能
- 不要跳过盘点和处置矩阵直接做迁移
- 不要让多个活跃线程同时修改同一批文件

## 第一轮完成标准

- 新仓库已有主干骨架
- 后端和前端都能 build
- 已定义模块契约
- 已有一个 Demo Module 跑通注册到展示全链路
- 已冻结 `scope-v1`
- 已形成旧资产处置矩阵
- 已挑出第一批真正进入重写范围的模块和资产

## 当前建议第一阶段范围

建议纳入：
- Host / Kernel / Contracts / SDK
- 前端 Shell
- Demo Module
- Authorization
- ConfigCenter
- Audit
- 一类轻量旧资产迁移 POC

建议默认暂缓：
- WinApp
- Studio
- 文件服务重写
- Files 深度迁移
- 工作流
- 报表与设计器

## 最小可用文件

如果团队希望尽快启动，至少保留这些文件：
- `codex/.codex/config.toml`
- `codex/.codex/agents/platform-explorer.toml`
- `codex/.codex/agents/contract-reviewer.toml`
- `codex/.codex/agents/test-fixer.toml`
- `templates/AGENTS.md`
- `templates/Plan.md`
- `templates/docs/scope-v1.md`
- `templates/docs/asset-inventory-template.md`
- `templates/docs/asset-disposition-matrix.md`
- `prompts/01_旧平台全量资产盘点.md`
- `prompts/02_旧平台资产处置矩阵.md`
- `prompts/03_新仓库骨架与基础规则.md`
- `prompts/04_后端模块契约与Host骨架.md`
- `prompts/05_前端Shell与前端SDK.md`
