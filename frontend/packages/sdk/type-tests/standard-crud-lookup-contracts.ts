import type {
  StandardCrudLookupExecutionContext,
  StandardCrudLookupPage,
  StandardCrudLookupProvider,
} from '../src/index';
import { loadStandardCrudLookupOptions, loadStandardCrudLookupPage } from '../src/index';

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

const pagedProvider: StandardCrudLookupProvider = {
  key: 'paged',
  load: async (request): Promise<StandardCrudLookupPage> => ({
    options: [{ value: `${request.keyword}-${request.page}`, label: '匹配项' }],
    currentOptions: request.currentValueIds.map((value) => ({ value, label: '当前值' })),
    page: request.page,
    pageSize: request.pageSize,
    hasMore: true,
  }),
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
void loadStandardCrudLookupPage({
  ...lookupInput,
  keyword: '财务',
  page: 2,
  pageSize: 20,
}, { signal: controller.signal });

void legacyProvider;
void contextAwareProvider;
void pagedProvider;
