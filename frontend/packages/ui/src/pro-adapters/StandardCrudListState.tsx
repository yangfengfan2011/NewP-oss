import { useLocalization } from '@icp-platform/core';
import { Button, Empty, Result, Spin } from 'antd';

export interface StandardCrudListStateProps {
  initialLoading: boolean;
  error?: string | null;
  filtered: boolean;
  onRetry: () => void;
}

export function StandardCrudListState({
  initialLoading,
  error,
  filtered,
  onRetry,
}: StandardCrudListStateProps) {
  const { t } = useLocalization();
  if (initialLoading) {
    return <Spin tip={t('Platform.LoadingList', '正在加载列表')}><div style={{ minHeight: 48 }} /></Spin>;
  }

  if (error) {
    return (
      <Result
        status="error"
        title={t('Platform.ListLoadFailed', '列表加载失败')}
        subTitle={error}
        extra={<Button onClick={onRetry}>{t('Platform.Retry', '重试')}</Button>}
      />
    );
  }

  return (
    <Empty
      description={filtered
        ? t('Platform.FilteredEmpty', '暂无符合筛选条件的数据，请调整筛选条件。')
        : t('Platform.EmptyList', '当前数据集为空，可使用页面主动作创建第一条记录。')}
    />
  );
}
