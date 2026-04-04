# OldP 资产处置矩阵 v1

## 1. 文档目的

本文件基于 [oldp-asset-inventory-v1.md](D:/Github/ICP-X/NewP/docs/oldp-asset-inventory-v1.md)，给出 `OldP` 首版资产处置决策。

本版本服务于新平台 V1：
- 先建立 Web 侧平台主干
- 先建立模块契约和 Shell（前端平台壳）
- 先完成 `Authorization / ConfigCenter / Audit / Demo Module`（权限、配置中心、审计、演示模块）
- 暂不进入 `WinApp`、`Studio`、文件服务重写

## 2. 决策定义

- `Retire`（淘汰）：不带入新平台
- `Rewrite`（重写）：在新平台中原生重做
- `Wrap`（包裹兼容）：短期兼容封装，后续应退出
- `Import`（导入）：将旧数据或元数据转换导入到新平台
- `Postpone`（暂缓）：不进入 V1

## 3. V1 先纳入范围

建议第一阶段纳入：
- Web 侧 Host（宿主）
- 前端 Shell（平台壳）
- Kernel / Contracts / SDK（内核、契约、SDK）
- Demo Module（演示模块）
- Authorization（权限与授权）
- ConfigCenter（配置中心）
- Audit（审计）
- 一类轻量旧资产导入 POC（概念验证）

## 4. V1 默认排除范围

建议第一阶段默认排除：
- WinApp
- Studio
- 文件服务重写
- 深度文件迁移
- 工作流
- 报表与设计器
- 大规模旧页面兼容
- 大体量业务模块迁移

## 5. 首版处置矩阵

