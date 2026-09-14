import { describe, expect, it } from 'vitest';
import { createFeatureVisibility } from './featureVisibility';

describe('功能入口可见性', () => {
  const points = [
    { position: 'root', parentPosition: null, container: true },
    { position: 'empty', parentPosition: 'root', container: true },
    { position: 'content', parentPosition: 'root', container: false },
    { position: 'write', parentPosition: 'content', container: false },
  ];
  it('隐藏只有自身资格的纯容器，内容入口不需要额外动作授权', () => {
    const empty = createFeatureVisibility(points, position => ['root', 'empty'].includes(position));
    expect(empty('root')).toBe(false);
    expect(empty('empty')).toBe(false);
    const readOnly = createFeatureVisibility(points, position => ['root', 'content'].includes(position));
    expect(readOnly('root')).toBe(true);
    expect(readOnly('content')).toBe(true);
    expect(readOnly('empty')).toBe(false);
    expect(readOnly('write')).toBe(false);
  });
  it('未知位置及未授容器不会由后代资格反向开放', () => {
    const visible = createFeatureVisibility(points, position => position === 'content');
    expect(visible('root')).toBe(false);
    expect(visible('unknown')).toBe(false);
  });
});
