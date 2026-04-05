# Scope V1（第一阶段范围）

## 文档目的

本文件用于冻结新平台第一阶段范围，避免在主干尚未成立前把任务扩张到旧平台全部能力。

当前范围基于以下共识：
- 先建立 Web 侧平台主干
- 先建立模块契约和平台壳
- 先建立最小可登录能力
- 先实现最小公共治理能力
- 暂不处理 `WinApp`、`Studio`、文件服务重写

## 第一阶段目标

第一阶段不是“重写完整旧平台”，而是完成以下 5 件事：

1. 建立新平台主干
2. 建立后端与前端模块契约
3. 建立最小可登录能力
4. 打通一个 Demo Module 全链路
5. 做出首批平台公共模块雏形

## 纳入范围

### 平台主干
- 后端宿主骨架（backend host skeleton）
- 内核与契约层（kernel and contracts）
- 服务端 SDK 接入入口（server-side SDK entry points）
- 前端 SDK 接入入口（frontend SDK entry points）
- 前端平台壳（frontend shell）
- 仓库文档与脚本（repository docs and scripts）

### 契约与接入机制
- 服务端模块元数据（server module metadata）
- 服务注册契约（service registration contract）
- 权限声明契约（permission declaration contract）
- 菜单与路由声明契约（menu and route declaration contract）
- 配置声明契约（config declaration contract）
- 迁移或导入钩子契约（migration or import hook contract）

### 第一批模块
- Identity Lite / LocalAuth（最小登录能力）
- Demo Module（演示模块）
- Authorization（权限与授权）
- ConfigCenter（配置中心）
- Audit（审计）

### 迁移验证
- 一类轻量旧资产迁移或导入概念验证（one lightweight old-asset migration or import proof of concept）

建议优先选择：
- 配置项
- 字典
- 枚举
- 轻量元数据资产

## 明确排除范围

以下内容默认不进入 V1 开发排期：

- WinApp 重写（WinApp rewrite）
- Studio 重写（Studio rewrite）
- 文件服务重写（file-service rewrite）
- 深度文件迁移（deep file migration）
- 工作流重设计（workflow redesign）
- 报表或设计器重设计（report or designer redesign）
- 大体量业务模块迁移（large business-module migration）
- 组织与租户能力的完整迁移（organization and tenant full migration）
- 旧 `.aspx` 页面的大规模兼容（old `.aspx` pages large-scale compatibility）

## 可记录但不实现的占位能力

以下能力可以保留扩展点、契约或目录，但不在 V1 落实完整实现：

- Files 抽象占位（Files abstraction placeholder）
- Tenant 扩展点占位（Tenant extension point）
- Organization 扩展点占位（Organization extension point）
- 兼容层或迁移层骨架（compatibility or migration layer skeleton）

## 第一阶段交付物

V1 至少应交付：

- 新仓库基础目录和规则文件
- Host / Shell（宿主与前端壳）可编译主干
- 最小登录闭环可运行
- 模块契约文档与基础实现
- Identity Lite / LocalAuth（最小登录能力）第一版
- Demo Module（演示模块）
- Authorization（权限与授权）第一版
- ConfigCenter（配置中心）第一版
- Audit（审计）第一版
- 旧平台资产盘点文档
- 资产处置矩阵
- 一类轻量旧资产迁移或导入 POC

## 完成判定

满足以下条件，才能认为 V1 范围基本完成：

- 后端构建通过（backend build）
- 前端构建通过（frontend build）
- 已存在固定启动入口
- 存在登录页、登录接口、当前用户接口和退出登录能力
- 存在默认管理员或本地种子账号
- Demo Module（演示模块）能从注册到前端展示全链路跑通
- Authorization / ConfigCenter / Audit 已有最小可用版本
- 登录后可进入主界面并访问受权限控制页面
- 资产盘点与处置矩阵已完成
- 已完成一类轻量旧资产迁移或导入验证

## 不允许的范围漂移

在没有明确重新评审范围之前，不应把以下内容直接插入当前排期：

- “顺手”补 Studio
- “顺手”补 WinApp
- “顺手”接旧文件服务
- “顺手”兼容大量旧页面
- 在同一轮里同时做架构重构、功能新增和迁移改造

## 范围变更规则

如果确实需要把排除项拉回 V1，至少要补充以下信息：

- 变更原因
- 对当前里程碑的影响
- 是否阻塞主干成立
- 哪些文件或模块会受影响
- 是否需要新建独立 worktree
- 谁来承担额外复杂度

没有完成以上评估前，排除项继续保持排除。
