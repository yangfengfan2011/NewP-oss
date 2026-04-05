# Setting 导入 POC 迁移文档

## 1. 概述

本文档记录 Phase 5 首个旧资产迁移 POC 的实际执行情况，迁移对象为旧平台 `.setting` 文件，目标承载模块为 `ConfigCenter`。

## 2. 迁移对象

### 2.1 样本来源

- **真实样本文件**：`OldP/Runtime/1_Server/Config/CompanyRuleSetting.setting`
- **文件类型**：XML 格式的旧平台配置文件
- **核心结构**：`root` -> `AppSet` 节点集合
- **关键字段**：`Key`、`Value`、`ToClient`、`Description`

### 2.2 样本选择理由

- 结构简单，字段稳定
- 不强依赖旧运行时上下文
- 配置项数量适中，适合作为机制验证样本
- 与 `ConfigCenter` 定位直接吻合

## 3. 映射规则

### 3.1 字段映射

| 旧字段 | 新字段 | 映射规则 |
|--------|--------|----------|
| `AppSet.Key` | `ConfigItem.Key` | 直接映射 |
| `AppSet.Value` | `ConfigItem.Value` | 直接映射 |
| `AppSet.Description` | `ConfigItem.Description` | 直接映射 |
| 文件名 | `ConfigItem.Group` | 从文件名提取（如 `CompanyRuleSetting`） |
| 固定值 | `ConfigItem.ModuleId` | 映射为 `ConfigCenter` |
| `AppSet.Key` | `ConfigItem.DisplayName` | 使用 Key 作为显示名称 |

### 3.2 覆盖策略

- 同一 `Key` 视为同一目标配置
- 重复导入时，后导入覆盖先导入
- 不做复杂合并，保持简单幂等

## 4. 导入路径

### 4.1 导入入口

- **API 端点**：`POST /api/config/import/setting`
- **请求方式**：JSON Body
- **请求字段**：`moduleId`、`sourceFileName`、`xmlContent`
- **执行方式**：受控导入，非自动扫描

示例：

```json
{
  "moduleId": "ConfigCenter",
  "sourceFileName": "CompanyRuleSetting.setting",
  "xmlContent": "<root>...</root>"
}
```

### 4.2 导入流程

1. 读取旧 `.setting` 文件内容并转为 XML 字符串
2. 解析 XML 结构，提取 `AppSet` 节点
3. 应用字段映射规则，生成 `ConfigItem` 集合
4. 执行导入前校验
5. 写入 `ConfigCenter` 仓储
6. 返回导入结果

### 4.3 错误处理

- XML 格式不合法：返回解析错误
- 必填字段缺失：跳过该配置项，记录警告
- 键冲突：按覆盖策略处理
- 部分失败：返回成功和失败的详细列表

## 5. 验证方式

### 5.1 后端验证

- HostApi 构建成功
- ConfigCenter 相关测试通过 14 个测试用例
- 导入 API `POST /api/config/import/setting` 可正常响应

### 5.2 功能验证

- 导入后可通过 `GET /api/config/{key}` 查询单个配置
- 可通过 `GET /api/config/grouped` 查询分组结果
- 可通过 `OtherCompanyCanOperate` 查询到对应配置项
- 可通过 `ConfigCenter / CompanyRuleSetting` 分组查询到 2 个配置项

### 5.3 前端承接情况

- 现有前端配置页消费 `GET /api/config/grouped`，理论上具备展示导入结果的承接能力
- 本轮实际完成的是后端/API 级 smoke，未单独做浏览器级人工验证

## 6. 当前限制

### 6.1 已知限制

- **中文 Description 编码不稳定**：中文 Description 在当前脚本化 smoke 中存在编码不稳定风险。
- 仅支持单文件导入，暂不支持批量导入
- 不支持环境或租户级别的配置隔离
- 覆盖策略较简单，不支持增量合并
- 未完整接入 `Audit` 模块记录导入操作

### 6.2 不支持的场景

- 复杂引用链或脚本逻辑的配置
- 需要运行时动态计算的配置
- 强依赖旧平台上下文的配置

## 7. 后续扩展方向

### 7.1 短期扩展

- 加强中文编码处理，解决 Description 字段的编码不稳定问题
- 支持批量导入多个 `.setting` 文件
- 增强导入报告，提供更详细的成功/失败统计
- 接入 `Audit` 模块，记录导入操作历史

### 7.2 中期扩展

- 支持 `Dictionary` 和 `Enumeration` 的导入
- 支持环境和租户级别的配置隔离
- 支持更复杂的覆盖和合并策略
- 提供导入预览和回滚能力

### 7.3 长期扩展

- 支持更复杂的旧资产类型（如 `.page`、`.view`）
- 建立完整的迁移平台和迁移工具链
- 为 `Icp Studio` Web 化提供配置维护能力

## 8. AI-First 对齐

本次 POC 的核心输出不仅是一个可运行的导入功能，更重要的是沉淀了一条 **AI 可复制执行的迁移模板**：

- **样本冻结**：明确使用 `CompanyRuleSetting.setting` 作为首个样本
- **规则冻结**：字段映射规则已固化在本文档中
- **执行冻结**：导入入口为固定 API `POST /api/config/import/setting`，请求体为 `moduleId/sourceFileName/xmlContent`
- **验证冻结**：验证步骤已记录在验证文档中
- **复用冻结**：本模板可直接复用于 `Dictionary`、`Enumeration` 等后续迁移对象

后续 AI agent 可以按照本文档的模板，独立完成同类资产的迁移设计和实施。

## 9. 参考文档

- [Phase 5 首个旧资产迁移 POC 设计](../designs/phase-5-first-import-poc-design.md)
- [Setting 导入 POC 验证记录](../verification/setting-import-poc-verification.md)
- [ConfigCenter 详细设计](../designs/configcenter-design.md)
- [元数据迁移策略](metadata-migration-strategy.md)
