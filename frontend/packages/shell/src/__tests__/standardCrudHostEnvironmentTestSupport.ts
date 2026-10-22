import type {
  StandardCrudHostEnvironment,
  StandardCrudHostInput,
  StandardCrudHostLifecycle,
  StandardCrudHostPresentation,
  StandardCrudHostSync,
} from '../standard-crud/standardCrudHostEnvironment';

export interface EmbeddedHostEnvironmentOverrides {
  lifecycle?: Partial<StandardCrudHostLifecycle>;
  sync?: Partial<StandardCrudHostSync>;
  input?: StandardCrudHostInput;
  presentation?: StandardCrudHostPresentation;
}

/** 测试用嵌入环境：缺省值与 PageOutlet 组装的嵌入 adapter 一致，仅供 shell 测试使用。 */
export function createEmbeddedHostEnvironment(overrides: EmbeddedHostEnvironmentOverrides = {}): StandardCrudHostEnvironment {
  return {
    address: null,
    lifecycle: { active: true, reportState: () => undefined, ...overrides.lifecycle },
    sync: { committed: async () => undefined, refreshVersion: 0, ...overrides.sync },
    input: { instanceId: 'test-instance', ...overrides.input },
    presentation: overrides.presentation ?? { kind: 'workspace', composed: true },
  };
}
