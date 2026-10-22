# ECODE 规范化且永久不复用

Identity 将 ECODE 输入 Trim 并执行 Unicode NFKC，结果必须为 3–64 个字符，且只包含 Unicode 字母、数字或 `.`、`_`、`-`、`@`，空格、控制字符和其他符号无效；该结果存入不可修改的 User.ECODE，再以 invariant uppercase 生成内部 NormalizedECODE。数据库在包含已删除墓碑的 User 集合上对 NormalizedECODE 建立唯一约束来处理并发冲突，账号删除也不会释放 ECODE。由于 User 墓碑永久保留，不再建立独立的账号保留实体或数据表，“登录账号保留”只是这一持久化不变量的领域名称。相比允许管理员修改或回收已删除账号的 ECODE，这一规则保持登录身份稳定并避免历史身份被新账号继承；格式错误返回字段级 validation ProblemDetails，创建时的 ECODE 冲突稳定映射为 `409 Conflict`。
