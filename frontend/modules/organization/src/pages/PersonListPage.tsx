import { useEffect, useMemo, useState } from 'react';
import { EmptyState, PageHeader, Panel, Select, SummaryCard, TableWrap } from '@icp-platform/ui';
import { getCompanies, type CompanyDto } from '../services/organizationService';

interface PersonListItem {
  id: string;
  personNo: string;
  name: string;
  companyName: string;
  departmentName: string;
}

const personColumns = [
  { key: 'personNo', title: '工号' },
  { key: 'name', title: '姓名' },
  { key: 'companyName', title: '公司' },
  { key: 'departmentName', title: '部门' },
];

export default function PersonListPage() {
  const [companies, setCompanies] = useState<CompanyDto[]>([]);
  const [keyword, setKeyword] = useState('');
  const [companyId, setCompanyId] = useState('');

  useEffect(() => {
    let cancelled = false;

    void getCompanies()
      .then((nextCompanies) => {
        if (!cancelled) {
          setCompanies(nextCompanies);
        }
      })
      .catch(() => {
        if (!cancelled) {
          setCompanies([]);
        }
      });

    return () => {
      cancelled = true;
    };
  }, []);

  const rows = useMemo<PersonListItem[]>(() => [], []);

  return (
    <div className="sh-page">
      <PageHeader
        eyebrow="组织结构"
        title="人员中心"
        description="首版先把筛选区、列表容器和详情入口位置固定下来，后续再把绑定账号与详情联动补齐。"
      />

      <div className="sh-section-grid">
        <SummaryCard label="当前公司筛选" value={companyId || '全部公司'} meta="列表接口接入前先固化筛选骨架" />
        <SummaryCard label="关键字" value={keyword || '未输入'} meta="支持按姓名 / 工号收窄" />
        <SummaryCard label="列表状态" value={rows.length} meta="当前无后端列表接口，先展示空态容器" />
      </div>

      <Panel title="筛选区" meta="后续人员列表 API 接入后复用这套筛选布局">
        <div
          style={{
            display: 'grid',
            gridTemplateColumns: 'repeat(auto-fit, minmax(220px, 1fr))',
            gap: 16,
          }}
        >
          <label style={{ display: 'grid', gap: 8 }}>
            <span style={{ fontSize: 12, color: '#64748b', textTransform: 'uppercase' }}>关键字</span>
            <input
              value={keyword}
              onChange={(event) => setKeyword(event.target.value)}
              placeholder="按工号或姓名筛选"
              className="sh-form-input"
            />
          </label>

          <Select
            label="所属公司"
            value={companyId}
            onChange={(event) => setCompanyId(event.target.value)}
            options={companies.map((company) => ({
              value: company.id,
              label: company.name,
            }))}
          />
        </div>
      </Panel>

      <Panel title="人员列表" meta="保留空态容器，保证页面骨架和后续表格承载位已就绪">
        {rows.length === 0 ? (
          <EmptyState message="暂无可展示的人员列表数据，等待后续列表接口接入。" />
        ) : (
          <TableWrap columns={personColumns} data={rows} rowKey={(item) => item.id} />
        )}
      </Panel>
    </div>
  );
}
