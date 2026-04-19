# 元数据消费链一致性 Review（`.dic / .model / .settings`）

## 1. 背景与范围

本 review 只覆盖当前仓库里已经存在实现、且已经暴露出“消费端仍在猜目录/文件名/旧路径”的三条元数据链路：

- `.dic`
- `.model`
- `.settings`

本 review 的目标不是重新讨论 authoring 布局设计，也不是重开后台显示层主线，而是回答一个更直接的问题：

**当前已有消费端，是否仍然在依赖固定目录、文件名推导、单目录假设或旧路径兼容，从而与现有元数据 authoring 统一设计不一致。**

本 review 覆盖以下入口：

- `server/kernel/PluginPackages/LocalizationRuntimeManifestBuilder.cs`
- `server/kernel/PluginPackages/RuntimeViewMaterializer.cs`
- `server/modules/ConfigCenter/Persistence/Descriptors/ConfigCenterModelMetadataProvider.cs`
- `server/tools/dev-schema-sync/Models/ModelDiscoveryService.cs`
- `server/tools/dev-schema-sync/Models/SingleRootModelDocumentLoader.cs`
- `server/modules/MetadataConfigCenter/Services/MetadataManifestService.cs`
- `server/modules/MetadataConfigCenter/Services/SettingDefinitionAuthoringService.cs`
- `server/modules/MetadataConfigCenter/Services/SettingDefinitionApplyService.cs`
- `server/modules/MetadataConfigCenter/Adapters/SettingDefinitionAuthoringAdapter.cs`
- `server/modules/MetadataConfigCenter/Storage/ModuleMetadataStore.cs`

本 review 不覆盖：

- `nav / view` 元数据运行时装配  
  当前仓库里尚未发现 `.nav.json / .view.json` 的正式消费实现。
- `enum` 整体运行时改造
- 组织模块试点页面与 CRUD 实现
- 后端用户消息契约落地

## 2. 当前已对齐部分

先说明已经对齐、当前不建议重开的部分。

### 2.1 authoring 真源根目录

当前消费端普遍已经承认：

- 模块级 authoring 真源位于 `modules/<ModuleId>/metadata/**`
- runtime 投影视图位于 `plugins/runtime/modules/<ModuleId>/metadata/**`

这条大方向没有问题。

### 2.2 `MetadataManifestService` 已经具备按 `type` 扫描的基础能力

`MetadataManifestService` 当前会：

- 扫描 `metadata/**/*.json`
- 读取文件头中的：
  - `schemaVersion`
  - `type`
- 按平台 `file-type-mappings.json` 校验支持类型与 mergeMode

这说明“先按文件头识别类型，再决定消费链路”这条基础能力已经存在，不需要从零重做。

### 2.3 `.dic` 前端消费口径已经稳定

当前前端已稳定复用：

- `catalogs = 语言 -> flat key -> 文案`
- `t(key, fallback?)`

因此本轮不应重做前端 i18n 基座，只需要收口宿主 manifest 生成链路。

## 3. 现状与设计不一致点

### 3.1 `.dic`：builder 仍然主要依赖固定目录与文件名推导

当前 `LocalizationRuntimeManifestBuilder` 仍有四个不一致点：

1. 只扫描 `metadata/dictionaries/*.dic.json`
2. 没有把 `type = "dic"` 作为真正门禁
3. `dicKey` 仍主要由文件名推导
4. 同词条冲突仍然按“不同值即失败”处理，没有落实 `dic-entry` 覆盖规则

这条链的偏差已经在单独 review 中详细展开，这里不重复全文，只保留结论：

- `.dic` 当前仍未完全符合“目录只表达归属、类型由文件自描述、业务根对象提供语义、模块顺序决定覆盖”的统一规则

### 3.2 `.model`：消费端仍然明显绑定 `models/*.model.json`

这条链路的问题比 `.dic` 更直接，因为它现在不仅有固定扩展名依赖，还有固定目录依赖。

