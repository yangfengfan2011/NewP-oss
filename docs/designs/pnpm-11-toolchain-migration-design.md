# pnpm 11 工具链迁移设计

## 1. 背景

仓库当前存在两套不一致的 pnpm 获取方式：

- CI 固定使用 pnpm 9 和 Node.js 20
- `scripts/bootstrap.ps1` 与 `scripts/bootstrap.sh` 在本机缺少 pnpm 时安装未固定版本的最新版

2026-08-07 的本机初始化因此安装了 pnpm 11.20.0。pnpm 11 默认拒绝未审核的依赖构建脚本，`esbuild@0.21.5` 被阻止后，`scripts/dev.ps1` 的前端依赖准备步骤以非零退出码失败。当前任务进一步确认工具链基线为 Node.js 24.19.0 与 pnpm 11.20.0，二者都必须精确匹配。

## 2. 决策

- 将前端工具链基线精确固定为 Node.js 24.19.0
- 将仓库使用的 pnpm 精确固定为 11.20.0
- 在 `frontend/package.json` 声明 `packageManager` 与 `devEngines.runtime`，分别作为 pnpm 与 Node.js 版本事实源
- 本地初始化和开发启动脚本读取上述事实源并精确校验版本，避免环境静默漂移
- 本地入口通过 Corepack 在 `frontend` 工程上下文中运行 `packageManager` 指定的 pnpm，不写入全局 pnpm
- CI 使用支持 pnpm 11 的 `pnpm/setup@v2`，从同一个 `frontend/package.json` 安装 pnpm 与 Node.js
- 在 `frontend/pnpm-workspace.yaml` 中显式允许 `esbuild` 执行构建脚本
- 将 `shamefully-hoist` 从 pnpm 11 不再承载常规项目设置的 `.npmrc` 迁移到 `pnpm-workspace.yaml`

## 3. 边界与非目标

- 不升级任何前端业务依赖
- 不迁移到仍处于测试阶段的 pnpm 12
- 不允许所有依赖任意执行构建脚本，只允许当前已确认需要构建的 `esbuild`
- 不通过 `npm install -g`、`corepack enable` 或其他方式修改全局 pnpm 命令
- 不改变 Host API、Shell 或模块装配边界
- 不修改开发服务端口、日志和 PID 生命周期

## 4. 入口与失败信号

入口保持不变：

- Windows 初始化：`scripts/bootstrap.ps1`
- POSIX 初始化：`scripts/bootstrap.sh`
- Windows 启动：`scripts/dev.ps1`
- POSIX 启动：`scripts/dev.sh`
- CI：`.github/workflows/ci.yml`

以下任一情况视为迁移失败：

- Node.js 不是 24.19.0 时脚本继续安装或启动
- pnpm 版本不是 11.20.0 时开发脚本继续启动
- Corepack 不可用时脚本静默安装或切换全局 pnpm
- `pnpm install --frozen-lockfile` 返回 `ERR_PNPM_IGNORED_BUILDS`
- `pnpm ignored-builds` 仍列出 `esbuild`
- 前端生产构建或真实开发启动失败
- CI 与本地脚本没有读取同一个 pnpm 版本事实源

## 5. 验收与验证

至少执行：

1. 校验 `frontend/package.json` 声明 Node.js 24.19.0 与 pnpm 11.20.0
2. 在 `frontend` 中使用 `corepack pnpm --version`，确认解析为 11.20.0
3. 使用 `corepack pnpm install --frozen-lockfile`，确认 `esbuild` 构建脚本获准执行
4. 执行 `corepack pnpm --filter @icp-platform/shell build`
5. 执行 `scripts/dev.ps1`，确认工具链门禁通过后 Host API 与前端均就绪
6. 执行 `scripts/stop-dev.ps1`，确认验证进程和监听端口已释放
7. 检查任务范围差异与所有编辑文件的行尾

## 6. 回滚

若 pnpm 11 迁移无法通过验收：

1. 恢复 CI 的 Node.js 20 与 pnpm 9
2. 恢复初始化和启动脚本的旧版本要求
3. 移除 `packageManager`、`allowBuilds` 和迁移后的 `shamefullyHoist`
4. 恢复 `frontend/.npmrc`
5. 使用 pnpm 9 重新安装前端依赖并复跑构建与开发启动验证
