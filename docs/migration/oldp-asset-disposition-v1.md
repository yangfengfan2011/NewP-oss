# OldP 资产处置矩阵 v1

## 1. 文档目的

本文档基于 [oldp-asset-inventory-v1.md](oldp-asset-inventory-v1.md)，给出 `OldP` 首版资产处置决策。

本版本服务于新平台 V1：

- 先建立 Web 侧平台主干
- 先建立模块契约和前端 Shell
- 先完成 `Authorization / ConfigCenter / Audit / Demo Module`
- 暂不进入 `WinApp`、`Studio`、文件服务重写

## 2. 决策定义

- `Retire`：不带入新平台
- `Rewrite`：在新平台中原生重做
- `Wrap`：短期兼容封装，后续应退出
- `Import`：将旧数据或元数据转换导入到新平台
- `Postpone`：不进入 V1

## 3. V1 优先纳入范围

建议第一阶段纳入：

- Web Host
- 前端 Shell
- Kernel / Contracts / SDK
- Demo Module
- Authorization
- ConfigCenter
- Audit
- 一个轻量旧资产导入 POC

## 4. V1 默认排除范围

建议第一阶段默认排除：

- WinApp
- Studio
- 文件服务重写
- 深度文件迁移
- 工作流
- 报表与设计器
- 大规模旧页面兼容
- 大体量业务模块整体迁移

## 5. 首版处置矩阵

| 资产 | 类别 | 当前位置 | 决策 | 目标区域 | 备注 |
| --- | --- | --- | --- | --- | --- |
| Web Host | Host | `Source/3_ServiceApp/...WebHost...` | Rewrite | `apps/host-api` | 重建新 Host，不直接复制旧启动逻辑 |
| Service API Host | Host | `Source/3_ServiceApp/...ServiceWebApiHost...` | Rewrite | `apps/host-api` | 合并到新平台统一入口 |
| ASP.NET Runtime 目录 | Runtime Host | `Runtime/1_Server` | Postpone | compatibility backlog | 先盘点，不直接带入 V1 |
| `1_Framework` 公共层 | Framework Code | `Source/1_Framework` | Rewrite | `server/kernel` / `server/sdk` | 只抽能力，不整包迁移 |
| `7_Business` 业务模块群 | Business Code | `Source/7_Business` | Postpone | migration backlog | V1 只挑平台公共能力切入 |
| 旧 WebApp 项目群 | Web UI | `Source/2_WebApp` | Postpone | migration backlog | 不做整体迁移 |
| 非 Web 服务宿主 | Service Host | `Source/3_ServiceApp` 非 Web 部分 | Postpone | backlog | 不阻塞主干成立 |
| Timer 宿主 | Timer Host | `Source/4_TimerApp` | Postpone | backlog | 先记录 |
| FileServer 宿主群 | File Host | `Source/6_FileServer` | Postpone | backlog | 当前明确不进 V1 |
| WinApp 项目群 | Desktop | `Source/8_WinApp` | Postpone | backlog | 当前范围明确排除 |
| Studio 项目群 | Tooling/Desktop | `Source/9_Studio` | Postpone | backlog | 当前范围明确排除 |
| Authorization 相关代码 | Platform Capability | `Source/7_Business/...Permission...` | Rewrite | `server/modules/Authorization` | 平台治理核心，适合 V1 |
| SystemSettings | Platform Capability | `Source/7_Business/12_SystemSettings` | Rewrite | `server/modules/ConfigCenter` | 配置中心公共能力 |
| Enumeration | Platform Capability | `Source/7_Business/09_Enumeration` | Import | `server/modules/ConfigCenter` | 更适合作为导入资产 |
| Dictionary 配置资产 | Runtime Metadata | `Runtime/.../Dictionary` | Import | `ConfigCenter` / migration tools | 适合轻量 POC |
| Setting 配置资产 | Runtime Config | `Runtime/.../Setting` | Import | `ConfigCenter` / migration tools | 可作为首个导入样例 |
| OperateLog | Platform Capability | `Source/7_Business/05_OperateLog` | Rewrite | `server/modules/Audit` | 边界相对独立 |
| `.dic` 资产 | Metadata | `Runtime/**/*.dic` | Import | migration tools / ConfigCenter | 先挑样本验证 |
| `.generator` 资产 | Metadata | `Runtime/**/*.generator` | Postpone | metadata strategy | 语义复杂，V1 风险高 |
| `.page` 资产 | Metadata | `Runtime/**/*.page` | Postpone | metadata strategy | 与旧页面体系耦合深 |
| `.view` 资产 | Metadata | `Runtime/**/*.view` | Postpone | metadata strategy | 暂不进入 V1 |
| `.model` / `.schema` | Metadata | `Runtime/**/*.model` / `*.schema` | Import | metadata migration POC | 可后续选样验证 |
| `.aspx` 页面 | Legacy UI | `Runtime/**/*.aspx` | Postpone | compatibility backlog | 不做大规模兼容 |
| `.cshtml` 页面 | Legacy UI | `Source/.../**/*.cshtml` | Postpone | compatibility backlog | 当前不纳入核心交付 |
| 旧 JS 页面与脚本 | Legacy UI | `Runtime/**/*.js` | Retire | new frontend shell | 新前端重新实现 |
| Example 示例资产 | Example / Metadata | `Runtime/.../Example/*` | Retire | docs/examples | 仅保留参考价值 |
| Report 相关资产 | Reporting | `Runtime/.../Report*` | Postpone | backlog | 当前范围排除 |
| Workflow 相关资产 | Workflow | `Source/.../Workflow` | Postpone | backlog | 当前范围排除 |

## 6. 第一阶段推荐实施顺序

1. Rewrite：Host / Kernel / Contracts / Shell
2. Rewrite：Demo Module
3. Rewrite：Authorization
4. Rewrite：ConfigCenter 最小模型
5. Import：Setting / Dictionary / Enumeration 轻量 POC
6. Rewrite：Audit

## 7. 当前最适合优先做的 Import POC

按风险和收益平衡，建议优先顺序：

1. `Setting`
2. `Dictionary`
3. `Enumeration`

原因：

- 更接近平台公共能力
- 结构相对可控
- 不会立刻把团队拖进旧页面兼容和复杂设计器体系

## 8. 当前不建议采用的策略

- 不要把 `1_Framework` 或 `7_Business` 整包复制进新仓库。
- 不要把 `.generator/.page/.view` 作为第一批硬迁移目标。
- 不要用兼容层把旧 `.aspx` 页面直接塞进新 Shell。
- 不要在 V1 同时重做文件服务、Studio、WinApp。

## 9. 下一步建议

1. 基于本矩阵评审 V1 范围。
2. 细化 `Authorization / ConfigCenter / Audit` 的模块边界。
3. 产出元数据迁移策略文档。
4. 选择 `Setting` 或 `Dictionary` 作为首个 Import POC。
