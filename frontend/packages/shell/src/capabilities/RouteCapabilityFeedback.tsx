import { Component, type ErrorInfo, type ReactNode } from 'react';
import { Button } from 'antd';
import { RouteCapabilityError, type RouteCapabilityErrorCode } from './RouteCapabilityRuntime';

export function RouteCapabilityLoading() {
  return <div className="sh-loading-state" role="status">正在加载页面…</div>;
}

function resolveFailureCode(error: unknown): RouteCapabilityErrorCode {
  return error instanceof RouteCapabilityError
    ? error.code
    : 'ROUTE_CAPABILITY_RENDER_FAILED';
}

function RouteCapabilityFailure({ error, retry }: { error: unknown; retry: () => void }) {
  const code = resolveFailureCode(error);
  return (
    <div className="sh-error-state" role="alert" data-route-capability-error={code}>
      <div>{code}</div>
      <div>当前页面无法加载，其他页面仍可继续使用。</div>
      <Button onClick={retry}>重试当前页面</Button>
    </div>
  );
}

interface RouteCapabilityErrorBoundaryProps {
  children: ReactNode;
  retry: () => void;
}

interface RouteCapabilityErrorBoundaryState {
  error: unknown | null;
}

export class RouteCapabilityErrorBoundary extends Component<
  RouteCapabilityErrorBoundaryProps,
  RouteCapabilityErrorBoundaryState
> {
  state: RouteCapabilityErrorBoundaryState = { error: null };

  static getDerivedStateFromError(error: unknown): RouteCapabilityErrorBoundaryState {
    return { error };
  }

  componentDidCatch(_error: unknown, _info: ErrorInfo): void {
    // React 负责记录组件栈；边界只把失败限制在当前 capability，并保留显式 retry。
  }

  render() {
    return this.state.error
      ? <RouteCapabilityFailure error={this.state.error} retry={this.props.retry} />
      : this.props.children;
  }
}
