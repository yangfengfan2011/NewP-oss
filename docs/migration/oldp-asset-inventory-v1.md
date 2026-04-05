# OldP 资产盘点报告 v1

## 1. 盘点范围

- 盘点时间：2026-04-04
- 盘点对象：`D:\Github\ICP-X\OldP`
- 目的：为新平台 V1 范围冻结、资产处置矩阵和第一阶段实施路线提供输入

本次盘点重点覆盖：
- 宿主与运行入口
- 代码项目分布
- 运行时元数据资产
- 页面与前端资产
- 与 V1 最相关的候选模块

## 2. 总体结构摘要

`OldP` 不是单一 Web 项目，而是多宿主、多代 .NET、代码与运行时元数据混合承载的平台仓库。

当前识别到的核心特征：
- `Source` 下共有 `313` 个 `csproj`
- 项目数最多的区域是 `7_Business`，共 `179` 个项目
- 其次是 `1_Framework`，共 `58` 个项目
- `9_Studio` 仍有 `30` 个项目
- `8_WinApp` 有 `6` 个项目
- `6_FileServer` 有 `6` 个项目
- 运行时元数据资产共约 `7323` 个文件

这说明旧平台不是“代码重写”这么简单，而是“代码、宿主、运行时配置/模型、页面资产”共同构成的平台。

## 3. 宿主清单

### 3.1 Web 与 Web 运行时（Web Runtime）

主要入口与运行时痕迹：
- `Runtime\1_Server\Global.asax`
- `Runtime\1_Server\Web.config`
- `Runtime\2_ServerX\01_Web\Publish\web.config`
- `Source\3_ServiceApp\02_Core\Hoteam.InforCenter.WebHost.Console\Program.cs`
- `Source\3_ServiceApp\02_Core\Hoteam.InforCenter.WebHost.Console\Startup.cs`
- `Source\3_ServiceApp\02_Core\ServiceWebApiHost\Hoteam.InforCenter.WebApiService.Web.Entry\Program.cs`
- `Source\3_ServiceApp\02_Core\ServiceWebApiHost\Hoteam.InforCenter.WebApiService.Web.Core\Startup.cs`

判断：
- 同时存在旧式 ASP.NET Runtime（运行时站点）和较新的 ASP.NET Core Web Host（Web 宿主）
- 旧平台处于多代宿主并存状态

### 3.2 后台服务与控制台

典型入口：
- `Source\3_ServiceApp\01_Net\Hoteam.InforCenter.Service.Console\Program.cs`
- `Source\3_ServiceApp\02_Core\Hoteam.InforCenter.Service.Console\Program.cs`
- `Source\4_TimerApp\Hoteam.InforCenter.Service.TimerConsole\Program.cs`
- `Source\7_Business\21_UpdateService\HoteamsoftUpdateService\Program.cs`

判断：
- 平台存在服务类宿主与定时任务类宿主
- 这些宿主不建议进入 V1 重写范围，但应进入资产处置矩阵

### 3.3 文件服务

典型入口：
- `Source\6_FileServer\HoteamsoftFileService3.0\Program.cs`
- `Source\6_FileServer\HoteamsoftFsManagementServer\Program.cs`
- `Source\6_FileServer\HoteamsoftCommandService\Program.cs`
- `Runtime\3_FileServer\ConfigCenter\web.config`

判断：
- 文件服务是独立宿主群
- 当前阶段建议盘点，但默认标记为 `Postpone`

### 3.4 WinApp 与 Studio

典型入口：
- `Source\8_WinApp\01_Base\Hoteam.InforCenter.App.Start\Program.cs`
- `Source\9_Studio\01_Base\Hoteam.InforCenter.Console.Start\Program.cs`
- `Source\9_Studio\01_Base\Hoteam.InforCenter.Developer.Start\Program.cs`
- `Source\9_Studio\01_Base\Hoteam.InforCenter.Developer.CodeGenerator\Program.cs`

判断：
- 旧平台包含桌面端和开发工具链
- 当前阶段建议只做盘点，不进入实现排期

## 4. 代码项目分布

按一级目录统计：

| 区域 | 项目数 | 判断 |
| --- | ---: | --- |
| `7_Business` | 179 | 业务与平台公共能力混杂，需细分 |
| `1_Framework` | 58 | 历史平台底层与公共层 |
| `9_Studio` | 30 | 当前阶段排除 |
| `3_ServiceApp` | 18 | 宿主与服务入口较多 |
| `2_WebApp` | 10 | Web UI 与 WebService 相关 |
| `8_WinApp` | 6 | 当前阶段排除 |
| `6_FileServer` | 6 | 当前阶段排除 |
| `4_TimerApp` | 3 | 当前阶段排除 |

### 4.1 Target Framework（目标框架）分布

按项目文件统计，旧平台存在明显的多代运行时并存：

| Framework | 数量 |
| --- | ---: |
| `net6;net48` | 235 |
| `v4.8` | 46 |
| `net6.0` | 8 |
| `netstandard2.0` | 3 |
| 其他混合组合 | 少量 |

补充观察：
- 仍可见 `v4.0`、`v3.5`、`netcoreapp3.1`、`net5.0`
- 这意味着旧平台带有明显跨代兼容包袱

## 5. 运行时元数据资产

这部分是旧平台最关键的非代码资产，应视为迁移重点对象。

统计结果：

| 扩展名 | 数量 |
| --- | ---: |
| `.generator` | 2588 |
| `.dic` | 2501 |
| `.page` | 1046 |
| `.schema` | 401 |
| `.model` | 399 |
| `.view` | 388 |

