# ICP 新平台

NewP 是独立演进的平台基础能力与扩展内核仓库，采用模块化单体结构，提供 Host、Shell、Contracts、SDK、Kernel 与内建公共模块。

## 开源状态

本仓库原创代码采用 [MIT 许可证](LICENSE)，第三方依赖保留各自许可证。提交的数据库连接是无密码的本地示例，请使用自己的连接配置；英文 [README](README.md) 提供环境变量配置步骤。仓库根目录的 `.env` 不会被启动脚本自动解析。

## 当前事实源

1. 当前源码、配置和测试描述已经存在的能力。
2. 已确认的 GitHub issue/spec 与 ADR 描述目标和决策。
3. [AGENTS.md](AGENTS.md) 描述仓库工作边界；[docs/README.md](docs/README.md) 只负责把任务路由到仍有效的文档。

## 快速开始

首次拉取或 lockfile 变化后初始化：

```powershell
.\scripts\bootstrap.ps1
```

日常启动与停止：

```powershell
.\scripts\dev.ps1
.\scripts\stop-dev.ps1
```

默认地址：

- Host API：`http://localhost:5000`
- Frontend Shell：`http://localhost:3000`

本地开发优先读取 `NEWP_DEV_SQLSERVER_CONNECTION_STRING`；未设置时使用 `apps/host-api/appsettings.Development.json` 已配置的 `ConnectionStrings:SqlServer`，无需每次重复传入连接字符串。

全新数据库需要在停止开发服务后，通过交互式终端初始化本地管理员：

```powershell
dotnet run --project .\apps\host-api\HostApi.csproj -- identity bootstrap-local-admin
```

## 验证

```powershell
# 当前任务先选所属项目/包的定向测试；确需全仓检查的快速变体时使用：
.\scripts\verify.ps1 -Quick

# 交付约定或实际影响需要全仓检查时使用，不附带验收：
.\scripts\verify.ps1

# 只补本次行为需要的精确场景，例如文件夹编辑：
.\scripts\accept.ps1 -Scenario NativeWorkspaceFolderEdit_UsesFormalDriver
```

验证证据写入 `.codex-temp/verify/`，无需从截断的终端输出推断结果。

## 仓库结构

```text
apps/          Host 与应用入口
server/        Kernel、Contracts、SDK、平台能力与后端模块
frontend/      Shell、前端 SDK/UI、组件声明、metadata 与前端模块
tests/         单元、集成与 Acceptance 验证
docs/          当前文档入口、Context 与 ADR
scripts/       可复跑的开发和验证入口
```

新增内建模块优先使用：

```powershell
pwsh -NoProfile -File .\scripts\new-module.ps1 -ModuleName ExampleModule -DisplayName "示例模块"
```

模块身份、版本和依赖由 `<组件源码根>/newp.plugin.json` 拥有；metadata 拥有路由、菜单、权限引用和模型；后端与前端代码只绑定稳定 behavior key。完整边界见[组件化护栏](docs/architecture/componentization-guardrails.md)。
