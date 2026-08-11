# 平台重写与 Codex 协作实施手册 v2 提纲

## 1. 项目定位

本项目不是旧系统升级补丁，而是平台内核重建。
但“可停机重写”不等于“可以跳过迁移分类”。
旧平台应被视为五类迁移对象的组合：
- 代码
- 运行时元数据
- 数据
- 宿主
- 工具链与发布资产

## 2. 核心原则

- 先盘点，再决策，再实施
- 先主干和契约，再公共模块，再迁移旧资产
- 先做模块化单体，不抢跑微服务
- Codex 适合做盘点、契约、脚手架、验证和受控实现，不适合当黑箱总包

## 3. 第一阶段真正目标

第一阶段不是“旧平台功能全部具备”，而是：
- 新主干成立
- 模块契约成立
- Demo Module 跑通
- 首批公共模块成立
- 一类真实旧资产完成导入或兼容验证

## 4. 旧平台资产分类

应至少覆盖：
- 宿主：Web、API、服务、定时器、文件服务、Studio、WinApp
- 代码：Framework、Business、WebApp、ServiceApp、FileServer 等
- 元数据：`.page/.view/.model/.schema/.generator/.dic`
- 页面：`.aspx/.cshtml/旧 JS`
- 配置与发布资产：运行时目录、`web.config`、发布目录

## 5. 处置矩阵

每项资产必须先归类为：
- Retire
- Rewrite
- Wrap
- Import
- Postpone

没有进入处置矩阵的资产，不进入开发排期。

## 6. 技术路线

- 后端：当前 LTS .NET + ASP.NET Core
- 前端：React + TypeScript + Vite
- 架构：模块化单体

## 7. 平台分层

- Host
- Kernel
- Module Contracts
- Built-in Modules
- SDK
- Compatibility / Migration Layer

建议明确加入 `Compatibility / Migration Layer`，避免兼容逻辑污染 Kernel。

## 8. 第一批模块

建议第一批做：
- Authorization
- ConfigCenter
- Audit
- Demo Module

建议暂缓：
- Organization
- Tenant
- Workflow
- 报表设计器
- Studio / WinApp 重写

## 9. 路线图

### Phase 0
- 全量资产盘点
- 资产处置矩阵
- 第一阶段范围冻结

### Phase 1
- 新仓库骨架
- AGENTS / Plan / scripts / docs
- Host / Shell 初版

### Phase 2
- 后端模块契约
- 前端模块契约
- Demo Module

### Phase 3
- Authorization
- ConfigCenter
- Audit

### Phase 4
- 一类真实旧资产导入或兼容验证

## 10. Codex 协作规则

- 一次只做一个模块或一个能力
- 不混合大改架构与功能新增
- 子代理优先只读探索和审查
- 真正写代码的线程尽量只有一个
- 每轮必须输出验证结果和风险

## 11. 验收标准

除 build/test 之外，应新增：
- 资产清单已完成
- 资产处置矩阵已完成
- 一类真实旧资产已完成 POC
- 兼容边界和退出路径已记录
