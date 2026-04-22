import { StandardListPage } from '@icp-platform/ui';
import PersonListStandardListEntry from '../standard-list/PersonListStandardListEntry';

export default function PersonListPage() {
  // Task 4 之后运行时主入口改由 metadata + host/custom registry 承接；
  // 这里继续保留兼容包装，避免现有直接 import PersonListPage 的测试和回退路径失效。
  return (
    <PersonListStandardListEntry viewKey="organization.person.list" viewMetadata={{}}>
      {(definition) => <StandardListPage {...definition} advancedFilterTitle="高级筛选" />}
    </PersonListStandardListEntry>
  );
}
