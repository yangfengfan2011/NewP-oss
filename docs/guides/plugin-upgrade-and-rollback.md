# 插件升级与回滚指南

本文档说明如何安全地升级插件以及在出现问题时如何回滚。

## 升级前准备

在升级插件之前，建议完成以下准备工作：

1. **备份当前插件目录**

```powershell
# Windows
Copy-Item -Path "./modules/YourPlugin" -Destination "./modules/YourPlugin.backup" -Recurse

# Linux/macOS
cp -r ./modules/YourPlugin ./modules/YourPlugin.backup
```

2. **记录当前版本信息**

查看当前插件的 `plugin.json` 文件，记录：
- `version`：插件版本号
- `entryAssemblySha256`：入口程序集哈希值（如果有）

3. **检查宿主版本兼容性**

确认新版本插件的 `hostVersion` 要求与当前宿主版本兼容。

查看当前宿主版本：

```json
// appsettings.json
{
  "Platform": {
    "HostVersion": "1.0.0"
  }
}
```

确认新插件的版本要求：

```json
// 新插件的 plugin.json
{
  "hostVersion": {
    "min": "1.0.0",
    "max": "2.0.0"
  }
}
```

## 升级步骤

### 1. 停止宿主服务

确保宿主进程已完全停止，避免文件占用问题。

### 2. 替换插件文件

```powershell
# 删除旧版本
Remove-Item -Path "./modules/YourPlugin" -Recurse -Force

# 解压或复制新版本
Copy-Item -Path "./new-plugin-package/YourPlugin" -Destination "./modules/YourPlugin" -Recurse
```

### 3. 验证插件包结构

确认新插件目录包含必要文件：

```
modules/YourPlugin/
├── plugin.json          # 插件清单
├── YourPlugin.dll       # 入口程序集
└── [其他依赖文件]
```

### 4. 检查清单内容

验证 `plugin.json` 格式正确且包含必要字段：

```json
{
  "moduleId": "YourPlugin",
  "displayName": "Your Plugin",
  "version": "1.1.0",
  "entryAssembly": "YourPlugin.dll",
  "hostVersion": {
    "min": "1.0.0",
    "max": "2.0.0"
  },
  "entryAssemblySha256": "可选的哈希值"
}
```

### 5. 启动宿主并验证

启动宿主服务并检查日志输出：

```
Plugin discovery root: ./modules, host version: 1.0.0, loadable plugins: 6, skipped plugins: 0
Module: YourPlugin - Your Plugin (v1.1.0)
```

如果插件成功加载，日志中会显示新版本号。

## 升级失败排查

如果升级后插件未能成功加载，检查宿主日志中的失败信息：

### 场景 1：宿主版本不兼容

```
Skipped plugin package ./modules/YourPlugin: IncompatibleHostVersion - Plugin requires host version 2.0.0 to 3.0.0, but current host version is incompatible
```

**解决方案**：
- 回滚到旧版本插件，或
- 升级宿主版本以满足插件要求

### 场景 2：入口程序集哈希不匹配

```
Skipped plugin package ./modules/YourPlugin: EntryAssemblyHashMismatch - Entry assembly SHA-256 mismatch: expected 'abc123...', but got 'def456...'
```

**解决方案**：
- 确认插件包完整性，重新下载或复制
- 如果是自行构建的插件，更新 `plugin.json` 中的 `entryAssemblySha256` 字段

### 场景 3：入口程序集加载失败

```
Skipped plugin package ./modules/YourPlugin: EntryAssemblyLoadFailed - Failed to load entry assembly: Could not load file or assembly...
```

**解决方案**：
- 检查插件依赖的 DLL 是否完整
- 确认插件与宿主的 .NET 运行时版本兼容
- 检查是否缺少必要的 NuGet 包依赖

### 场景 4：清单文件格式错误

```
Skipped plugin package ./modules/YourPlugin: InvalidManifest - Error parsing plugin.json: Unexpected character...
```

**解决方案**：
- 使用 JSON 验证工具检查 `plugin.json` 格式
- 确认文件编码为 UTF-8
- 检查是否有多余的逗号或缺少引号

