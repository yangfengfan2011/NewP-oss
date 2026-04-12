// @vitest-environment node

import { describe, expect, it } from 'vitest';
import config from '../../vite.config';

describe('vite config', () => {
  it('proxies metadata-config requests to the host api', () => {
    const serverConfig = typeof config === 'function' ? config({ command: 'serve', mode: 'test' }) : config;
    const proxy = serverConfig.server?.proxy;

    expect(proxy).toBeDefined();
    expect(proxy).toHaveProperty('/metadata-config');
    expect(proxy?.['/metadata-config']).toMatchObject({
      target: 'http://localhost:5000',
      changeOrigin: true,
    });
  });
});
