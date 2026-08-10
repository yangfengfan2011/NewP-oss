import React from 'react';
import ReactDOM from 'react-dom/client';
import { ModuleRegistry } from '@icp-platform/sdk';
import App, { AuthenticationOnlyApp } from './App';
import { bootstrapShell } from './bootstrap';
import { ModuleRuntimeFatal } from './components/ModuleRuntimeFatal';
import { registerShellModules } from './registerModules';
import { readCurrentUser } from '@icp-platform/core';
import '@icp-platform/ui/styles';
import './index.css';

const root = ReactDOM.createRoot(document.getElementById('root')!);

void bootstrapShell({
  readAuthenticationState: async () => {
    const user = await readCurrentUser();
    return !user ? 'anonymous' : user.mustChangePassword ? 'restricted' : 'regular';
  },
  registerModules: registerShellModules,
  initializeModules: () => ModuleRegistry.initializeAll(),
  renderApp: (authenticationState) => root.render(
    <React.StrictMode>
      {authenticationState === 'regular' ? <App /> : <AuthenticationOnlyApp />}
    </React.StrictMode>,
  ),
  renderFatal: (error, retry) => root.render(
    <ModuleRuntimeFatal error={error} onRetry={() => void retry()} />,
  ),
});
