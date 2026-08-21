import type {
  StandardCrudFormMode,
  StandardCrudResourceRegistration,
// @ts-expect-error 整体详情 renderer 上下文已从公共 SDK 退役。
  StandardCrudDetailExtensionContext,
} from '../src/index';

// @ts-expect-error 详情呈现不再是表单 mode。
const retiredViewMode: StandardCrudFormMode = 'view';

const registration: StandardCrudResourceRegistration = {
  resourceKey: 'sample.record',
  dataAdapter: {
    list: async () => ({ items: [], total: 0, page: 1, pageSize: 20 }),
    get: async () => ({
      normalized: { fields: {}, summaries: {} },
      value: { fields: {}, summaries: {} },
    }),
    getRowId: () => 'record-1',
  },
  listViewKey: 'sample.record.list',
  detailViewKey: 'sample.record.detail',
};

const retiredDetailActions: StandardCrudResourceRegistration = {
  ...registration,
  // @ts-expect-error 详情动作只由 Detail View pageActions 声明。
  detailActions: [],
};

const retiredDetailRenderer: StandardCrudResourceRegistration = {
  ...registration,
  // @ts-expect-error 普通详情结构只能由 Resolved Detail Schema 呈现。
  renderDetail: () => null,
};

const retiredHeaderRenderer: StandardCrudResourceRegistration = {
  ...registration,
  // @ts-expect-error 详情页头动作只由 Detail View pageActions 声明。
  renderDetailHeaderActions: () => null,
};

const retiredFlatDataRegistration: StandardCrudResourceRegistration = {
  ...registration,
  // @ts-expect-error 资源数据入口已收敛到 dataAdapter，不再向 Host 暴露 API 路径。
  apiRoute: '/api/sample/records',
};

const retiredMutationValidationRegistration: StandardCrudResourceRegistration = {
  ...registration,
  // @ts-expect-error 提交校验与错误映射由共享 Mutation 生命周期管理。
  validateMutationValues: () => null,
};

void retiredViewMode;
void retiredDetailActions;
void retiredDetailRenderer;
void retiredHeaderRenderer;
void retiredFlatDataRegistration;
void retiredMutationValidationRegistration;
void (undefined as unknown as StandardCrudDetailExtensionContext<unknown>);