#### 3.2.1 `ConfigCenterModelMetadataProvider` 直接写死物理路径

`ConfigCenterModelMetadataProvider` 当前直接读取：

- `modules/ConfigCenter/metadata/models/ConfigItem.model.json`

问题在于：

- 这不是“按类型发现模型文件”，而是“按模块私有常量直读固定路径”
- 一旦模型 authoring 目录不再叫 `models/`，当前实现就直接失效
- `ModuleId = "ConfigCenter"` 也是在代码里硬编码回填，不是从统一定位链路中得到

#### 3.2.2 `dev-schema-sync` 仍把 `metadata/models` 当成结构前提

`ModelDiscoveryService` 当前只扫描：

- `*.model.json`

`SingleRootModelDocumentLoader` 当前还要求路径中必须出现：

- `modules/<ModuleId>/metadata/models/...`

它在提取 `moduleId` 时明确依赖了 `metadata/models` 这一级目录。

问题在于：

- 这与“目录可按类型，也可按领域分组，但类型判定以文件头为准”的设计不一致
- 它把 `models/` 从“当前常见组织方式”升级成了“硬语义前提”

#### 3.2.3 平台侧模型 loader 仍保留旧文档形状接口

`platform-modeling` 中的 `IModelMetadataDocumentLoader` / `JsonModelMetadataDocumentLoader` 仍然面向旧式 `ModelMetadataDocument` 直反序列化。

而当前仓库实际 authoring 方向已经是：

- 文件头：`schemaVersion + type`
- 业务根对象：单个模型对象

问题在于：

- 工具侧已经为单业务根对象补了 `SingleRootModelDocumentLoader`
- 平台建模侧却还没有把这套单根对象解析规则收成统一能力

这意味着：

- 当前 `.model` 的消费规则并没有真正收敛成一套共享内核
- 而是“模块里手写一份，工具里再手写一份”

### 3.3 `.settings`：manifest 层按 `type` 扫描，但读写/apply 仍然绑定单一 `SourcePath`

这是本次检查里最容易被忽略、但后续最容易再次出问题的一条。

#### 3.3.1 manifest 只保留一个 `SourcePath`

`MetadataManifestService` 在构建 `MetadataTypeDescriptor` 时，当前每种 type 只记录一个：

- `SourcePath`

尽管它同时也会累计：

- `SourceFiles`

但服务层真正往下游传递的主要仍然是单个 `SourcePath`。

这意味着：

- 同一种 metadata 若分布在多个目录
- 或后续目录从按类型组织切到按领域组织

当前 descriptor 的主消费路径就会先失真。

#### 3.3.2 `SettingDefinitionAuthoringService` 读取时只枚举单目录下的 `*.json`

`SettingDefinitionAuthoringService` 先取到：

- `settingsDescriptor.SourcePath`

然后只在该目录下枚举：

- `*.json`

问题在于：

- 这不是按 `SourceFiles` 精确定位文件
- 也不是按 `type = "settings"` 二次门禁
- 而是再次退回“目录里这些 json 应该都是 settings”

#### 3.3.3 `SettingDefinitionAuthoringAdapter` 没有把 `type = "settings"` 当作强门禁

当前 adapter 只检查：

- 顶层是否有 `settings` 对象

如果没有，就直接返回空集合。

这会带来两个问题：

1. 同目录混放其他元数据时，会被静默忽略，而不是显式失败
2. 调用方会误以为“当前没有 settings”，实际上可能是“当前扫到了错误类型文件”

#### 3.3.4 `SettingDefinitionApplyService` 也仍然按单目录整体替换

`ApplySettingDefinitionsAsync` 当前直接把：

- authoring 目录
- runtime 目录

按单目录整体比较和整体替换。

问题在于：

- 这仍然假设 settings 的 authoring/runtime 文件都聚集在一个目录下
- 一旦 `settings` 分散到多个目录，就无法只靠一个 `SourcePath` 完成 apply

