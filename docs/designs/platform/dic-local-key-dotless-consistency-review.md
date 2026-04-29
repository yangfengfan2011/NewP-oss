# dic localKey 去点号一致性 Review

## 1. Review 结论

本次修正与 `dic 与 Localization Manifest 一致性设计`、`用户可见文本多语言底层设计` 保持一致，可以进入受控实现。

## 2. 设计一致性

- `dicKey` 继续来自 dic 文件唯一业务根对象，例如 `PersonListView`。
- `localKey` 只表达业务词条，不再承载路径式层级，因此不允许包含 `.`。
- 运行时 `localization manifest` 仍保持 `dicKey.localKey -> 文案` 的扁平 catalog 形态。
- 前端 `LocalizationProvider` 与 `t(key, fallback?)` 不新增第二套查询入口。

## 3. 范围边界

本次只允许修改：

- `modules/**/metadata/dictionaries/*.dic.json` 中含点号的 dic entry key。
- 引用这些 dic entry 的 view/model metadata 文案 key。
- `LocalizationRuntimeManifestBuilder` 的校验与对应测试。
- 与上述 key 形态直接绑定的前端/后端测试夹具。

本次不修改：

- `LocalizationProvider` 查询模型。
- `languages.json` 结构。
- `enum` authoring 结构。
- Shell/Host 的模块加载边界。

## 4. 验证要求

- 先通过测试证明含点号 dic entry 当前不会被拒绝。
- 新增并运行 `scripts/validate-dic-localization.ps1`，在静态校验阶段提前拒绝含点号 localKey。
- 实现后运行 `dotnet test .\tests\Kernel.Tests\Kernel.Tests.csproj --filter FullyQualifiedName~LocalizationRuntimeManifestBuilderTests -nr:false -v minimal`。
- 视影响面补跑 runtime materializer 与前端组织页面相关测试。

## 5. 风险与回滚

风险是历史测试夹具或过渡资源仍使用 `Common.Add` 这类旧 key，实施时必须同步改为 `Add` 并确认最终 manifest 仍输出 `Common.Add`。

回滚方式是撤回本次 key 重命名、builder 校验和测试修改；运行时 manifest 会恢复接受历史含点号 entry，但也会恢复多段点号 lookup 风险。

## 6. AI-First 最小检查清单

1. 入口在哪里  
`LocalizationRuntimeManifestBuilder`、`modules/**/metadata/dictionaries/*.dic.json`、引用 dic 文案的 view/model metadata。

2. 边界在哪里  
只处理 dic localKey 去点号、对应 metadata 引用和测试夹具，不改变 `LocalizationProvider`、`languages.json`、Host/Shell 装载模型。

3. 如何验证  
运行设计校验、`validate-dic-localization.ps1`、`LocalizationRuntimeManifestBuilderTests`、`RuntimeViewMaterializerTests`、相关组织页面 vitest。

4. 失败信号是什么  
静态脚本未拒绝含点号 dic entry、builder 未拒绝含点号 entry、运行时 manifest 缺少预期 `dicKey.localKey`、页面显示 fallback key。

5. 如何回滚  
回退 key 重命名、builder 校验与测试修改；必要时暂时恢复接受旧含点号 entry 的兼容行为。
