# AI-First 执行标准 Phase 7 验证记录

## 1. 本轮目标

本轮用于收口 `WP2`，目标不是继续扩展新的 AI-first 机制，而是把已经存在的标准、脚本和设计创建入口接入当前协作入口与阶段验收入口，并补齐设计创建入口的最小健壮性验证。

本轮范围：

- 协作入口接入 `docs/architecture/ai-first-execution-standard.md`
- 后续工作包与阶段验收入口引用 AI-first 标准
- `new-design-doc.ps1` 增加模板预校验，避免无效模板被直接创建并登记
- `docs/current-status.md` 将 `WP2` 更新为已完成并收口

## 2. 本轮改动摘要

- 更新 `docs/collaboration/current-collaboration-mode.md`
- 更新 `docs/collaboration/github-copilot-working-mode.md`
- 更新 `docs/collaboration/post-phase-6-copilot-stage-map.md`
- 新增 `docs/collaboration/work-packages/wp2-ai-first-execution-standard-closeout.md`
- 更新 `docs/architecture/ai-first-execution-standard.md`
- 更新 `docs/current-status.md`
- 更新 `docs/README.md`
- 更新 `scripts/new-design-doc.ps1`
- 更新 `tests/Kernel.Tests/AiFirstDesignValidationScriptTests.cs`

## 3. 定向验证

### 3.1 TDD 红灯确认

先运行新增回归测试：

```powershell
dotnet test .\tests\Kernel.Tests\Kernel.Tests.csproj --filter FullyQualifiedName~AiFirstDesignValidationScriptTests.NewDesignDoc_FailsWhenTemplateDoesNotPassValidationAndDoesNotRegisterTarget -nr:false -v minimal
```

结果：

- 初次运行失败，确认当时 `new-design-doc.ps1` 允许无效模板继续创建并登记
- 补上模板预校验后再次运行通过

### 3.2 脚本级回归测试

执行：

```powershell
dotnet test .\tests\Kernel.Tests\Kernel.Tests.csproj --filter FullyQualifiedName~AiFirstDesignValidationScriptTests -nr:false -v minimal
```

结果：

- `14` 个测试全部通过
- 覆盖模板校验、显式目标清单、自动发现、登记去重、设计创建成功路径、无效模板拒绝路径

### 3.3 AI-first 受控目标检查

执行：

```powershell
powershell -NoProfile -ExecutionPolicy Bypass -File .\scripts\check-ai-first-design-targets.ps1
```

结果：

- 通过
- 当前受控基础集中的 `docs/designs/detailed-design-template.md`
  与 `docs/architecture/ai-first-execution-standard.md` 均校验通过

## 4. 结论

`WP2` 在本轮完成收口，当前仓库已具备以下最小可执行闭环：

1. 正式标准入口
2. 设计模板入口
3. 设计级校验入口
4. 受控目标门禁入口
5. 目标登记入口
6. 设计创建入口
7. 协作入口与阶段验收入口

## 5. 当前仍刻意保留在范围外的内容

- 不对既有全部设计文档做一次性回填
- 不把 AI-first 门禁扩成全仓文档治理
- 不在本轮展开 `WP3` 的大范围状态一致性整理
- 不把根测试脚本中的既存失败项误判为本轮回归
