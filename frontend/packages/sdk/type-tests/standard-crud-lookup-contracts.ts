import type {
  StandardCrudLookupExecutionContext,
  StandardCrudLookupProvider,
} from '../src/index';
import { loadStandardCrudLookupOptions } from '../src/index';

const legacyProvider: StandardCrudLookupProvider = {
  key: 'legacy',
  load: async (_request) => [],
};

const contextAwareProvider: StandardCrudLookupProvider = {
  key: 'context-aware',
  load: async (_request, context?: StandardCrudLookupExecutionContext) => {
    void context?.signal.aborted;
    return [];
  },
};

const lookupInput = {
  field: {
    key: 'companyId',
    labelKey: '公司',
    modelField: 'COMPANY_ID',
    kind: 'lookup' as const,
    lookup: { providerKey: 'legacy' },
  },
  providers: [legacyProvider],
  values: {},
};
const controller = new AbortController();

void loadStandardCrudLookupOptions(lookupInput);
void loadStandardCrudLookupOptions(lookupInput, { signal: controller.signal });

void legacyProvider;
void contextAwareProvider;
