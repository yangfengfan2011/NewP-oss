import { useLocalization, type LocalizationContextValue } from '@icp-platform/core';
import { Alert, Button, Form, Input, Select, Space, Typography } from 'antd';

export interface DataScopeCondition {
  type: string; field: string; operator: string; value: unknown; contextKey?: string | null;
  children?: DataScopeCondition[] | null; key?: string | null;
}
export interface DataScopeResourceField { name: string; type: string; title?: string; enumName?: string; enumValues?: string[] | null }
export interface DataScopeContextValue { key: string; title: string; valueType: string }
export interface DataScopeBusinessType { key: string; title: string; modelKeys: string[]; hasReadContribution: boolean }

const operations = (t: LocalizationContextValue['t']) => [
  ['equal', t('DataScope.Equal', '等于')], ['notEqual', t('DataScope.NotEqual', '不等于')], ['greaterThan', t('DataScope.Greater', '大于')], ['greaterThanOrEqual', t('DataScope.GreaterEqual', '大于等于')],
  ['lessThan', t('DataScope.Less', '小于')], ['lessThanOrEqual', t('DataScope.LessEqual', '小于等于')], ['contains', t('DataScope.Contains', '包含')], ['startsWith', t('DataScope.Starts', '开头是')],
  ['endsWith', t('DataScope.Ends', '结尾是')], ['in', t('DataScope.In', '属于')], ['notIn', t('DataScope.NotIn', '不属于')], ['isNull', t('DataScope.IsNull', '为空')], ['notNull', t('DataScope.NotNull', '不为空')],
] as const;
const nullOperations = new Set(['isNull', 'notNull']);
const setOperations = new Set(['in', 'notIn']);
const orderedOperations = new Set(['greaterThan', 'greaterThanOrEqual', 'lessThan', 'lessThanOrEqual']);
const textOperations = new Set(['contains', 'startsWith', 'endsWith']);
const orderedTypes = new Set(['Int32', 'Int64', 'Decimal', 'Double', 'Single', 'DateOnly', 'DateTime']);
const numberTypes = new Set(['Int32', 'Int64', 'Decimal', 'Double', 'Single']);
const fixedValue = (value: unknown, t: LocalizationContextValue['t']): string => {
  if (value === null) return t('DataScope.Null', '空值');
  if (value === '') return t('DataScope.EmptyText', '空文本');
  if (typeof value === 'boolean') return value ? t('DataScope.Yes', '是') : t('DataScope.No', '否');
  if (typeof value === 'string' || typeof value === 'number') return String(value);
  if (Array.isArray(value)) return value.length ? value.map(item => fixedValue(item, t)).join('、') : t('DataScope.EmptySet', '空集合');
  return t('DataScope.Configured', '已配置值');
};

/** 只描述既有条件配置；祖先摘要不重复运行裁决或解释服务器事实。 */
export const describeDataScopeCondition = (condition: DataScopeCondition, contexts: DataScopeContextValue[],
  businessTypes: DataScopeBusinessType[], fields: DataScopeResourceField[] = [], t: LocalizationContextValue['t'] = (_key, fallback) => fallback ?? _key, enumLabel: LocalizationContextValue['getEnumerationLabel'] = (_name, value) => value): string => {
  if (condition.type === 'group') {
    const joined = (condition.children ?? []).map(child => describeDataScopeCondition(child, contexts, businessTypes, fields, t, enumLabel))
      .join(condition.operator === 'or' ? ` ${t('DataScope.Or', '或 OR')} ` : ` ${t('DataScope.And', '且 AND')} `);
    return `（${joined}）`;
  }
  if (condition.type === 'business') {
    const business = businessTypes.find(item => item.key === condition.key);
    return `${business ? t(`DataScope.Business_${business.key}`, business.title) : condition.key ?? t('DataScope.NoBusiness', '未选业务条件')}${business?.hasReadContribution === false ? t('DataScope.NoReadSuffix', '（无读取贡献）') : ''}`;
  }
  const field = fields.find(field => field.name === condition.field);
  const title = field?.title ?? condition.field;
  const operation = operations(t).find(([key]) => key === condition.operator)?.[1] ?? condition.operator;
  if (nullOperations.has(condition.operator)) return `${title} ${operation}`;
  const value = condition.contextKey
    ? `${t('DataScope.Context', '当前身份值')}：${t(`DataScope.Context_${condition.contextKey}`, contexts.find(item => item.key === condition.contextKey)?.title ?? condition.contextKey)}`
    : field?.enumName ? (Array.isArray(condition.value) ? condition.value : [condition.value]).map(value => enumLabel(field.enumName!, String(value), String(value))).join('、') : fixedValue(condition.value, t);
  return `${title} ${operation} ${value}`;
};
const attribute = (field = ''): DataScopeCondition => ({ type: 'attribute', field, operator: 'equal', value: '' });
const group = (operator: 'and' | 'or', child: DataScopeCondition): DataScopeCondition =>
  ({ type: 'group', field: '', operator, value: null, children: [child] });

