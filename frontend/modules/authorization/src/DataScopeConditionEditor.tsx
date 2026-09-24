import { Alert, Button, Form, Input, Select, Space, Typography } from 'antd';

export interface DataScopeCondition {
  type: string; field: string; operator: string; value: unknown; contextKey?: string | null;
  children?: DataScopeCondition[] | null; key?: string | null;
}
export interface DataScopeResourceField { name: string; type: string; title?: string; enumValues?: string[] | null }
export interface DataScopeContextValue { key: string; title: string; valueType: string }
export interface DataScopeBusinessType { key: string; title: string; modelKeys: string[]; hasReadContribution: boolean }

const operations = [
  ['equal', '等于'], ['notEqual', '不等于'], ['greaterThan', '大于'], ['greaterThanOrEqual', '大于等于'],
  ['lessThan', '小于'], ['lessThanOrEqual', '小于等于'], ['contains', '包含'], ['startsWith', '开头是'],
  ['endsWith', '结尾是'], ['in', '属于'], ['notIn', '不属于'], ['isNull', '为空'], ['notNull', '不为空'],
] as const;
const nullOperations = new Set(['isNull', 'notNull']);
const setOperations = new Set(['in', 'notIn']);
const orderedOperations = new Set(['greaterThan', 'greaterThanOrEqual', 'lessThan', 'lessThanOrEqual']);
const textOperations = new Set(['contains', 'startsWith', 'endsWith']);
const orderedTypes = new Set(['Int32', 'Int64', 'Decimal', 'Double', 'Single', 'DateOnly', 'DateTime']);
const numberTypes = new Set(['Int32', 'Int64', 'Decimal', 'Double', 'Single']);
const fixedValue = (value: unknown): string => {
  if (value === null) return '空值';
  if (value === '') return '空文本';
  if (typeof value === 'string' || typeof value === 'number' || typeof value === 'boolean') return String(value);
  if (Array.isArray(value)) return value.length ? value.map(fixedValue).join('、') : '空集合';
  return '已配置值';
};

