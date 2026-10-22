import type {
  StandardCrudActionDefinition,
  StandardCrudActionHandler,
  StandardCrudActionState,
} from '../src/index';

const genericState: StandardCrudActionState = {
  visible: true,
  available: false,
  reason: '当前快照不允许执行该操作。',
  // @ts-expect-error 模块业务状态不属于公共动作结果。
  status: 'Enabled',
};

const genericAction: StandardCrudActionDefinition = {
  key: 'secure',
  labelKey: 'Action_Secure',
  // @ts-expect-error 模块业务表达式不属于公共动作声明。
  visibleWhen: { Status: 'Enabled' },
};

const localizedState: StandardCrudActionState = {
  visible: true,
  available: false,
  reason: 'Owner guard diagnostic',
  reasonKey: 'Example.OwnerProtected',
  labelKey: 'Example.ActionPending',
  args: { owner: 'Admin', count: 1 },
};

const moduleActionHandler: StandardCrudActionHandler = async (context) => {
  // @ts-expect-error 页面刷新属于 Shell 动作生命周期，不暴露给模块 handler。
  context.refresh();
};

void genericState;
void genericAction;
void localizedState;
void moduleActionHandler;

const correctableInputHandler: StandardCrudActionHandler = Object.assign(async () => undefined, {
  isCorrectableInputError: (failure: unknown) => failure instanceof Error,
});
// @ts-expect-error 错误分类为同步纯判断，不能在共享错误边界中再次发起异步工作。
const asynchronousClassification: StandardCrudActionHandler = Object.assign(async () => undefined, {
  isCorrectableInputError: async (_failure: unknown) => true,
});
void correctableInputHandler;
void asynchronousClassification;

// 旧 handler 无须提供列表上下文；新输出消费者只能读取或复制已应用快照。
const listOutputHandler: StandardCrudActionHandler = context => {
  if (!context.listQuery) return;
  const request = { ...context.listQuery };
  // @ts-expect-error 动作不能改写宿主持有的当前页快照。
  context.listQuery.pageIndex = 2;
  // @ts-expect-error 动作不能追加或改写查询筛选。
  context.listQuery.keyword = 'changed';
  // @ts-expect-error 快照属性本身也不可重新赋值。
  context.listQuery = request;
};
void listOutputHandler;

const readonlyPreview: import('../src/standardCrud.types').StandardCrudActionInputRenderer = context => {
  if (context.listQuery) {
    // @ts-expect-error 预览不能修改已应用查询。
    context.listQuery.pageIndex = 3;
    // @ts-expect-error 预览不能替换查询快照。
    context.listQuery = {};
  }
  return null;
};
void readonlyPreview;

// 来源可只提供原 get；导航贡献只接受固定目标和同步字符串定位投影。
const sourceHistory: import('../src/standardCrud.types').StandardCrudDetailReadSource = {
  key: 'source.properties', resourceKeys: ['example.record'],
  get: async () => ({ value: { fields: { EID: 'one' }, summaries: {} }, normalized: { fields: { EID: 'one' }, summaries: {} } }),
  navigationContributions: [{ key: 'history', labelKey: 'Example.History', navigationKey: 'example.history',
    getParameters: (_resourceKey, id) => ({ recordId: id }) }],
};
const asynchronousSourceNavigation: NonNullable<typeof sourceHistory.navigationContributions>[number] = {
  key: 'history', labelKey: 'Example.History', navigationKey: 'example.history',
  // @ts-expect-error 导航投影必须同步；不在来源导航阶段发起异步工作。
  getParameters: async () => ({ recordId: 'one' }),
};
void sourceHistory;
void asynchronousSourceNavigation;