总量约 `7323` 个文件。

### 5.1 典型目录

- `Runtime\1_Server\Base\Config`
- `Runtime\1_Server\Base\Config\Dictionary`
- `Runtime\1_Server\CommandServiceManagement\Config`
- `Runtime\1_Server\CommandServiceManagement\Config\Dictionary`
- `Runtime\1_Server\CommandServiceManagement\Config\Enumeration`
- `Runtime\1_Server\CommandServiceManagement\Config\Generator`
- `Runtime\1_Server\CommandServiceManagement\Config\Model`
- `Runtime\1_Server\CommandServiceManagement\Config\Pages`
- `Runtime\1_Server\Example\Config`
- `Runtime\1_Server\Example\Config\Dictionary`
- `Runtime\1_Server\Example\Config\Enumeration`
- `Runtime\1_Server\Example\Config\Model`
- `Runtime\1_Server\Example\Config\Pages`
- `Runtime\1_Server\Config\Setting`

### 5.2 判断

- 旧平台明显依赖配置驱动、模型驱动和页面驱动机制
- 这些资产不能简单等同于普通源码文件
- V1 应优先产出“元数据迁移与兼容策略”，而不是直接全量重写

## 6. 页面与前端资产

按文件扩展统计：

| 类型 | 数量 |
| --- | ---: |
| `.js` | 1891 |
| `.aspx` | 31 |
| `.cshtml` | 18 |
| `.ts` | 8 |

### 6.1 判断

- 旧前端以 JS 资产为主，不是现代前端工程化结构
- 同时存在 Web Forms 页面和较新的 Razor 页面
- 当前阶段新平台前端应直接建立 React Shell（前端平台壳），不建议把旧页面直接混入新壳

### 6.2 典型页面线索

- `Runtime\1_Server\Default.aspx`
- `Runtime\1_Server\H5Default.aspx`
- `Runtime\1_Server\MobileDefault.aspx`
- `Runtime\1_Server\Registered\ResetPassword\IdentityVerification.aspx`
- `Runtime\1_Server\Platform\ReportMangement\aspx\ViewReport.aspx`
- `Source\3_ServiceApp\02_Core\Hoteam.InforCenter.WebHost.Console\Views\Home\Index.cshtml`

## 7. 与 V1 最相关的候选旧模块

### 7.1 Authorization（权限与授权）候选线索

- `Source\7_Business\15_ModulePermission\Hoteam.InforCenter.ModulePermission.Business`
- `Source\7_Business\15_ModulePermission\Hoteam.InforCenter.ModulePermission.DataLayer`
- `Source\7_Business\00_Utility\Hoteam.InforCenter.ModulePermission.Interface`
- `Source\7_Business\00_Utility\Hoteam.InforCenter.ModulePermission.Parameter`
- `Source\7_Business\27_ThreeRoleAuthPlatform\*`

判断：
- 权限与授权能力分散在 `ModulePermission` 与 `ThreeRoleAuthPlatform`
- 适合抽象出新平台统一授权模型，而不是原样迁移

### 7.2 ConfigCenter（配置中心）候选线索

- `Source\7_Business\12_SystemSettings\Hoteam.InforCenter.SystemSettings.Business`
- `Source\7_Business\12_SystemSettings\Hoteam.InforCenter.SystemSettings.Config`
- `Source\7_Business\12_SystemSettings\Hoteam.InforCenter.SystemSettings.Service`
- `Source\7_Business\09_Enumeration\*`
- `Runtime\1_Server\Base\Config\Dictionary`
- `Runtime\1_Server\Config\Setting`

判断：
- 配置、系统设置、字典、枚举能力分布广泛
- 适合 V1 先统一成 `ConfigCenter` 的最小模型，再做导入策略

### 7.3 Audit（审计）候选线索

- `Source\7_Business\05_OperateLog\Hoteam.InforCenter.OperateLog.Service`
- `Source\7_Business\05_OperateLog\Hoteam.InforCenter.OperateLog.Parameter`
- `Source\7_Business\00_Utility\Hoteam.InforCenter.OperateLog.Utility`

判断：
- 审计日志能力相对边界更清楚
- 适合作为 V1 首批模块之一

## 8. 高风险区域

当前识别到的高风险区：

- 多代 .NET 并存，迁移时容易混入旧框架心智
- 运行时元数据资产规模大，容易被低估
- `7_Business` 项目数量巨大，平台能力与业务能力耦合风险高
- 页面体系存在 Web Forms、Razor、旧 JS 并存
- 文件服务、Studio、WinApp 都属于独立复杂域，不适合混入 V1

## 9. V1 建议纳入范围

建议纳入：
- 新平台 Host / Kernel / Contracts / SDK（宿主、内核、契约、SDK）
- 新前端 Shell（前端平台壳）
- Demo Module（演示模块）
- Authorization（权限与授权）第一版
- ConfigCenter（配置中心）第一版
- Audit（审计）第一版
- 一类轻量旧资产导入或迁移 POC（概念验证）

轻量 POC 优先建议：
- 配置项
- 字典
- 枚举

## 10. V1 建议暂缓范围

建议暂缓：
- WinApp
- Studio
- 文件服务重写
- 深度文件迁移
- 工作流
- 报表与设计器
- 大规模旧页面兼容
- 大体量业务模块迁移

## 11. 下一步建议

建议基于本报告继续产出：

1. `OldP` 资产处置矩阵
2. V1 首批模块清单与边界说明
3. 元数据迁移与兼容策略
4. Demo Module 所需最小契约清单
