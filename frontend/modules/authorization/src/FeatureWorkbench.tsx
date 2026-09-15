import { useCallback, useEffect, useMemo, useRef, useState } from "react";
import { Alert, Button, Card, Checkbox, Flex, Input, List, Modal, Pagination, Space, Spin, Tag, Tree, Typography } from "antd";
import useMessage from "antd/es/message/useMessage";
import { platformFetch, useAuthorization } from "@icp-platform/core";
import type { PageProps } from "@icp-platform/sdk";
import { requestRole } from "./roleResource";
import { buildFeatureTree, changeBranch, featureDiff, featureTreeKeys, type FeaturePoint } from "./featureTree";

interface RoleSummary { id: string; code: string; displayName: string; isEnabled: boolean }
interface RoleState { role: RoleSummary; positions: string[] }
interface RolePage { items: { EID: string; ECODE: string; ENAME: string; IsEnabled: boolean }[]; total: number }
interface Catalog { points: FeaturePoint[] }
const endpoint = "/api/authorization/workbench";
const message = (error: unknown) => error instanceof Error ? error.message : "功能授权操作失败";

/** 功能授权的局部草稿 owner；所有写入由正式 API 判权，不在浏览器推断管理员放行。 */
export default function FeatureWorkbench({ input, reportState }: PageProps) {
  const { hasFeature } = useAuthorization();
  const [catalog, setCatalog] = useState<Catalog>();
  const [roles, setRoles] = useState<RolePage>();
  const [roleQuery, setRoleQuery] = useState("");
  const [page, setPage] = useState(1);
  const [current, setCurrent] = useState<RoleState>();
  const [draft, setDraft] = useState<Set<string>>(new Set());
  const [query, setQuery] = useState("");
  const [grantedOnly, setGrantedOnly] = useState(false);
  const [expanded, setExpanded] = useState<React.Key[]>([]);
  const [branch, setBranch] = useState<string>();
  const [error, setError] = useState("");
  const [messageApi, messageContextHolder] = useMessage();
  const [loading, setLoading] = useState(false);
  const [saving, setSaving] = useState(false);
  const [switchTarget, setSwitchTarget] = useState<string>();
  const loadRequest = useRef<AbortController>();
  const pending = useRef(false);
  const entryPosition = String(input.parameters.featurePosition ?? "");
  const saved = useMemo(() => new Set(current?.positions ?? []), [current]);
  const diff = useMemo(() => featureDiff(saved, draft), [saved, draft]);
  const dirty = diff.added.length + diff.removed.length > 0;
  const missingEntries = useMemo(() => {
    const dependencies = new Set((catalog?.points ?? []).filter(point => draft.has(point.position))
      .flatMap(point => point.entryDependencies).filter(position => !draft.has(position)));
    return (catalog?.points ?? []).filter(point => dependencies.has(point.position));
  }, [catalog, draft]);
  const savePoint = catalog?.points.find(point => point.behaviorKey === "authorization.workbench.save");
  const readOnly = !savePoint || !hasFeature(savePoint.position) || current?.role.code.toLowerCase() === "localadmin";
  const tree = useMemo(() => buildFeatureTree(catalog?.points ?? [], query, draft, grantedOnly), [catalog, query, draft, grantedOnly]);
  const read = useCallback((path: string, signal?: AbortSignal) => requestRole(platformFetch as typeof fetch, endpoint + path,
    { signal, headers: { "X-NewP-Feature": entryPosition } }), [entryPosition]);

  // 目录与角色查询分离；切换目标不重新抓取完整功能树。
  useEffect(() => {
    const controller = new AbortController();
    void read("/catalog", controller.signal).then(value => {
      if (!controller.signal.aborted) setCatalog(value as Catalog);
    }).catch(failure => { if (!controller.signal.aborted) setError(message(failure)); });
    return () => controller.abort();
  }, [read]);
  useEffect(() => {
    const controller = new AbortController();
    void read(`/roles?query=${encodeURIComponent(roleQuery)}&pageIndex=${page}&pageSize=20`, controller.signal)
      .then(value => { if (!controller.signal.aborted) setRoles(value as RolePage); })
      .catch(failure => { if (!controller.signal.aborted) setError(message(failure)); });
    return () => controller.abort();
  }, [read, roleQuery, page]);

  // 读取成功后才切换角色；过期响应与失败都不覆盖当前角色和草稿。
  const loadRole = useCallback(async (id: string) => {
    if (pending.current) return;
    loadRequest.current?.abort();
    const controller = new AbortController();
    loadRequest.current = controller;
    setLoading(true);
    setError("");
    try {
      const value = await read(`/roles/${encodeURIComponent(id)}`, controller.signal) as RoleState;
      if (!controller.signal.aborted) { setCurrent(value); setDraft(new Set(value.positions)); }
    } catch (failure) { if (!controller.signal.aborted) setError(message(failure)); }
    finally { if (!controller.signal.aborted) setLoading(false); }
  }, [read]);
  useEffect(() => {
    const id = input.parameters.roleId;
    if (typeof id === "string") void loadRole(id);
    return () => loadRequest.current?.abort();
  }, [input.parameters.roleId, loadRole]);
  const reset = useCallback(() => setDraft(new Set(current?.positions ?? [])), [current]);

  // 一次整组提交，失败保留草稿；网络不确定不重放，ref 同步阻止双击。
  const save = useCallback(async (): Promise<boolean> => {
    if (pending.current || readOnly || !current || !catalog || missingEntries.length > 0) return false;
    const action = catalog.points.find(point => point.behaviorKey === "authorization.workbench.save");
    if (!action) { setError("保存功能声明不可用"); return false; }
    pending.current = true;
    setSaving(true);
    setError("");
    try {
      await requestRole(platformFetch as typeof fetch, `${endpoint}/roles/${encodeURIComponent(current.role.id)}`, {
        method: "PUT", headers: { "Content-Type": "application/json", "X-NewP-Feature": action.position },
        body: JSON.stringify({ positions: [...draft] }),
      });
      setCurrent({ ...current, positions: [...draft] });
      void messageApi.success({ content: "授权已保存", duration: 3, key: "authorization-saved" });
      return true;
    } catch (failure) { setError(message(failure)); return false; }
    finally { pending.current = false; setSaving(false); }
  }, [catalog, current, draft, readOnly, missingEntries, messageApi]);
  useEffect(() => { reportState({ dirty, submitting: saving || loading }, reset, save); }, [dirty, saving, loading, reset, save, reportState]);
  const chooseRole = (id: string) => {
    if (pending.current || id === current?.role.id) return;
    if (dirty) setSwitchTarget(id); else void loadRole(id);
  };

  return <div style={{ padding: 24 }}>
    <Typography.Title level={3}>功能授权</Typography.Title>
    <Typography.Paragraph type="secondary">选择角色，按入口位置配置功能。保存一次提交该角色的完整授权集合。</Typography.Paragraph>
    {error && <Alert type="error" message={error} showIcon style={{ marginBottom: 16 }} />}
    {messageContextHolder}
    <Flex gap={20} align="stretch" wrap>
      <Card title="角色" style={{ width: 280, flexShrink: 0 }}>
        <Input.Search aria-label="搜索角色" placeholder="名称或代码" onSearch={value => { setRoleQuery(value); setPage(1); }} disabled={saving} />
        <List dataSource={roles?.items ?? []} loading={!roles} renderItem={role => <List.Item>
          <Button type={current?.role.id === role.EID ? "primary" : "text"} block disabled={saving} onClick={() => chooseRole(role.EID)}
            style={{ display: "block", height: "auto", minHeight: 32, whiteSpace: "normal", overflowWrap: "anywhere", textAlign: "left" }}>
            {role.ENAME} · {role.ECODE} {!role.IsEnabled && <Tag>停用</Tag>}
          </Button>
        </List.Item>} />
        <Pagination simple current={page} pageSize={20} total={roles?.total ?? 0} onChange={setPage} disabled={saving} />
      </Card>
      <Card style={{ flex: "1 1 460px", minWidth: 0 }} title={current ? `${current.role.displayName} · ${current.role.code}` : "选择一个角色"}>
        {!catalog ? <Spin /> : !current ? <Typography.Text type="secondary">从左侧选择角色以查看授权。</Typography.Text> : <Spin spinning={loading}>
          {!current.role.isEnabled && <Alert type="warning" message="此角色已停用，保留的授权当前不生效。" style={{ marginBottom: 16 }} />}
          {readOnly && <Alert type="info" message={current.role.code.toLowerCase() === "localadmin" ? "本地管理员授权由活动目录维护，只读。" : "当前账号可查看，未获保存授权。"} style={{ marginBottom: 16 }} />}
          <Space wrap style={{ marginBottom: 12 }}>
            <Input.Search aria-label="搜索功能" placeholder="功能名称或位置" value={query} onChange={event => setQuery(event.target.value)} />
            <Checkbox checked={grantedOnly} onChange={event => setGrantedOnly(event.target.checked)}>仅已授权</Checkbox>
            <Button onClick={() => setExpanded(featureTreeKeys(tree))}>展开全部</Button>
            <Button onClick={() => setExpanded([])}>收起全部</Button>
          </Space>
          <Space wrap style={{ marginBottom: 12 }}>
            <Button disabled={!branch || readOnly || saving} onClick={() => setDraft(changeBranch(catalog.points, draft, branch!, true))}>全选此分支</Button>
            <Button disabled={!branch || readOnly || saving} onClick={() => setDraft(changeBranch(catalog.points, draft, branch!, false))}>清除此分支</Button>
            <Typography.Text type="secondary">勾选独立生效；点击名称选定批量操作分支。</Typography.Text>
          </Space>
          <Tree checkable checkStrictly virtual height={420} treeData={tree} checkedKeys={[...draft]} expandedKeys={expanded}
            onExpand={setExpanded} onSelect={keys => setBranch(keys[0]?.toString())} disabled={readOnly || saving}
            onCheck={(_keys, info) => setDraft(previous => { const next = new Set(previous); if (info.checked) next.add(String(info.node.key)); else next.delete(String(info.node.key)); return next; })} />
          {missingEntries.length > 0 && <Alert type="warning" showIcon message={`请先补选入口：${missingEntries.map(point => point.title).join("、")}`} />}
          <Space wrap style={{ marginTop: 16 }}>
            <Typography.Text>已授权 {draft.size} / {catalog.points.length}</Typography.Text>
            <Tag color="green">新增 {diff.added.length}</Tag><Tag color="red">移除 {diff.removed.length}</Tag>
            <Button type="primary" disabled={!dirty || readOnly || loading || missingEntries.length > 0} loading={saving} onClick={() => void save()}>保存授权</Button>
            <Button disabled={!dirty || saving || loading} onClick={reset}>重置</Button>
          </Space>
        </Spin>}
      </Card>
    </Flex>
    <Modal open={!!switchTarget} title="当前角色有未保存的授权" closable={!saving} onCancel={() => setSwitchTarget(undefined)} footer={[
      <Button key="cancel" disabled={saving} onClick={() => setSwitchTarget(undefined)}>取消</Button>,
      <Button key="discard" disabled={saving} onClick={() => { const target = switchTarget!; setSwitchTarget(undefined); void loadRole(target); }}>放弃并切换</Button>,
      <Button key="save" type="primary" disabled={missingEntries.length > 0} loading={saving} onClick={async () => { if (await save()) { const target = switchTarget!; setSwitchTarget(undefined); await loadRole(target); } }}>保存并切换</Button>,
    ]}>保存失败会保留当前角色及草稿。</Modal>
  </div>;
}
