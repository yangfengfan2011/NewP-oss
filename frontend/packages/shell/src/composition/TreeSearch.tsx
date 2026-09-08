import { useEffect, useState } from 'react';
import { useLocalization } from '@icp-platform/core';
import type { TreeProviderBinding, TreeView } from '@icp-platform/sdk';

type SearchItem = Awaited<ReturnType<TreeProviderBinding['search']>>['items'][number];
function Highlight({ text, keyword }: { text: string; keyword: string }) {
  const index = text.toLocaleLowerCase().indexOf(keyword.toLocaleLowerCase());
  return index < 0 ? <>{text}</> : <>{text.slice(0, index)}<mark>{text.slice(index, index + keyword.length)}</mark>{text.slice(index + keyword.length)}</>;
}

/** 结果只展示搜索时路径；选择交给宿主重新读取当前位置。 */
export function TreeSearch({ provider, scope, view, choose }: {
  provider: TreeProviderBinding; scope: Record<string, unknown>; view: TreeView; choose: (key: string) => void;
}) {
  const { t } = useLocalization();
  const [keyword, setKeyword] = useState('');
  const [pagination, setPagination] = useState({ provider, scope, page: 1 });
  const page = pagination.provider === provider && pagination.scope === scope ? pagination.page : 1;
  const setPage = (value: number) => setPagination({ provider, scope, page: value });
  const [opened, setOpened] = useState(false);
  const [items, setItems] = useState<SearchItem[]>([]);
  const [total, setTotal] = useState(0);
  const [loading, setLoading] = useState(false);
  const [error, setError] = useState<string | null>(null);
  const [retry, setRetry] = useState(0);
  useEffect(() => { setItems([]); }, [provider, scope]);
  useEffect(() => {
    if (!keyword.trim() || !opened) return;
    const controller = new AbortController();
    setLoading(true); setError(null);
    void provider.search(scope, keyword.trim(), page, 20, controller.signal).then(result => {
      if (controller.signal.aborted) return;
      setItems(current => page === 1 ? result.items : [...current, ...result.items]); setTotal(result.total);
    }, failure => { if (!controller.signal.aborted) setError(String(failure)); })
      .finally(() => { if (!controller.signal.aborted) setLoading(false); });
    return () => controller.abort();
  }, [keyword, opened, page, provider, retry, scope]);
  return <div style={{ position: 'relative', margin: '12px 0' }}>
    <input aria-label="搜索节点" placeholder="搜索名称或编码" value={keyword} onFocus={() => { if (keyword.trim() && !opened) { setPage(1); setItems([]); setOpened(true); } }}
      onChange={event => { setKeyword(event.target.value); setPage(1); setItems([]); setOpened(!!event.target.value.trim()); }} />
    {opened && <div aria-label="搜索结果" style={{ position: 'absolute', zIndex: 10, width: '100%', background: 'white', border: '1px solid #ddd', padding: 8, maxHeight: 360, overflowY: 'auto' }}>
      <button onClick={() => setOpened(false)}>关闭搜索结果</button>
      {items.map(item => {
        const type = view.nodeTypes.find(value => value.key === item.node.nodeType);
        return <button key={item.node.nodeKey} aria-label={`定位节点 ${item.node.label}`} style={{ display: 'block', width: '100%', textAlign: 'left', whiteSpace: 'normal', overflowWrap: 'anywhere', padding: 8 }} onClick={() => { choose(item.node.nodeKey); setOpened(false); }}>
          <Highlight text={item.node.label} keyword={keyword.trim()} /> {type ? t(`${view.dicKey}.${type.titleKey}`, type.titleKey) : ''}
          {item.node.code && <> · <Highlight text={item.node.code} keyword={keyword.trim()} /></>}
          <div><Highlight text={item.path.map(node => node.label).join(' / ')} keyword={keyword.trim()} /></div>
        </button>;
      })}
      {loading && <p role="status">正在搜索…</p>}
      {error && <div role="alert">搜索失败：{error}<button onClick={() => setRetry(value => value + 1)}>重试搜索</button></div>}
      {!loading && !error && !items.length && <p>没有匹配节点</p>}
      {!loading && !error && items.length < total && <button onClick={() => setPage(page + 1)}>加载更多</button>}
    </div>}
  </div>;
}
