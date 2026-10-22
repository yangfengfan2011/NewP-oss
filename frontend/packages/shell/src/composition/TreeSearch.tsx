import { useEffect, useMemo, useRef, useState } from 'react';
import SearchOutlined from '@ant-design/icons/SearchOutlined';
import Alert from 'antd/es/alert';
import Button from 'antd/es/button';
import Card from 'antd/es/card';
import Empty from 'antd/es/empty';
import Input from 'antd/es/input';
import Spin from 'antd/es/spin';
import theme from 'antd/es/theme';
import { formatRequestError, useLocalization } from '@icp-platform/core';
import type { TreeProviderBinding, TreeView } from '@icp-platform/sdk';

type SearchItem = Awaited<ReturnType<TreeProviderBinding['search']>>['items'][number];
function Highlight({ text, keyword }: { text: string; keyword: string }) {
  const index = text.toLocaleLowerCase().indexOf(keyword.toLocaleLowerCase());
  return index < 0 ? <>{text}</> : <>{text.slice(0, index)}<mark>{text.slice(index, index + keyword.length)}</mark>{text.slice(index + keyword.length)}</>;
}

/** 结果只展示搜索时路径；选择交给宿主重新读取当前位置。 */
export function TreeSearch({ provider, scope, view, choose, changeEpoch = 0, active = true }: {
  provider: TreeProviderBinding; scope: Record<string, unknown>; view: TreeView; choose: (key: string) => void; changeEpoch?: number; active?: boolean;
}) {
  const { t } = useLocalization();
  const { token } = theme.useToken();
  const [keyword, setKeyword] = useState('');
  // 身份变化在当前渲染即隐藏旧数据；effect 清理并非主体隔离的唯一保障。
  const context = useMemo(() => ({ provider, scope, keyword, changeEpoch, active }), [provider, scope, keyword, changeEpoch, active]);
  const currentContext = useRef(context); currentContext.current = context;
  const request = useRef<AbortController>();
  const [pagination, setPagination] = useState({ context, page: 1 });
  const page = pagination.context === context ? pagination.page : 1;
  const setPage = (value: number) => { request.current?.abort(); setPagination({ context, page: value }); };
  const [opened, setOpened] = useState(false);
  const [data, setData] = useState<{ context: object; items: SearchItem[]; total: number; loading: boolean; error: unknown } | null>(null);
  const current = data?.context === context ? data : null;
  const items = current?.items ?? []; const total = current?.total ?? 0;
  const loading = current?.loading ?? false; const error = current?.error;
  const [retry, setRetry] = useState(0);
  useEffect(() => {
    if (!keyword.trim() || !opened || !active) return;
    const controller = new AbortController(); request.current = controller;
    setData(previous => ({ context, items: page > 1 && previous?.context === context ? previous.items : [],
      total: page > 1 && previous?.context === context ? previous.total : 0, loading: true, error: null }));
    void provider.search(scope, keyword.trim(), page, 20, controller.signal).then(result => {
      if (controller.signal.aborted || currentContext.current !== context) return;
      setData(previous => ({ context, items: page === 1 ? result.items : [...(previous?.context === context ? previous.items : []), ...result.items], total: result.total, loading: false, error: null }));
    }, failure => {
      if (!controller.signal.aborted && currentContext.current === context) setData(previous => ({ context, items: previous?.context === context ? previous.items : [], total: previous?.context === context ? previous.total : 0, loading: false, error: failure }));
    });
    return () => controller.abort();
  }, [active, context, keyword, opened, page, provider, retry, scope]);
  const close = () => { request.current?.abort(); setOpened(false); setData(null); };
  return <div style={{ position: 'relative', marginBottom: token.marginSM }}>
    <Input aria-label={t('Platform.TreeSearch', '搜索节点')} placeholder={view.linkSource ? t('Platform.TreeSearchNamePlaceholder', '搜索名称') : t('Platform.TreeSearchPlaceholder', '搜索名称或编码')} prefix={<SearchOutlined style={{ color: token.colorTextPlaceholder }} />} allowClear value={keyword} onFocus={() => { if (keyword.trim() && !opened) { setPage(1); setData(null); setOpened(true); } }}
      onChange={event => { request.current?.abort(); setKeyword(event.target.value); setPage(1); setData(null); setOpened(!!event.target.value.trim()); }} />
    {opened && active && <Card aria-label={t('Platform.TreeSearchResults', '搜索结果')} size="small" title={t('Platform.TreeSearchResults', '搜索结果')}
      extra={<Button type="text" size="small" onClick={close}>{t('Platform.TreeCloseSearch', '关闭搜索结果')}</Button>}
      style={{ position: 'absolute', zIndex: token.zIndexPopupBase, width: '100%', marginTop: token.marginXS, boxShadow: token.boxShadowSecondary }}
      styles={{ body: { padding: token.paddingXS, maxHeight: 360, overflowY: 'auto' } }}>
      {items.map(item => {
        const type = view.nodeTypes.find(value => value.key === item.node.nodeType);
        return <Button type="text" block key={item.node.nodeKey} aria-label={t('Platform.TreeLocateNode', '定位节点 {name}', { name: item.node.label })} style={{ display: 'block', height: 'auto', textAlign: 'left', whiteSpace: 'normal', overflowWrap: 'anywhere', padding: token.paddingXS }} onClick={() => { choose(item.node.nodeKey); close(); }}>
          <span><Highlight text={item.node.label} keyword={keyword.trim()} /> {type ? t(`${view.dicKey}.${type.titleKey}`, type.titleKey) : ''}
            {item.node.code && <> · <Highlight text={item.node.code} keyword={keyword.trim()} /></>}</span>
          <span style={{ display: 'block', color: token.colorTextSecondary, fontSize: token.fontSizeSM }}><Highlight text={item.path.map(node => node.labelKey ? t(node.labelKey, node.label) : node.label).join(' / ')} keyword={keyword.trim()} /></span>
        </Button>;
      })}
      {loading && <div role="status" style={{ padding: token.paddingSM, textAlign: 'center' }}><Spin size="small" /> {t('Platform.TreeSearching', '正在搜索…')}</div>}
      {error != null && <Alert type="error" showIcon message={t('Platform.TreeSearchFailed', '搜索失败：{message}', { message: formatRequestError(error, t) })} action={<Button size="small" onClick={() => setRetry(value => value + 1)}>{t('Platform.TreeRetrySearch', '重试搜索')}</Button>} />}
      {!loading && !error && !items.length && <Empty image={Empty.PRESENTED_IMAGE_SIMPLE} description={t('Platform.TreeNoMatches', '没有匹配节点')} />}
      {!loading && !error && items.length < total && <Button type="link" block onClick={() => setPage(page + 1)}>{t('Platform.LoadMore', '加载更多')}</Button>}
    </Card>}
  </div>;
}