## 使用 DisabledPlugins 临时禁用插件

如果升级后的插件导致问题，可以在不删除文件的情况下临时禁用它：

编辑 `appsettings.json`：

```json
{
  "Platform": {
    "DisabledPlugins": ["YourPlugin"]
  }
}
```

重启宿主后，该插件会被跳过加载：

```
Skipped plugin package ./modules/YourPlugin: PluginDisabled - Plugin 'YourPlugin' is disabled by policy
```

这种方式适合：
- 快速禁用有问题的插件
- 在生产环境中临时关闭某个功能
- 测试插件对系统的影响

## 回滚步骤

如果升级后出现问题，可以回滚到之前的版本：

### 1. 停止宿主服务

### 2. 恢复备份

```powershell
# 删除有问题的新版本
Remove-Item -Path "./modules/YourPlugin" -Recurse -Force

# 恢复备份
Copy-Item -Path "./modules/YourPlugin.backup" -Destination "./modules/YourPlugin" -Recurse
```

### 3. 清理禁用配置

如果之前使用了 `DisabledPlugins`，记得从配置中移除：

```json
{
  "Platform": {
    "DisabledPlugins": []
  }
}
```

### 4. 重启宿主并验证

确认日志中显示旧版本已成功加载。

## 使用 hostVersion / SHA-256 / disabled 的最佳实践

### hostVersion 最佳实践

- **插件开发者**：合理设置版本范围，避免过于宽松或过于严格
  - 过宽（如 `"min": "0.0.1"`）可能导致运行时不兼容
  - 过严（如 `"min": "1.0.0", "max": "1.0.0"`）限制了灵活性
- **宿主管理员**：在 `appsettings.json` 中显式声明 `Platform:HostVersion`，避免依赖程序集版本

### SHA-256 最佳实践

- **开发阶段**：可以不声明 `entryAssemblySha256`，简化调试流程
- **生产环境**：建议声明哈希值，确保插件完整性
- **计算哈希值**：

```powershell
# Windows PowerShell
$hash = Get-FileHash -Path "./modules/YourPlugin/YourPlugin.dll" -Algorithm SHA256
$hash.Hash.ToLower()

# Linux/macOS
sha256sum ./modules/YourPlugin/YourPlugin.dll | awk '{print $1}'
```

将计算结果填入 `plugin.json`：

```json
{
  "entryAssemblySha256": "a1b2c3d4e5f6..."
}
```

### disabled 配置最佳实践

- **临时禁用**：使用 `DisabledPlugins` 配置，无需删除文件
- **永久移除**：直接删除插件目录
- **环境隔离**：不同环境（开发/测试/生产）可以配置不同的禁用列表

```json
// appsettings.Development.json
{
  "Platform": {
    "DisabledPlugins": ["ExperimentalPlugin"]
  }
}

// appsettings.Production.json
{
  "Platform": {
    "DisabledPlugins": []
  }
}
```

## 多插件批量升级

升级多个插件时，建议：

1. **逐个升级**：每次只升级一个插件，便于定位问题
2. **记录顺序**：如果插件之间有依赖关系，先升级被依赖的插件
3. **分批验证**：每升级一批插件后重启宿主验证，避免问题累积

## 生产环境升级建议

1. **在测试环境先验证**：确保新版本插件在测试环境正常运行
2. **选择低峰期**：在业务低峰期进行升级操作
3. **准备回滚预案**：确保可以快速回滚到旧版本
4. **监控日志**：升级后密切关注宿主日志和业务指标
5. **保留多个版本备份**：不仅保留上一个版本，建议保留最近 2-3 个稳定版本

## 注意事项

- 当前插件平台**不支持热更新**，所有升级操作都需要重启宿主
- 当前**没有远程插件市场**，所有插件包需要手动获取和部署
- 当前**没有自动化升级工具**，所有操作需要手动执行
- 插件升级可能涉及**数据库迁移**，注意检查插件的迁移脚本
- 当前**没有插件依赖自动解析**，需要手动确保依赖插件版本兼容
