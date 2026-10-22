import type {
  StandardCrudDataAdapter,
  StandardCrudDetailReadContext,
} from '../src/index';

const legacyAdapter = {
  list: async () => ({ items: [], total: 0, page: 1, pageSize: 20 }),
  get: async () => ({ normalized: { fields: {}, summaries: {} }, value: { fields: {}, summaries: {} } }),
  getRowId: () => 'legacy',
} satisfies StandardCrudDataAdapter;

const contextAwareAdapter = {
  list: async () => ({ items: [], total: 0, page: 1, pageSize: 20 }),
  get: async (_id: string, context?: StandardCrudDetailReadContext) => {
    void context?.signal?.aborted;
    return { normalized: { fields: {}, summaries: {} }, value: { fields: {}, summaries: {} } };
  },
  getRowId: () => 'context-aware',
} satisfies StandardCrudDataAdapter;

const legacyContract: StandardCrudDataAdapter = legacyAdapter;
void legacyContract.get('legacy');
void contextAwareAdapter.get('context-aware', { signal: new AbortController().signal });
