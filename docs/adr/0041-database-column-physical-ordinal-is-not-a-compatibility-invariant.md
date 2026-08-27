---
status: accepted
---

# 数据库物理列 ordinal 不是兼容性不变量

Model metadata 与 canonical Provider Target/Transition 的字段顺序继续服务 authoring、契约、逻辑投影和版本化摘要，但 SQL Server 通过合法 `ALTER TABLE ADD` 追加新列时会独立决定数据库物理列位置。Platform Persistence 因此按稳定列名配对数据库列，再严格核对 provider type、nullability、长度、精度、scale、collation、computed/stored、identity、rowversion、default 与其他已投影 provider 语义；数据库物理 column ordinal 不参与兼容性判断，也不单独构成 Schema Drift。

## Consequences

- Provider Target/Transition v1 的逻辑列顺序、schema version 与 digest 保持不变，既有 migration history 不重算也不改写。
- 合法追加列不触发表重建、列重排或任何修复 DDL；Host 仍是唯一 schema 写入入口。
- 缺失、额外、重名或同名但结构 facet 不同的列仍是 drift；key、foreign key、index 与 constraint 内部有语义的列顺序仍严格比较。
