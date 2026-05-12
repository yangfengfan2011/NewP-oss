import { ErrorState } from '@icp-platform/ui';

export default function PersonDetailPage() {
  return (
    <ErrorState message="人员详情已由 Shell 标准 CRUD 详情路由接管，请通过 /organization/persons/:id 访问。" />
  );
}
