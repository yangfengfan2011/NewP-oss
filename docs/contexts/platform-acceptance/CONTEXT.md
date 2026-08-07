# Platform Acceptance

Platform Acceptance 上下文在受支持环境中编排真实用户旅程、故障注入与资源清理，并从正式观察面产生平台可用性和模块化闸门的唯一裁决。它不拥有 Host、Shell、业务规则、插件生命周期或数据库结构。

## Language

**验收闸门（Acceptance Gate）**：
一组必须同时成立的可观察条件，用于判定平台能力是否已达到可交付状态；单个测试、HTTP 成功或截图都不能独立通过闸门。
_Avoid_: smoke 成功、功能演示、测试全绿

**验收运行（Acceptance Run）**：
拥有唯一 run identity、隔离资源、事件流、证据包和终态裁决的一次完整执行。不同运行不得共享可变的进程、数据库、插件状态或 actors。
_Avoid_: 测试进程、CI job、开发会话

**验收场景（Acceptance Scenario）**：
在一个验收运行中验证单一成功旅程或失败语义的隔离路径，拥有明确前置状态、观察结果和 teardown。
_Avoid_: 测试方法、手工步骤、任意脚本片段

**观察事件（Observation Event）**：
由启动器、API、浏览器、插件工具或数据库只读观察面产生的结构化事实。事件可以推动裁决状态机，但不能自行声明闸门通过。
_Avoid_: 日志行、断言通过、状态写入命令

**验收裁决（Acceptance Verdict）**：
裁决器根据完整必需事件、证据完整性和归属安全清理产生的终态结果。缺失、跳过、观察器故障或 teardown 失败都不能得到通过。
_Avoid_: 进程退出码、测试框架汇总、人工确认

**证据包（Evidence Bundle）**：
与验收运行绑定、可由机器追溯的脱敏产物集合，包含事件、结果、关键请求响应、浏览器证据、日志和资源身份。
_Avoid_: 截图文件夹、完整日志转储、凭据目录

**验收 Fixture（Acceptance Fixture）**：
只在显式验收模式中通过受限应用接口收敛 actors 与最小前置数据的适配器；它不能直接写业务表，也不能参与被测行为的成功断言。
_Avoid_: 开发种子、生产 bootstrap、数据库脚本

**归属安全清理（Ownership-safe Teardown）**：
只对能再次证明属于当前验收运行的进程、临时文件和 disposable catalog 执行的收尾动作。归属不确定时必须保留资源并使裁决失败。
_Avoid_: 端口清扫、按裸 PID 终止、自动删除未知数据库
