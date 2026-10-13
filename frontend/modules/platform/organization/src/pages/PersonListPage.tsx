import { ErrorState } from '@icp-platform/ui';

export default function PersonListPage() {
  return (
    <ErrorState message="人员中心主列表已由 Shell 标准 CRUD 宿主接管，请通过 /organization/persons 的 metadata 入口访问。" />
  );
}
