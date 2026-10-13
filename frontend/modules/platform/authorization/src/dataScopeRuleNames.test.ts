import { expect, it } from 'vitest';
import { nextRuleName, trimRuleNames, validateRuleNames } from './dataScopeRuleNames';
const node = (id: string, name: string, children: ReturnType<typeof leaf>[] = []) => ({ id, name, children });
const leaf = (id: string, name: string) => ({ id, name, children: [] });

it('allocates an unused name and trims names without mutating the draft', () => {
  expect(nextRuleName('新规则', [{ name: '新规则' }, { name: ' 新规则2 ' }])).toBe('新规则3');
  const root = node('root', ' 根 ', [leaf('child', ' 子 ')]);
  expect(trimRuleNames(root)).toEqual(node('root', '根', [leaf('child', '子')]));
  expect(root.name).toBe(' 根 ');
});

it('rejects sibling duplicates, allows names in separate branches and excludes the current root', () => {
  const root = node('root', '根', [leaf('a', '子'), leaf('b', ' 子 ')]);
  expect(() => validateRuleNames(root, [])).toThrow('同级规则');
  const valid = { ...root, children: [node('a', '甲', [leaf('c', '同名')]), node('b', '乙', [leaf('d', '同名')])] };
  expect(() => validateRuleNames(valid, [valid])).not.toThrow();
  expect(() => validateRuleNames(valid, [leaf('another', ' 根 ')])).toThrow('同级规则');
  expect(() => validateRuleNames(leaf('blank', '  '), [])).toThrow('不能为空');
});