| 资产 | 类别 | 当前位置 | 决策 | 目标模块/区域 | 原因 | 前置条件 | 备注 |
| --- | --- | --- | --- | --- | --- | --- | --- |
| ASP.NET Core Web Host | Host | `Source\3_ServiceApp\02_Core\Hoteam.InforCenter.WebHost.Console` | Rewrite | `apps/host-api` | 新平台需要统一 Web Host，但不应直接复制旧宿主启动逻辑 | 定义 Host 边界与启动入口 | 保留能力，不保留旧实现 |
| ServiceWebApiHost | Host | `Source\3_ServiceApp\02_Core\ServiceWebApiHost\*` | Rewrite | `apps/host-api` / compatibility notes | 与新平台 Web/API 入口相关，但旧结构不宜长期保留 | 先完成 Host 契约 | 需盘点哪些 API 真的进入 V1 |
| ASP.NET Runtime | Runtime Host | `Runtime\1_Server` | Postpone | compatibility backlog | 属于旧式运行时目录，不适合直接进入新平台主干 | 完成资产清单 | 仅盘点，不迁入 V1 |
| `7_Business` 大体量业务模块 | Business Code | `Source\7_Business` | Postpone | migration backlog | 数量过大，业务与平台能力混杂，V1 应先拆边界 | 完成模块分类 | 只挑平台公共能力切入 |
| `1_Framework` 公共层 | Framework Code | `Source\1_Framework` | Rewrite | `server/kernel` / `server/sdk` | 有平台底层价值，但旧框架包袱重，不宜整体搬运 | 先提取契约 | 只能“抽能力”，不能整包复制 |
| `2_WebApp` Web 项目群 | Web UI | `Source\2_WebApp` | Postpone | migration backlog | 旧 Web UI 与 WebService 项目需逐项甄别，不适合 V1 全纳 | 完成页面与功能映射 | 仅提炼公共壳能力 |
| `3_ServiceApp` 非 Web 宿主 | Service Host | `Source\3_ServiceApp\01_Net`, `02_Core` 非 Web 部分 | Postpone | compatibility backlog | 宿主多且杂，V1 不应同时重做 | 完成宿主分类 | 后续按必要性再拆分 |
| Timer 宿主 | Timer Host | `Source\4_TimerApp` | Postpone | backlog | 不阻塞平台主干成立 | 无 | 先记录 |
| FileServer 宿主群 | File Host | `Source\6_FileServer`, `Runtime\3_FileServer` | Postpone | backlog | 当前已明确不进入 V1 | 无 | 仅保留接口占位需求 |
| WinApp 项目群 | Desktop | `Source\8_WinApp` | Postpone | backlog | 当前范围明确排除 | 无 | 只做盘点 |
| Studio 项目群 | Tooling/Desktop | `Source\9_Studio` | Postpone | backlog | 当前范围明确排除 | 无 | 只做盘点 |
| Authorization 相关代码 | Platform Capability | `Source\7_Business\15_ModulePermission\*`, `27_ThreeRoleAuthPlatform\*` | Rewrite | `server/modules/Authorization` | 属于平台治理核心，适合 V1 建最小新模型 | 先完成权限契约 | 旧代码作为参考来源 |
| SystemSettings | Platform Capability | `Source\7_Business\12_SystemSettings\*` | Rewrite | `server/modules/ConfigCenter` | 配置中心属于平台公共能力 | 完成配置契约 | 不直接复制旧存储逻辑 |
| Enumeration | Platform Capability | `Source\7_Business\09_Enumeration\*` | Import | `server/modules/ConfigCenter` | 枚举更像资产导入，不一定需要原样重写 | 确定目标数据模型 | 适合作为轻量 POC |
| Dictionary 配置资产 | Runtime Metadata | `Runtime\1_Server\Base\Config\Dictionary`, `Runtime\1_Server\Example\Config\Dictionary` | Import | `ConfigCenter` / migration tools | 更适合导入到新配置模型，而非重写 | 先定义字典模型 | 适合作为轻量 POC |
| Setting 配置资产 | Runtime Config | `Runtime\1_Server\Config\Setting` | Import | `ConfigCenter` / migration tools | 典型配置资产，导入价值高 | 先定义配置项模型 | 可作为首个导入样本 |
| OperateLog | Platform Capability | `Source\7_Business\05_OperateLog\*` | Rewrite | `server/modules/Audit` | 审计能力相对独立，适合 V1 | 先定义审计抽象 | 旧代码只作行为参考 |
| `.dic` 资产 | Metadata | `Runtime\1_Server\**\*.dic` | Import | migration tools / ConfigCenter / metadata adapters | 数量大，且具有配置属性 | 先完成资产分类 | 不建议在 V1 全量处理 |
| `.generator` 资产 | Metadata | `Runtime\1_Server\**\*.generator` | Postpone | metadata strategy | 规模最大，语义复杂，V1 风险高 | 先出迁移策略 | 暂不进入首批实现 |
| `.page` 资产 | Metadata | `Runtime\1_Server\**\*.page` | Postpone | metadata strategy | 与页面体系绑定较深，不适合 V1 先碰 | 先出迁移策略 | 可后续选样本验证 |
| `.view` 资产 | Metadata | `Runtime\1_Server\**\*.view` | Postpone | metadata strategy | 与旧 UI 结构耦合 | 先出迁移策略 | 暂不进入 V1 |
| `.model` / `.schema` 资产 | Metadata | `Runtime\1_Server\**\*.model`, `*.schema` | Import | metadata migration POC | 可能适合作为模型导入或转换来源 | 先明确目标模型 | 需小样本验证 |
| `.aspx` 页面 | Legacy UI | `Runtime\1_Server\**\*.aspx` | Postpone | compatibility backlog | 当前前端路线已切到 React Shell | 先完成新壳 | 暂不做大规模兼容 |
| `.cshtml` 页面 | Legacy UI | `Source\3_ServiceApp\02_Core\**\*.cshtml` | Postpone | compatibility backlog | 不属于 V1 核心交付 | 先完成新壳 | 仅记录 |
| 旧 JS 页面与脚本 | Legacy UI | `Runtime\1_Server\**\*.js` | Retire | frontend shell / new modules | 数量大且非现代工程化结构，不适合带入新平台 | 新前端主干建立 | 必要交互重新实现 |
| ResetPassword 等注册页面 | Legacy UI / Identity Edge | `Runtime\1_Server\Registered\ResetPassword\*` | Postpone | auth backlog | 与身份流程有关，但当前未纳入完整 Identity 模块 | Authorization 雏形完成后再评估 | 先记录 |
| Example 配置与示例资产 | Example / Metadata | `Runtime\1_Server\Example\*` | Retire | docs/examples | 示例资产可参考，不必整体迁入 | 无 | 只保留参考价值 |
| Report 相关资产 | Reporting | `Runtime\1_Server\Platform\ReportMangement`, `Example\Report\*` | Postpone | backlog | 当前阶段明确排除报表与设计器 | 无 | 后续单列 |
| Workflow 相关资产 | Workflow | `Source\7_Business\24_Workflow`, `Example\Config\Pages\WorkFlowDesignerExtend` | Postpone | backlog | 当前阶段明确排除 | 无 | 后续单列 |

## 6. 第一阶段推荐实施顺序

1. Rewrite（重写）：Host / Kernel / Contracts / Shell
2. Rewrite（重写）：Demo Module
3. Rewrite（重写）：Authorization
4. Rewrite（重写）：ConfigCenter 最小模型
5. Import（导入）：Setting / Dictionary / Enumeration 轻量 POC
6. Rewrite（重写）：Audit

## 7. 当前最适合先做的 Import POC

按风险和收益平衡，建议优先顺序：

1. `Runtime\1_Server\Config\Setting`
2. `Runtime\1_Server\Base\Config\Dictionary`
3. `Source\7_Business\09_Enumeration\*` 对应枚举资产

理由：
- 更接近平台公共能力
- 结构相对可控
- 不会立刻把团队拖进旧页面兼容和复杂设计器体系

## 8. 当前不建议采用的策略

- 不要把 `1_Framework` 或 `7_Business` 整包复制进新仓库
- 不要把 `.generator/.page/.view` 作为第一批硬迁移目标
- 不要用兼容层把旧 `.aspx` 页面直接塞进新壳
- 不要在 V1 同时重做文件服务、Studio、WinApp

## 9. 下一步建议

建议按以下顺序继续推进：

1. 基于本矩阵评审 V1 范围
2. 细化 `Authorization / ConfigCenter / Audit` 的模块边界
3. 产出元数据迁移策略文档
4. 选择 `Setting` 或 `Dictionary` 作为首个 Import POC
