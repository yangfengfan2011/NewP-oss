// @vitest-environment node

import { describe, expect, it } from 'vitest';
import config from '../../vite.config';

describe('vite config', () => {
  it('keeps metadata api traffic under the shared /api proxy so page routes do not collide', () => {
    const serverConfig = typeof config === 'function' ? config({ command: 'serve', mode: 'test' }) : config;
    const proxy = serverConfig.server?.proxy;

    expect(proxy).toBeDefined();
    expect(proxy).toHaveProperty('/api');
    expect(proxy?.['/api']).toMatchObject({
      target: 'http://localhost:5000',
      changeOrigin: true,
    });
    expect(proxy).not.toHaveProperty('/metadata-config');
  });
});
