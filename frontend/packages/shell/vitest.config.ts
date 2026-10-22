import { fileURLToPath, URL } from 'node:url';
import { mergeConfig } from 'vite';
import { defineConfig } from 'vitest/config';
import viteConfig from './vite.config';

export default mergeConfig(
  viteConfig,
  defineConfig({
    resolve: {
      alias: [
        {
          find: '../../../ui/dist/components/DataTable.js',
          replacement: fileURLToPath(new URL('../ui/src/components/DataTable.tsx', import.meta.url)),
        },
        {
          find: '../../../ui/dist/components/Tree.js',
          replacement: fileURLToPath(new URL('../ui/src/components/Tree.tsx', import.meta.url)),
        },
        {
          find: '../../../ui/dist/components/TreeSelect.js',
          replacement: fileURLToPath(new URL('../ui/src/components/TreeSelect.tsx', import.meta.url)),
        },
        {
          find: '../../../ui/dist/components/Uploader.js',
          replacement: fileURLToPath(new URL('../ui/src/components/Uploader.tsx', import.meta.url)),
        },
      ],
    },
  }),
);
