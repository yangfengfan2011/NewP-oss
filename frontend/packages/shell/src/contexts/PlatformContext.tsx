import { createContext, useContext, ReactNode } from 'react';
import { useNavigate } from 'react-router-dom';
import { PlatformContext as IPlatformContext, ShellView, UiConfig } from '@icp-platform/sdk';
import { useAuth } from './AuthContext';

const PlatformContext = createContext<IPlatformContext | null>(null);

interface PlatformProviderProps {
  children: ReactNode;
  uiConfig: UiConfig;
  shellView: ShellView;
  setShellView: (view: ShellView) => void;
  canSwitchShellView: boolean;
}

export function PlatformProvider({
  children,
  uiConfig,
  shellView,
  setShellView,
  canSwitchShellView,
}: PlatformProviderProps) {
  const auth = useAuth();
  const navigate = useNavigate();

  const showNotification = (message: string, type: 'info' | 'success' | 'warning' | 'error' = 'info') => {
    // TODO: 实现通知系统
    console.log(`[${type.toUpperCase()}] ${message}`);
  };

  const showDialog = async (content: ReactNode, _options?: { title?: string; width?: number }): Promise<boolean> => {
    // TODO: 实现对话框系统
    return window.confirm(String(content));
  };

  return (
    <PlatformContext.Provider value={{
      auth,
      uiConfig,
      shellView,
      setShellView,
      canSwitchShellView,
      navigate,
      showNotification,
      showDialog
    }}>
      {children}
    </PlatformContext.Provider>
  );
}

export function usePlatform() {
  const context = useContext(PlatformContext);
  if (!context) {
    throw new Error('usePlatform must be used within PlatformProvider');
  }
  return context;
}
