# 本地开发脚本健壮性增强设计

## 1. 背景

当前仓库使用本地开发脚本同时启动：

- `apps/host-api`
- `frontend/packages/shell`

当前入口包括：

- `scripts/dev.ps1`
- `scripts/dev.sh`

近期本机多次出现以下问题：

- 本机已安装 `node`，但没有可直接执行的 `pnpm`
- `frontend/node_modules` 尚未安装，导致 `vite` 不存在
- 首次后端冷启动时间超过 60 秒，脚本误判启动失败
- 启动失败时只提示 “Check the logs”，开发者需要手动翻找 `.codex-temp` 日志才能定位问题

这些问题会反复阻塞本地联调，不符合当前项目“可验证、可复制、适合 AI 辅助开发”的目标。

## 2. 目标

- 让 `scripts/dev.ps1` 在启动前自动完成前端依赖检查
- 在可自动修复的场景下，直接修复而不是立即失败
- 在启动失败时直接输出关键日志尾部，降低排障成本
- 降低首次冷启动场景下的误报

## 3. 非目标

- 不引入新的全局安装策略或系统级依赖管理
- 不修改前端构建体系或 workspace 包发布策略
- 不接管 Docker / 数据库容器启动

## 4. 现状问题

### 4.1 前端依赖前置条件不透明

当前脚本只检查 `pnpm` 命令是否存在，但不会检查：

- `frontend/node_modules` 是否存在
- `vite` 是否可执行

结果是：

- 即使 `pnpm` 存在，前端依赖未安装时仍会在后半段失败
- 失败信号落在前端日志，而非脚本主输出

### 4.2 冷启动等待时间过短

当前后端和前端都只等待 60 秒：

- 后端第一次编译或首次数据库初始化时可能超过 60 秒
- 脚本会在服务真实可用前退出

### 4.3 失败提示不够直接

当前失败时仅输出：

- `One or more services did not become ready in time. Check the logs.`

开发者需要再手动打开：

- `.codex-temp/dev-host-api.out.log`
- `.codex-temp/dev-host-api.err.log`
- `.codex-temp/dev-frontend.out.log`
- `.codex-temp/dev-frontend.err.log`

## 5. 方案

### 5.1 新增前端依赖自检与自动安装

在启动 Host API 之前增加前端依赖准备步骤：

1. 检查 `frontend/node_modules` 是否存在
2. 检查 `vite` 是否可从 `frontend` workspace 执行
3. 如果任一检查失败，则自动执行：

```powershell
pnpm -C frontend install
```

这样可以覆盖：

- 首次克隆仓库后的本机
- 清理过 `node_modules` 的本机
- 只有 `pnpm` 但没有前端依赖的本机

### 5.2 增加统一日志尾部输出函数

新增内部辅助函数，在启动失败时自动打印日志尾部：

- Host API 标准输出尾部
- Host API 错误输出尾部
- Frontend 标准输出尾部
- Frontend 错误输出尾部

默认尾部行数控制在一个较小窗口，避免输出过长。

### 5.3 增加更宽松的启动等待时间

将启动等待时间调整为：

- 后端：`120s`
- 前端：`90s`

理由：

- 后端首次编译、数据库连接和种子初始化成本更高
- 前端在依赖已安装后一般较快，但也要预留依赖预优化时间

### 5.4 明确失败原因

在以下场景输出更明确的文字：

- 缺失 `dotnet`
- 缺失 `node`
- 缺失 `pnpm`
- 前端依赖检查失败且自动安装失败
- 后端未在时限内 ready
- 前端未在时限内 ready

### 5.5 同步到 `dev.sh`

为了避免不同平台脚本行为分叉，同样的增强策略需要同步到 `scripts/dev.sh`：

- 启动前自动检查前端依赖
- 后端与前端等待时间放宽
- 启动失败时输出关键日志尾部
- 对前端子进程提前退出给出明确提示

### 5.6 保持现有行为边界

以下行为保持不变：

- 仍由 `scripts/stop-dev.ps1` 停止服务
- 仍由 `scripts/stop-dev.sh` 停止服务
- 仍使用 `.codex-temp` 写日志与 PID 文件
- 仍以 `5000/3000` 作为默认端口
- 失败时不自动删除日志

## 6. 影响文件

- `docs/designs/dev-script-resilience-design.md`
- `scripts/dev.ps1`
- `scripts/dev.sh`

## 7. 验证方式

至少验证以下场景：

1. `pnpm` 存在且 `frontend/node_modules` 已安装时，脚本能正常启动
2. 删除 `frontend/node_modules` 后，脚本能自动执行前端依赖安装
3. 人为制造前端启动失败时，脚本会输出前端日志尾部
4. 后端和前端均成功启动时，脚本退出码为 `0`

## 8. 失败信号

若出现以下任一情况，说明实现不满足预期：

- `frontend/node_modules` 缺失时仍直接因为 `vite` 不存在而失败
- 脚本失败后没有输出关键日志尾部
- 正常启动情况下脚本引入新的端口占用或误杀行为

## 9. 回滚思路

如需回滚：

1. 恢复 `scripts/dev.ps1` 到增强前版本
2. 保留新增设计文档，作为后续重做参考
