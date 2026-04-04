import React from 'react';
import ReactDOM from 'react-dom/client';
import { ModuleRegistry } from '@icp-platform/sdk';
import App from './App';
import { registerShellModules } from './registerModules';
import './index.css';

registerShellModules();

ModuleRegistry.initializeAll().then(() => {
  console.log('All modules initialized');
});

ReactDOM.createRoot(document.getElementById('root')!).render(
  <React.StrictMode>
    <App />
  </React.StrictMode>,
);
