# Organization 模块

Organization 拥有公司、部门、岗位、人员档案，以及 Person 与 Identity User 的永久一对一绑定。

`OrganizationModule` 当前注册：

- 公司、部门、岗位和人员绑定的 EF repositories 与应用服务；
- Person 标准 CRUD descriptor、查询定制、展示值和摘要 provider；
- 人员生命周期、账号绑定、删除保护和条件删除执行；
- Identity/Authentication/Authorization 的稳定契约协作 seam。

`frontend/modules/organization/metadata` 是模型与静态 authoring 真源，`Migrations.SqlServer` 推进模块自有迁移流。边界和术语见 `docs/contexts/organization/CONTEXT.md`，验证以 `tests/Organization.Tests` 与 Personnel Acceptance Gate 为准。
