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
