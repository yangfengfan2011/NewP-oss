import React from 'react';
import ReactDOM from 'react-dom/client';
import { ModuleRegistry } from '@icp-platform/sdk';
import App from './App';
import { registerShellModules } from './registerModules';
import '@icp-platform/ui/styles';
import './index.css';

async function bootstrap() {
  await registerShellModules();

  await ModuleRegistry.initializeAll();
  console.log('All modules initialized');

  ReactDOM.createRoot(document.getElementById('root')!).render(
    <React.StrictMode>
      <App />
    </React.StrictMode>,
  );
}

bootstrap();
