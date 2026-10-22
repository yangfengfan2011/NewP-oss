type PlatformQueryLifecycleEvent = 'started' | 'joined' | 'completed' | 'failed' | 'orphan-aborted';

export type PlatformQueryLifecycleReason = 'initial' | 'refresh' | 'retry' | 'invalidate';

export interface PlatformQueryLifecycleIdentity {
  readonly owner: string;
  readonly keyHash: string;
}

interface PlatformQueryLifecycleRecord extends PlatformQueryLifecycleIdentity {
  readonly generation: number;
  readonly event: PlatformQueryLifecycleEvent;
  readonly subscriberCount: number;
  readonly durationMs: number;
  readonly reason: PlatformQueryLifecycleReason;
}

/** 仅为私有运行时观测生成固定宽度稳定标识，不把 canonical key 写入日志。 */
function stableHash(value: string): string {
  let hash = 0x811c9dc5;
  for (let index = 0; index < value.length; index += 1) {
    hash ^= value.charCodeAt(index);
    hash = Math.imul(hash, 0x01000193);
  }
  return (hash >>> 0).toString(16).padStart(8, '0');
}

/** 从已经校验的 query key 建立私有观测身份；owner 是契约规定的稳定首段。 */
export function createPlatformQueryLifecycleIdentity(
  queryKey: string,
  keySegments: readonly string[],
): PlatformQueryLifecycleIdentity {
  return {
    owner: JSON.parse(keySegments[0] ?? '"<unknown>"') as string,
    keyHash: stableHash(queryKey),
  };
}

/** 使用单调时钟计算单次 generation 的本地耗时。 */
export function readPlatformQueryLifecycleClock(): number {
  return globalThis.performance?.now() ?? Date.now();
}

/** 输出固定、值无关的结构化记录；不得向这里传入 key、结果、错误或认证值。 */
export function recordPlatformQueryLifecycle(record: PlatformQueryLifecycleRecord): void {
  try {
    console.debug('platform-query.lifecycle', record);
  } catch {
    // 观测是旁路能力；宿主替换或损坏 console sink 时仍必须保持查询状态机可用。
  }
}
