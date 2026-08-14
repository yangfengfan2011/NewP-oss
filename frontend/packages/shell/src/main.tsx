import React from 'react';
import ReactDOM from 'react-dom/client';
import { ModuleRegistry } from '@icp-platform/sdk';
import { AuthenticationOnlyApp } from './AuthenticationOnlyApp';
import { bootstrapShell } from './bootstrap';
import { ModuleRuntimeFatal } from './components/ModuleRuntimeFatal';
import { registerShellModules } from './registerModules';
import { readCurrentUser } from '@icp-platform/core';
import '@icp-platform/ui/styles';
import './index.css';

const root = ReactDOM.createRoot(document.getElementById('root')!);

void bootstrapShell({
  readAuthenticationSnapshot: readCurrentUser,
  registerModules: registerShellModules,
  initializeModules: () => ModuleRegistry.initializeAll(),
  renderApp: async (user) => {
    if (user && !user.mustChangePassword) {
      const { default: App } = await import('./App');
      root.render(
        <React.StrictMode>
          <App initialUser={user} />
        </React.StrictMode>,
      );
      return;
    }

    root.render(
      <React.StrictMode>
        <AuthenticationOnlyApp initialUser={user} />
      </React.StrictMode>,
    );
  },
  renderFatal: (error, retry) => root.render(
    <ModuleRuntimeFatal error={error} onRetry={() => void retry()} />,
  ),
});