### 3.4 `MetadataConfigCenter` 中的 runtime 读取优先与 runtime authoring 兜底仍然是过渡兼容

`ModuleMetadataStore` 当前存在两段明显的兼容逻辑：

1. 读取优先命中 runtime，再回退 repository
2. authoring 根不存在时，允许回退到 runtime 根

这条链不应和前三条问题混为一类。

原因是：

- 代码注释已经明确说明这是兼容兜底
- 它的目标是承接尚未完全回迁的历史数据

因此这里的判断是：

- **这是已知过渡残留，但不是本轮必须一并收口的主阻断点**
- 本轮只要不再继续扩大这种兼容逻辑即可

### 3.5 `nav / view` 当前还没有实现消费端，不存在“同类已落地偏差”

这次检查没有发现 `.nav.json / .view.json` 的正式消费实现。

因此当前不适合说：

- `nav / view` 也存在和 `.dic / .model / .settings` 一样的实现偏差

更准确的说法是：

- 它们还没进入实现阶段，所以还没有暴露出这类消费侧偏差

## 4. 共同问题本质

把三条链放在一起看，问题本质其实是同一类：

### 4.1 消费端仍在自己猜“文件在哪”

表现为：

- 猜固定目录名
- 猜固定扩展名
- 猜某个目录下所有 json 都是同一类型

### 4.2 消费端仍在自己猜“谁提供最终语义”

表现为：

- 用文件名推导 `dicKey`
- 用路径片段推导模型类型或模块归属
- 用目录归类替代文件头类型识别

### 4.3 消费端仍在自己猜“哪些文件参与生效”

表现为：

- 不使用显式模块顺序
- 不使用精确 `SourceFiles`
- 不把 merge 规则收口到代码内核

因此，这次收口不应该按“三个零碎 bug”来做，而应该作为一件统一工作来做：

**让消费端停止猜目录、猜文件名、猜生效顺序，改为只消费显式解析后的文件集合与类型语义。**

## 5. 下一步最小落地点

虽然三条链都要改，但仍应收成最小实现闭环，而不是顺手做 metadata 平台大重构。

### 5.1 `.dic` 最小落地点

1. 扫描 `modules/<ModuleId>/metadata/**/*.json`
2. 用文件头 `type = "dic"` 识别
3. 用单业务根对象确定 `dicKey`
4. 文件名只作为一致性校验
5. 用显式模块顺序执行 `dic-entry` 覆盖

### 5.2 `.model` 最小落地点

1. 把单业务根对象解析规则沉回共享 loader，而不是继续只存在于工具侧
2. 停止要求路径中必须出现 `metadata/models`
3. `moduleId` 只要求来自：
   - `modules/<ModuleId>/metadata/**`
4. 工具发现与模块消费都复用同一套单根对象 loader
5. 当前模块级 provider 不再把 `models/ConfigItem.model.json` 写死成长期语义前提

### 5.3 `.settings` 最小落地点

1. manifest 继续负责按 `type` 扫描
2. 下游 authoring/apply 服务改为优先消费：
   - `SourceFiles`
   而不是只消费单个 `SourcePath`
3. settings adapter 显式校验 `type = "settings"`
4. apply 阶段按精确文件集合投影，而不是假设单目录整体替换就足够

### 5.4 本轮不建议混入

1. `nav / view` 运行时装配
2. `enum` 运行时体系重写
3. `MetadataConfigCenter` 整体 UI 改造
4. 全量清除 runtime authoring 兼容兜底
5. 文件类型映射扩展到更多新类型

## 6. 验证方式

### 6.1 `.dic`

- `Kernel.Tests`
- 覆盖：
  - 非 `dictionaries/` 目录下的 dic 识别
  - `type = "dic"` 门禁
  - `dicKey` 来自业务根对象
  - 同词条按模块顺序覆盖
  - runtime/public 回滚

