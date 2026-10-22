import { useLocalization } from '@icp-platform/core';
import { Component, ErrorInfo, ReactNode } from 'react';

interface Props {
  children: ReactNode;
  fallback?: ReactNode;
}

interface State {
  hasError: boolean;
  error?: Error;
}

export class ErrorBoundary extends Component<Props, State> {
  constructor(props: Props) {
    super(props);
    this.state = { hasError: false };
  }

  static getDerivedStateFromError(error: Error): State {
    return { hasError: true, error };
  }

  componentDidCatch(error: Error, errorInfo: ErrorInfo) {
    console.error('ErrorBoundary caught an error:', error, errorInfo);
  }

  render() {
    if (this.state.hasError) {
      if (this.props.fallback) {
        return this.props.fallback;
      }

      return <PageError onRetry={() => this.setState({ hasError: false })} />;
    }

    return this.props.children;
  }
}

export function PageError({ onRetry }: { onRetry: () => void }) {
  const { t } = useLocalization();
  return <div className="sh-error-boundary" role="alert">
    <h2>{t('PlatformShell.PageError', '页面出现错误')}</h2>
    <p>{t('Platform.UnexpectedError', '操作失败，请稍后重试。')}</p>
    <button className="sh-btn sh-btn-primary" onClick={onRetry}>{t('PlatformAuthentication.Retry', '重试')}</button>
  </div>;
}
