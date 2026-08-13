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
  if (initialLoading) {
    return <Spin tip="正在加载列表"><div style={{ minHeight: 48 }} /></Spin>;
  }

  if (error) {
    return (
      <Result
        status="error"
        title="列表加载失败"
        subTitle={error}
        extra={<Button onClick={onRetry}>重试</Button>}
      />
    );
  }

  return (
    <Empty
      description={filtered
        ? '暂无符合筛选条件的数据，请调整筛选条件。'
        : '当前数据集为空，可使用页面主动作创建第一条记录。'}
    />
  );
}
