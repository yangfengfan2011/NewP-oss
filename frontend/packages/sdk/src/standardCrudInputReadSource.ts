import type { ActionDataAccessRequirement, StandardCrudActionDefinition, StandardCrudFeatureRequestContext } from './standardCrud.types';

/** 从正式目录取得当前输入显式引用的唯一读取来源；声明有误时拒绝，不借保存按钮或其他功能位置回退。 */
export function standardCrudInputReadSource(
  context: Pick<StandardCrudFeatureRequestContext, 'featurePosition' | 'dataAccessDeclarations'> & {
    action?: Pick<StandardCrudActionDefinition, 'inputReadSourceKey' | 'dataAccess'>;
  },
): ActionDataAccessRequirement | undefined {
  const key = context.action?.inputReadSourceKey;
  if (key === undefined) return undefined;
  if (typeof key !== 'string' || !key.trim() || !context.featurePosition) throw new Error('INPUT_READ_SOURCE_INVALID');
  const matches = context.dataAccessDeclarations?.filter(item => item.viewKey === key && item.surface === 'read'
    && item.featurePositions.includes(context.featurePosition!)) ?? [];
  const source = matches.length === 1 ? matches[0] : undefined;
  if (!source || source.businessBinding || source.actions.length !== 1 || source.actions[0] !== 'read'
    || context.action?.dataAccess && source.moduleId !== context.action.dataAccess.moduleId
    || !source.requirements?.length || source.requirements.some(item => item.stage === 'submit'
      || item.anyOf.length !== 1 || item.anyOf[0].length !== 1 || item.anyOf[0][0] !== 'read')) {
    throw new Error('INPUT_READ_SOURCE_INVALID');
  }
  return source;
}
