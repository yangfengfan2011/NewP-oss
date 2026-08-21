---
status: accepted
---

# Standard CRUD 动作共用一个 Shell 执行生命周期

Standard CRUD 的远程业务动作由 Shell 内一个共享执行 Module 统一管理互斥、错误、数据刷新、冲突和不透明结果交接。模块 handler 只接收记录标识、详情或行快照以及可选业务输入，不再接收页面刷新回调；这使同一动作从列表或完整详情执行时具有相同生命周期，也让模块无法依赖宿主内部状态。

远程动作成功后，Shell 恰好刷新一次当前呈现面的数据，再把可选结果交给已注册 presenter。列表投影必须包含行级动作声明的 `dataDependency.fields`，保证模块收到完整的确认快照。`409` 表示确认所依据的快照已失效：Shell 刷新最新数据、关闭旧 Review Dialog 且不自动重放；其他失败不刷新并保留输入。带业务输入的动作只使用一个 Review Dialog 同时呈现确认文案和模块输入。标准删除 Adapter 与模块专用 handler 都通过这一执行 Module；`view`、`create`、页面 `refresh` 等本地页面动作不要求模块 handler。

未采用模块自选刷新、动作 metadata 失效计划或持久化 Operation artifact。当前人员与账号两个真实 adopter 的所有远程动作成功后都必须重新读取当前呈现面，而模块自选刷新已经造成列表、详情、删除和带输入动作之间的重复分支；增加静态失效协议或执行产物不会提高现有行为能力，只会重新扩大公共 Interface。该决定不改变业务命令、输入内容、错误翻译或结果 payload 的模块归属。
