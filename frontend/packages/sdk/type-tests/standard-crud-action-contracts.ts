import type {
  StandardCrudActionDefinition,
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

void genericState;
void genericAction;
