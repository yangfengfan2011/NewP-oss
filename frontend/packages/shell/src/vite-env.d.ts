/// <reference types="vite/client" />

interface ImportMetaEnv {
  readonly VITE_FRONTEND_PLUGIN_MANIFEST_URL?: string;
}

interface ImportMeta {
  readonly env: ImportMetaEnv;
}

declare module 'virtual:newp-frontend-entry-registry' {
  const registry: Record<string, () => Promise<Record<string, unknown>>>;
  export default registry;
}
