import type { StandardListViewRendererProps } from '@icp-platform/sdk';

export default function PersonListStandardListEntry(_props: StandardListViewRendererProps) {
  return (
    <div className="sh-error-state">
      人员中心标准列表入口已迁移到 standardCrudResources，请通过 runtime metadata 入口访问。
    </div>
  );
}
