import React from 'react';
import ReactDOM from 'react-dom/client';
import { ModuleRegistry } from '@icp-platform/sdk';
import App from './App';
import { bootstrapShell } from './bootstrap';
import { ModuleRuntimeFatal } from './components/ModuleRuntimeFatal';
import { registerShellModules } from './registerModules';
import '@icp-platform/ui/styles';
import './index.css';

const root = ReactDOM.createRoot(document.getElementById('root')!);

void bootstrapShell({
  registerModules: registerShellModules,
  initializeModules: () => ModuleRegistry.initializeAll(),
  renderApp: () => root.render(
    <React.StrictMode>
      <App />
    </React.StrictMode>,
  ),
  renderFatal: (error, retry) => root.render(
    <ModuleRuntimeFatal error={error} onRetry={() => void retry()} />,
  ),
});