/** 编辑节点只修改当前草稿；服务端仍是类型、目标与可信当前值的最终校验者。 */
export default function DataScopeConditionEditor({ condition, path, fields, modelKey, contexts, bindings, businessTypes,
  onChange, onRemove, errorPath, errorMessage, leafOnly = false }: {
  condition: DataScopeCondition; path: string; fields: DataScopeResourceField[]; modelKey: string;
  contexts: DataScopeContextValue[]; bindings: { modelKey: string; field: string; contextKey: string }[];
  businessTypes: DataScopeBusinessType[]; onChange: (value: DataScopeCondition) => void; onRemove?: () => void;
  errorPath?: string; errorMessage?: string; leafOnly?: boolean;
}) {
  const { t, getEnumerationLabel } = useLocalization();
  const selected = fields.find(field => field.name === condition.field);
  const availableContexts = contexts.filter(value => bindings.some(binding => binding.modelKey === modelKey
    && binding.field === condition.field && binding.contextKey === value.key && value.valueType === selected?.type));
  const availableOperations = operations(t).filter(([key]) => !textOperations.has(key) || selected?.type === 'String' && !selected.enumValues)
    .filter(([key]) => !orderedOperations.has(key) || orderedTypes.has(selected?.type ?? ''));
  const business = businessTypes.find(type => type.key === condition.key);
  const changeChild = (index: number, value: DataScopeCondition) => onChange({ ...condition,
    children: condition.children?.map((child, childIndex) => childIndex === index ? value : child) });
  const addChild = (value: DataScopeCondition) => onChange({ ...condition, children: [...condition.children ?? [], value] });
  const valueSource = condition.contextKey ? 'context' : 'fixed';
  const value = condition.value;
  return <div role="group" aria-label={t('DataScope.ConditionItem', '条件项 {path}', { path })} style={{ border: '1px solid var(--ant-color-border-secondary)', borderRadius: 6,
    padding: 12, marginBottom: 12, minWidth: 0 }}>
    {errorPath === path && errorMessage && <Alert type="error" showIcon message={errorMessage} style={{ marginBottom: 8 }} />}
    <Space wrap align="start" style={{ marginBottom: 8 }}>
      <Typography.Text strong>{t('DataScope.ConditionPath', '条件 {path}', { path })}</Typography.Text>
      {!leafOnly && condition.type !== 'group' && <Button size="small" onClick={() => onChange(group('and', condition))}>{t('DataScope.ToAnd', '转换为 AND 组合')}</Button>}
      {!leafOnly && condition.type !== 'group' && <Button size="small" onClick={() => onChange(group('or', condition))}>{t('DataScope.ToOr', '转换为 OR 组合')}</Button>}
      {onRemove && <Button size="small" danger onClick={onRemove}>{t('DataScope.RemoveCondition', '删除条件')}</Button>}
    </Space>
    {condition.type === 'group' ? <>
      <Form.Item label={t('DataScope.Combination', '组合方式')}><Select aria-label={`${t('DataScope.ConditionItem', '条件项 {path}', { path })} ${t('DataScope.Combination', '组合方式')}`} style={{ width: 130 }} value={condition.operator}
        options={[{ value: 'and', label: t('DataScope.And', '且 AND') }, { value: 'or', label: t('DataScope.Or', '或 OR') }]}
        onChange={operator => onChange({ ...condition, operator })} /></Form.Item>
      {condition.children?.map((child, index) => <DataScopeConditionEditor key={index} condition={child} path={`${path}.${index + 1}`}
        fields={fields} modelKey={modelKey} contexts={contexts} bindings={bindings} businessTypes={businessTypes}
        errorPath={errorPath} errorMessage={errorMessage} onChange={next => changeChild(index, next)} onRemove={() => onChange({ ...condition,
          children: condition.children?.filter((_, childIndex) => childIndex !== index) })} />)}
      {(condition.children?.length ?? 0) === 0 && <Alert type="warning" showIcon message={t('DataScope.GroupRequired', '组合中至少添加一个条件。')} />}
      <Space wrap><Button size="small" onClick={() => addChild(attribute(fields[0]?.name))}>{t('DataScope.AddAttribute', '添加属性条件')}</Button>
        <Button size="small" disabled={businessTypes.length === 0} onClick={() => addChild({ type: 'business', field: '', operator: '', value: null,
          key: businessTypes[0]?.key })}>{t('DataScope.AddBusiness', '添加业务条件')}</Button>
        <Button size="small" onClick={() => addChild(group('and', attribute(fields[0]?.name)))}>{t('DataScope.AddAnd', '添加 AND 组合')}</Button>
        <Button size="small" onClick={() => addChild(group('or', attribute(fields[0]?.name)))}>{t('DataScope.AddOr', '添加 OR 组合')}</Button></Space>
    </> : condition.type === 'business' ? <Space wrap align="start">
      <Form.Item label={t('DataScope.Business', '业务条件')}><Select aria-label={`${t('DataScope.ConditionItem', '条件项 {path}', { path })} ${t('DataScope.Business', '业务条件')}`} style={{ minWidth: 220 }} value={condition.key}
        options={businessTypes.map(type => ({ value: type.key, label: t(`DataScope.Business_${type.key}`, type.title) }))}
        onChange={key => onChange({ ...condition, key })} /></Form.Item>
      <Typography.Text type="secondary">{business?.hasReadContribution ? t('DataScope.ReadContribution', '提供读取贡献') : t('DataScope.NoReadContribution', '无读取贡献；完整判断仍生效')}</Typography.Text>
    </Space> : <Space wrap align="start">
      <Form.Item label={t('DataScope.Attribute', '直接属性')}><Select aria-label={path === '1' ? t('DataScope.Attribute', '直接属性') : `${t('DataScope.ConditionItem', '条件项 {path}', { path })} ${t('DataScope.Attribute', '直接属性')}`} style={{ minWidth: 180 }} value={condition.field || undefined}
        options={fields.map(field => ({ value: field.name, label: field.title ?? field.name }))}
        onChange={field => onChange({ ...condition, field, operator: 'equal', value: '', contextKey: null })} /></Form.Item>
      <Form.Item label={t('DataScope.Comparison', '比较')}><Select aria-label={`${t('DataScope.ConditionItem', '条件项 {path}', { path })} ${t('DataScope.Comparison', '比较')}`} style={{ minWidth: 130 }} value={condition.operator}
        options={availableOperations.map(([key, label]) => ({ value: key, label }))}
        onChange={operator => onChange({ ...condition, operator, value: setOperations.has(operator) ? [] : nullOperations.has(operator) ? null : '', contextKey: null })} /></Form.Item>
      {!nullOperations.has(condition.operator) && <Form.Item label={t('DataScope.ValueSource', '值来源')}><Select aria-label={path === '1' ? t('DataScope.ValueSource', '值来源') : `${t('DataScope.ConditionItem', '条件项 {path}', { path })} ${t('DataScope.ValueSource', '值来源')}`} style={{ width: 150 }} value={valueSource}
        options={[{ value: 'fixed', label: t('DataScope.Fixed', '固定值') }, { value: 'context', label: t('DataScope.Context', '当前身份值'), disabled: availableContexts.length === 0 || setOperations.has(condition.operator) }]}
        onChange={source => onChange({ ...condition, value: source === 'context' ? null : '',
          contextKey: source === 'context' ? availableContexts[0]?.key : null })} /></Form.Item>}
      {!nullOperations.has(condition.operator) && (condition.contextKey ? <Form.Item label={t('DataScope.Context', '当前身份值')}>
        <Select aria-label={path === '1' ? t('DataScope.Context', '当前身份值') : `${t('DataScope.ConditionItem', '条件项 {path}', { path })} ${t('DataScope.Context', '当前身份值')}`} style={{ minWidth: 160 }} value={condition.contextKey}
          options={availableContexts.map(item => ({ value: item.key, label: t(`DataScope.Context_${item.key}`, item.title) }))}
          onChange={contextKey => onChange({ ...condition, contextKey })} /></Form.Item>
        : <Form.Item label={setOperations.has(condition.operator) ? t('DataScope.ValueSet', '比较集合') : t('DataScope.Fixed', '固定值')}>
          {setOperations.has(condition.operator) ? selected?.type === 'Boolean'
            ? <Select aria-label={`${t('DataScope.ConditionItem', '条件项 {path}', { path })} ${t('DataScope.ValueSet', '比较集合')}`} mode="multiple" style={{ minWidth: 220 }}
                value={Array.isArray(value) ? value : []} options={[{ value: true, label: t('DataScope.Yes', '是') }, { value: false, label: t('DataScope.No', '否') }]}
                onChange={value => onChange({ ...condition, value })} />
            : selected?.enumValues ? <Select aria-label={`${t('DataScope.ConditionItem', '条件项 {path}', { path })} ${t('DataScope.ValueSet', '比较集合')}`} mode="multiple" style={{ minWidth: 220 }}
                value={Array.isArray(value) ? value : []} options={selected.enumValues.map(item => ({ value: item, label: selected.enumName ? getEnumerationLabel(selected.enumName, item, item) : item }))}
                onChange={value => onChange({ ...condition, value })} />
              : <Select aria-label={`${t('DataScope.ConditionItem', '条件项 {path}', { path })} ${t('DataScope.ValueSet', '比较集合')}`} mode="tags" style={{ minWidth: 220 }}
                  value={Array.isArray(value) ? value.map(String) : []}
                  onChange={value => onChange({ ...condition, value })} />
            : selected?.enumValues ? <Select aria-label={t('DataScope.Fixed', '固定值')} style={{ minWidth: 180 }} value={typeof value === 'string' ? value : undefined}
              options={selected.enumValues.map(item => ({ value: item, label: selected.enumName ? getEnumerationLabel(selected.enumName, item, item) : item }))} onChange={value => onChange({ ...condition, value })} />
              : selected?.type === 'Boolean' ? <Select aria-label={t('DataScope.Fixed', '固定值')} style={{ width: 130 }} value={typeof value === 'boolean' ? value : undefined}
                options={[{ value: true, label: t('DataScope.Yes', '是') }, { value: false, label: t('DataScope.No', '否') }]} onChange={value => onChange({ ...condition, value })} />
                : <Input aria-label={t('DataScope.Fixed', '固定值')} type={selected?.type === 'DateOnly' ? 'date' : 'text'}
                  placeholder={selected?.type === 'DateTime' ? 'YYYY-MM-DDTHH:mm:ss' : numberTypes.has(selected?.type ?? '') ? t('DataScope.Integer', '输入整数') : undefined}
                  value={value === null || value === undefined ? '' : String(value)}
                  onChange={event => onChange({ ...condition, value: event.target.value })} />}
        </Form.Item>)}
    </Space>}
  </div>;
}
