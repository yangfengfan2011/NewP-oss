import type { StandardCrudActionInstance } from '@icp-platform/sdk';

/** 只归一个树/页面实例的动作状态容器；模块拥有值及其释放逻辑，宿主只管理生命周期。 */
export class ActionInstanceScope implements StandardCrudActionInstance {
  private values = new Map<symbol, { value: unknown; dispose: () => void }>();
  private listeners = new Set<() => void>();
  private version = 0;
  private live = true;
  get active(): boolean { return this.live; }
  readonly subscribe = (listener: () => void): (() => void) => { this.listeners.add(listener); return () => { this.listeners.delete(listener); }; };
  readonly getSnapshot = (): number => this.version;
  getOrCreate<T>(owner: symbol, create: () => T, dispose: (value: T) => void): T {
    if (!this.live) throw new Error('Action instance has ended.');
    const existing = this.values.get(owner);
    if (existing) return existing.value as T;
    const value = create(); this.values.set(owner, { value, dispose: () => dispose(value) }); return value;
  }
  changed(): void { if (this.live) { this.version++; for (const listener of this.listeners) listener(); } }
  /** Effect重挂载开启新的状态生命周期，旧模块值已经释放，迟到响应不能恢复它们。 */
  activate(): () => void {
    this.live = true; this.changed();
    return () => {
      this.live = false;
      for (const entry of this.values.values()) entry.dispose();
      this.values.clear(); this.version++;
    };
  }
}
