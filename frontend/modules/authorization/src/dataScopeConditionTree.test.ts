import { expect, it } from 'vitest';
import type { DataScopeCondition } from './DataScopeConditionEditor';
import { asConditionGroup, groupConditions, moveConditionOut, removeCondition, ungroupCondition } from './dataScopeConditionTree';

const leaf = (value: string): DataScopeCondition => ({ type: 'attribute', field: 'ENAME', operator: 'equal', value });
const group = (operator: string, children: DataScopeCondition[]): DataScopeCondition => ({ type: 'group', field: '', operator, value: null, children });
const a = leaf('A'), b = leaf('B'), c = leaf('C'), d = leaf('D');

it('wraps legacy single conditions without mutating them', () => {
  expect(asConditionGroup(a)).toEqual(group('and', [a]));
  expect(a).toEqual(leaf('A'));
});
it('groups same-level selections in their original order and preserves other branches', () => {
  const original = group('and', [a, b, c, d]);
  expect(groupConditions(original, ['1.3', '1.1'], 'or')).toEqual(group('and', [group('or', [a, c]), b, d]));
  expect(original.children).toEqual([a, b, c, d]);
  const nested = group('or', [a, group('and', [b, c, d])]);
  expect(groupConditions(nested, ['1.2.1', '1.2.3'], 'or')).toEqual(group('or', [a, group('and', [group('or', [b, d]), c])]));
});
it('rejects cross-level or single-item grouping', () => {
  const original = group('and', [a, group('or', [b, c])]);
  expect(() => groupConditions(original, ['1.1', '1.2.1'], 'or')).toThrow();
  expect(() => groupConditions(original, ['1.1'], 'and')).toThrow();
  expect(() => groupConditions(original, ['1.1', '1.1'], 'and')).toThrow();
});
it('ungroups without losing children or changing the parent operator', () => {
  expect(ungroupCondition(group('and', [a, group('or', [b, c]), d]), '1.2')).toEqual(group('and', [a, b, c, d]));
});
it('moves a child group beside its parent and removes an emptied parent', () => {
  const inner = group('or', [b, c]);
  expect(moveConditionOut(group('or', [group('and', [a, inner]), d]), '1.1.2'))
    .toEqual(group('or', [group('and', [a]), inner, d]));
  expect(moveConditionOut(group('and', [group('or', [b]), c]), '1.1.1')).toEqual(group('and', [b, c]));
  expect(() => moveConditionOut(group('and', [a, b]), '1.1')).toThrow();
});
it('removes empty nested groups but retains an empty overall group as an editable draft', () => {
  expect(removeCondition(group('and', [a, group('or', [group('and', [b])])]), '1.2.1.1')).toEqual(group('and', [a]));
  expect(removeCondition(group('or', [a]), '1.1')).toEqual(group('or', []));
});
