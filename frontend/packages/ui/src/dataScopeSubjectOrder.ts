import type { DataScopeSubject } from './DataScopeSubjectEditor';

/** 服务端集合顺序不作为优先顺序；回显始终按已保存的优先级降序。 */
export const orderDataScopeSubjects = (subjects: DataScopeSubject[]): DataScopeSubject[] =>
  [...subjects].sort((left, right) => right.priority - left.priority);

/** 用户排列的顺序映射为唯一优先级，靠前项优先，末项为零。 */
export const rankDataScopeSubjects = (subjects: DataScopeSubject[]): DataScopeSubject[] =>
  subjects.map((subject, index) => ({ ...subject, priority: subjects.length - index - 1 }));
