import { ErrorState } from '@icp-platform/ui';

export default function PersonCreatePage() {
  return (
    <ErrorState message="人员新建已由标准 CRUD 抽屉接管，请从人员中心列表点击新建。" />
  );
}