/** 只描述既有条件配置；祖先摘要不重复运行裁决或解释服务器事实。 */
export const describeDataScopeCondition = (condition: DataScopeCondition, contexts: DataScopeContextValue[],
  businessTypes: DataScopeBusinessType[], fields: DataScopeResourceField[] = []): string => {
  if (condition.type === 'group') {
    const joined = (condition.children ?? []).map(child => describeDataScopeCondition(child, contexts, businessTypes, fields))
      .join(condition.operator === 'or' ? ' 或 OR ' : ' 且 AND ');
    return `（${joined}）`;
  }
  if (condition.type === 'business') {
    const business = businessTypes.find(item => item.key === condition.key);
    return `${business?.title ?? condition.key ?? '未选业务条件'}${business?.hasReadContribution === false ? '（无读取贡献）' : ''}`;
  }
  const title = fields.find(field => field.name === condition.field)?.title ?? condition.field;
  const operation = operations.find(([key]) => key === condition.operator)?.[1] ?? condition.operator;
  if (nullOperations.has(condition.operator)) return `${title} ${operation}`;
  const value = condition.contextKey
    ? `当前身份值：${contexts.find(item => item.key === condition.contextKey)?.title ?? condition.contextKey}`
    : fixedValue(condition.value);
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
  const selected = fields.find(field => field.name === condition.field);
  const availableContexts = contexts.filter(value => bindings.some(binding => binding.modelKey === modelKey
    && binding.field === condition.field && binding.contextKey === value.key && value.valueType === selected?.type));
  const availableOperations = operations.filter(([key]) => !textOperations.has(key) || selected?.type === 'String' && !selected.enumValues)
    .filter(([key]) => !orderedOperations.has(key) || orderedTypes.has(selected?.type ?? ''));
  const business = businessTypes.find(type => type.key === condition.key);
  const changeChild = (index: number, value: DataScopeCondition) => onChange({ ...condition,
    children: condition.children?.map((child, childIndex) => childIndex === index ? value : child) });
  const addChild = (value: DataScopeCondition) => onChange({ ...condition, children: [...condition.children ?? [], value] });
  const valueSource = condition.contextKey ? 'context' : 'fixed';
  const value = condition.value;
  return <div role="group" aria-label={`条件项 ${path}`} style={{ border: '1px solid var(--ant-color-border-secondary)', borderRadius: 6,
    padding: 12, marginBottom: 12, minWidth: 0 }}>
    {errorPath === path && errorMessage && <Alert type="error" showIcon message={errorMessage} style={{ marginBottom: 8 }} />}
    <Space wrap align="start" style={{ marginBottom: 8 }}>
      <Typography.Text strong>条件 {path}</Typography.Text>
      {!leafOnly && condition.type !== 'group' && <Button size="small" onClick={() => onChange(group('and', condition))}>转换为 AND 组合</Button>}
      {!leafOnly && condition.type !== 'group' && <Button size="small" onClick={() => onChange(group('or', condition))}>转换为 OR 组合</Button>}
      {onRemove && <Button size="small" danger onClick={onRemove}>删除条件</Button>}
    </Space>
    {condition.type === 'group' ? <>
      <Form.Item label="组合方式"><Select aria-label={`条件项 ${path} 组合方式`} style={{ width: 130 }} value={condition.operator}
        options={[{ value: 'and', label: '且 AND' }, { value: 'or', label: '或 OR' }]}
        onChange={operator => onChange({ ...condition, operator })} /></Form.Item>
      {condition.children?.map((child, index) => <DataScopeConditionEditor key={index} condition={child} path={`${path}.${index + 1}`}
        fields={fields} modelKey={modelKey} contexts={contexts} bindings={bindings} businessTypes={businessTypes}
        errorPath={errorPath} errorMessage={errorMessage} onChange={next => changeChild(index, next)} onRemove={() => onChange({ ...condition,
          children: condition.children?.filter((_, childIndex) => childIndex !== index) })} />)}
      {(condition.children?.length ?? 0) === 0 && <Alert type="warning" showIcon message="组合中至少添加一个条件。" />}
      <Space wrap><Button size="small" onClick={() => addChild(attribute(fields[0]?.name))}>添加属性条件</Button>
        <Button size="small" disabled={businessTypes.length === 0} onClick={() => addChild({ type: 'business', field: '', operator: '', value: null,
          key: businessTypes[0]?.key })}>添加业务条件</Button>
        <Button size="small" onClick={() => addChild(group('and', attribute(fields[0]?.name)))}>添加 AND 组合</Button>
        <Button size="small" onClick={() => addChild(group('or', attribute(fields[0]?.name)))}>添加 OR 组合</Button></Space>
    </> : condition.type === 'business' ? <Space wrap align="start">
      <Form.Item label="业务条件"><Select aria-label={`条件项 ${path} 业务条件`} style={{ minWidth: 220 }} value={condition.key}
        options={businessTypes.map(type => ({ value: type.key, label: type.title }))}
        onChange={key => onChange({ ...condition, key })} /></Form.Item>
      <Typography.Text type="secondary">{business?.hasReadContribution ? '提供读取贡献' : '无读取贡献；完整判断仍生效'}</Typography.Text>
    </Space> : <Space wrap align="start">
      <Form.Item label="直接属性"><Select aria-label={path === '1' ? '直接属性' : `条件项 ${path} 直接属性`} style={{ minWidth: 180 }} value={condition.field || undefined}
        options={fields.map(field => ({ value: field.name, label: field.title ?? field.name }))}
        onChange={field => onChange({ ...condition, field, operator: 'equal', value: '', contextKey: null })} /></Form.Item>
      <Form.Item label="比较"><Select aria-label={`条件项 ${path} 比较`} style={{ minWidth: 130 }} value={condition.operator}
        options={availableOperations.map(([key, label]) => ({ value: key, label }))}
        onChange={operator => onChange({ ...condition, operator, value: setOperations.has(operator) ? [] : nullOperations.has(operator) ? null : '', contextKey: null })} /></Form.Item>
      {!nullOperations.has(condition.operator) && <Form.Item label="值来源"><Select aria-label={path === '1' ? '值来源' : `条件项 ${path} 值来源`} style={{ width: 150 }} value={valueSource}
        options={[{ value: 'fixed', label: '固定值' }, { value: 'context', label: '当前身份值', disabled: availableContexts.length === 0 || setOperations.has(condition.operator) }]}
        onChange={source => onChange({ ...condition, value: source === 'context' ? null : '',
          contextKey: source === 'context' ? availableContexts[0]?.key : null })} /></Form.Item>}
      {!nullOperations.has(condition.operator) && (condition.contextKey ? <Form.Item label="当前身份值">
        <Select aria-label={path === '1' ? '当前身份值' : `条件项 ${path} 当前身份值`} style={{ minWidth: 160 }} value={condition.contextKey}
          options={availableContexts.map(item => ({ value: item.key, label: item.title }))}
          onChange={contextKey => onChange({ ...condition, contextKey })} /></Form.Item>
        : <Form.Item label={setOperations.has(condition.operator) ? '比较集合' : '固定值'}>
          {setOperations.has(condition.operator) ? selected?.type === 'Boolean'
            ? <Select aria-label={`条件项 ${path} 比较集合`} mode="multiple" style={{ minWidth: 220 }}
                value={Array.isArray(value) ? value : []} options={[{ value: true, label: '是' }, { value: false, label: '否' }]}
                onChange={value => onChange({ ...condition, value })} />
            : selected?.enumValues ? <Select aria-label={`条件项 ${path} 比较集合`} mode="multiple" style={{ minWidth: 220 }}
                value={Array.isArray(value) ? value : []} options={selected.enumValues.map(item => ({ value: item, label: item }))}
                onChange={value => onChange({ ...condition, value })} />
              : <Select aria-label={`条件项 ${path} 比较集合`} mode="tags" style={{ minWidth: 220 }}
                  value={Array.isArray(value) ? value.map(String) : []}
                  onChange={value => onChange({ ...condition, value })} />
            : selected?.enumValues ? <Select aria-label="固定值" style={{ minWidth: 180 }} value={typeof value === 'string' ? value : undefined}
              options={selected.enumValues.map(item => ({ value: item, label: item }))} onChange={value => onChange({ ...condition, value })} />
              : selected?.type === 'Boolean' ? <Select aria-label="固定值" style={{ width: 130 }} value={typeof value === 'boolean' ? value : undefined}
                options={[{ value: true, label: '是' }, { value: false, label: '否' }]} onChange={value => onChange({ ...condition, value })} />
                : <Input aria-label="固定值" type={selected?.type === 'DateOnly' ? 'date' : 'text'}
                  placeholder={selected?.type === 'DateTime' ? 'YYYY-MM-DDTHH:mm:ss' : numberTypes.has(selected?.type ?? '') ? '输入整数' : undefined}
                  value={value === null || value === undefined ? '' : String(value)}
                  onChange={event => onChange({ ...condition, value: event.target.value })} />}
        </Form.Item>)}
    </Space>}
  </div>;
}
