# WP4 Stage 10 Enumeration 静态结构清单与本地化资源转换验证记录

## 1. 本轮目标

本轮用于完成 `Stage 10` 的最小可执行闭环：

- 将 `Enumerations.enum` 与 `Enumerations.dic` 转换为受控静态枚举结构清单
- 将启用语言集合通过 `resources/localization/languages.json` 过滤后输出为枚举本地化资源
- 明确 `.enum` 与 `.dic` 不完全对齐时的受控告警与跳过规则

## 2. 本轮改动摘要

- 新增 `scripts/migration/enumeration-to-static-resources.ps1`
- 新增 `tests/Kernel.Tests/EnumerationToStaticResourcesScriptTests.cs`
- 生成 `resources/enumerations/*.json`
- 生成 `resources/localization/{zhs,en}/enumerations/*.json`

## 3. 定向验证

### 3.1 脚本级回归测试

执行：

```powershell
dotnet test .\tests\Kernel.Tests\Kernel.Tests.csproj --filter FullyQualifiedName~EnumerationToStaticResourcesScriptTests -nr:false -v minimal
```

结果：

- 通过
- `1` 个测试通过
- 已覆盖：结构输出、启用语言过滤、孤立翻译跳过告警、缺翻译不阻塞结构落地

### 3.2 真实冻结样本转换

执行：

```powershell
powershell -NoProfile -ExecutionPolicy Bypass -Command "& '.\scripts\migration\enumeration-to-static-resources.ps1' -EnumerationPath 'D:\Github\ICP-X\OldP\Runtime\1_Server\Platform\Config\Enumeration\Enumerations.enum' -DictionaryPath 'D:\Github\ICP-X\OldP\Runtime\1_Server\Platform\Config\Enumeration\Enumerations.dic' -OutputRoot '.\resources' -LanguagesConfigPath '.\resources\localization\languages.json'"
```

结果：

- 通过
- 输出 `32` 个静态枚举结构文件
- 输出 `62` 个本地化文件（`zhs/en` 各 `31` 个）
- 产生 `14` 条受控 warning：
  - `SelectMode.MULTI/NULL/SINGLE` 缺少翻译，结构已生成，消费侧应回退到 `ItemName`
  - `HomePageModeDefaultHomePage`、`HomePageModeSimpleHomePage`、`AcademicRecordBachelor` 等孤立翻译标签已跳过

### 3.3 资源结果 spot check

执行：

```powershell
powershell -NoProfile -Command "$structureCount = (Get-ChildItem '.\resources\enumerations' -Filter *.json -File | Measure-Object).Count; $zhsCount = (Get-ChildItem '.\resources\localization\zhs\enumerations' -Filter *.json -File | Measure-Object).Count; $enCount = (Get-ChildItem '.\resources\localization\en\enumerations' -Filter *.json -File | Measure-Object).Count; $gender = ((Get-Content '.\resources\localization\zhs\enumerations\Gender.json' -Raw | ConvertFrom-Json).'Gender.Female'); $selectModeExists = Test-Path '.\resources\localization\zhs\enumerations\SelectMode.json'; ""StructureCount=$structureCount""; ""ZhsCount=$zhsCount""; ""EnCount=$enCount""; ""GenderFemale=$gender""; ""SelectModeLocalizationExists=$selectModeExists"""
```

结果：

- `StructureCount=32`
- `ZhsCount=31`
- `EnCount=31`
- `GenderFemale=女`
- `SelectModeLocalizationExists=False`

## 4. 结论

`Stage 10` 已完成并通过，当前已具备：

1. 受控静态枚举结构清单转换脚本
2. 受控枚举本地化资源转换脚本
3. `languages.json` 驱动的启用语言过滤
4. `.enum` 与 `.dic` 不完全对齐时的可复跑 warning 行为

## 5. 当前边界

- 本轮只处理静态 `.enum` + `.dic`，未处理 DB Enumeration
- 本轮只完成静态结构清单与本地化资源落地，未新增运行时查询 API
- 本轮未补枚举消费层 UI 或运行时投影，消费侧如需使用应从静态清单/本地化资源继续接入

## 6. 下一步建议

- `WP4` 当前冻结范围已形成 `Dictionary + Enumeration` 双闭环；如继续推进，建议单独拆包处理枚举消费接入或下一批样本扩展