### 6.2 `.model`

- `ConfigCenter.Tests`
- `DevSchemaSync.Tests`
- 覆盖：
  - 非 `metadata/models` 目录下的 model 仍可被识别
  - 单业务根对象 loader 可同时服务工具与运行时消费
  - 文件名与业务根对象不一致时失败

### 6.3 `.settings`

- `MetadataConfigCenter.Tests`
- 覆盖：
  - settings adapter 对 `type = "settings"` 的硬门禁
  - 多文件 / 多目录 source files 读取
  - apply 阶段的精确文件集合投影
  - apply 失败回滚

## 7. 回滚思路

本轮回滚原则是：

- 只回滚三条消费链的定位与装配策略
- 不回滚 authoring 布局方向
- 不回滚前端 `catalogs + t(key)` 消费模型

### 7.1 回滚触发信号

1. `.dic` 收口后现有 manifest 词条大量丢失
2. `.model` 收口后 `dev-schema-sync` 或 `ConfigCenter` 模型投影失败
3. `.settings` 收口后 authoring/apply 无法识别现有文件
4. staging/commit 或 apply 回滚链路被破坏

### 7.2 回滚动作

1. 回退 `.dic / .model / .settings` 本轮消费端改动
2. 保留新增失败测试作为下一轮门禁
3. 保留本 review 与实施计划文档，避免后续再次回到“代码里各自猜路径”的状态

## 8. 结论

本次检查结论很明确：

1. `.dic` 的确存在已单独记账的不一致点
2. `.model` 也存在同类问题，而且当前固定目录依赖更明显
3. `.settings` 的问题不在 manifest 扫描层，而在下游 authoring/apply 仍然只认单 `SourcePath` 与弱类型门禁

因此，若决定“一次一起收”，合理的收法不是分别修三个点，而是把它们作为同一个目标推进：

**消费端不再自己猜目录、文件名和生效顺序，而是统一改为消费显式解析后的类型、业务根对象和精确文件集合。**

## 9. AI-First 最小检查清单

### 入口在哪里

- `server/kernel/PluginPackages/LocalizationRuntimeManifestBuilder.cs`
- `server/modules/ConfigCenter/Persistence/Descriptors/ConfigCenterModelMetadataProvider.cs`
- `server/tools/dev-schema-sync/Models/ModelDiscoveryService.cs`
- `server/tools/dev-schema-sync/Models/SingleRootModelDocumentLoader.cs`
- `server/modules/MetadataConfigCenter/Services/MetadataManifestService.cs`
- `server/modules/MetadataConfigCenter/Services/SettingDefinitionAuthoringService.cs`
- `server/modules/MetadataConfigCenter/Services/SettingDefinitionApplyService.cs`
- `server/modules/MetadataConfigCenter/Adapters/SettingDefinitionAuthoringAdapter.cs`

### 边界在哪里

- 本 review 只收口 `.dic / .model / .settings` 三条已实现消费链
- 不改 `nav / view` 主线设计
- 不改前端 `catalogs + t(key)` 口径
- 不重做 `enum` 体系

### 如何验证

- `.dic` 通过 `Kernel.Tests`
- `.model` 通过 `ConfigCenter.Tests` 与 `DevSchemaSync.Tests`
- `.settings` 通过 `MetadataConfigCenter.Tests`
- 额外执行一次宿主路由或 runtime smoke，确认 manifest/apply 链路未被破坏

### 失败信号是什么

- 消费端仍然依赖 `metadata/dictionaries`、`metadata/models` 或单一 `SourcePath`
- 仍然靠文件名或目录名提供最终业务语义
- 同类型文件分布到多个目录后，读取或 apply 立即失效
- 回滚链路被破坏

### 如何回滚

- 回退三条消费链的本轮实现
- 保留文档与新增测试
- 继续维持当前 authoring 布局设计，不回滚整体方向
