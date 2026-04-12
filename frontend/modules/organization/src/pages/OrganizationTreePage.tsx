import { useEffect, useMemo, useState } from 'react';
import { EmptyState, ErrorState, LoadingState, PageHeader, Panel, SummaryCard } from '@icp-platform/ui';
import OrganizationTreePanel, { type OrganizationTreeNode } from '../components/OrganizationTreePanel';
import { getCompanies, getDepartments, type CompanyDto, type DepartmentDto } from '../services/organizationService';

interface SelectedOrganization {
  title: string;
  subtitle: string;
  status: string;
}

export default function OrganizationTreePage() {
  const [companies, setCompanies] = useState<CompanyDto[]>([]);
  const [departmentsByCompany, setDepartmentsByCompany] = useState<Record<string, DepartmentDto[]>>({});
  const [selectedNode, setSelectedNode] = useState<OrganizationTreeNode | null>(null);
  const [error, setError] = useState<string | null>(null);
  const [loading, setLoading] = useState(true);

  useEffect(() => {
    let cancelled = false;

    async function load() {
      try
      {
        setLoading(true);
        setError(null);

        const nextCompanies = await getCompanies();
        const departmentEntries = await Promise.all(
          nextCompanies.map(async (company) => [company.id, await getDepartments(company.id)] as const),
        );

        if (cancelled) {
          return;
        }

        setCompanies(nextCompanies);
        setDepartmentsByCompany(Object.fromEntries(departmentEntries));
      }
      catch (loadError)
      {
        if (!cancelled) {
          setError(loadError instanceof Error ? loadError.message : '加载组织树失败');
        }
      }
      finally
      {
        if (!cancelled) {
          setLoading(false);
        }
      }
    }

    void load();

    return () => {
      cancelled = true;
    };
  }, []);

  const treeNodes = useMemo<OrganizationTreeNode[]>(() =>
    companies.map((company) => ({
      key: `company:${company.id}`,
      label: company.name,
      meta: company.code,
      kind: 'company',
      isEnabled: company.isEnabled,
      children: buildDepartmentNodes(company.id, departmentsByCompany[company.id] ?? []),
    })), [companies, departmentsByCompany]);

  const summary = useMemo<SelectedOrganization>(() => {
    if (!selectedNode) {
      return {
        title: '请选择公司或部门',
        subtitle: '当前右侧区域会跟随树节点展示组织摘要',
        status: '待选择',
      };
    }

    return {
      title: selectedNode.label,
      subtitle: selectedNode.meta,
      status: selectedNode.isEnabled ? '启用' : '停用',
    };
  }, [selectedNode]);

  const departmentCount = useMemo(
    () => Object.values(departmentsByCompany).reduce((total, items) => total + items.length, 0),
    [departmentsByCompany],
  );

  return (
    <div className="sh-page">
      <PageHeader
        eyebrow="组织结构"
        title="组织树"
        description="组织结构模块首版先提供公司/部门结构视图，为人员中心和岗位管理页提供统一组织上下文。"
      />

      <div className="sh-section-grid">
        <SummaryCard label="公司数量" value={companies.length} meta="组织树根节点" />
        <SummaryCard label="部门数量" value={departmentCount} meta="跨公司部门汇总" />
        <SummaryCard label="当前选中" value={selectedNode ? selectedNode.label : '未选择'} meta="用于右侧详情摘要" />
      </div>

      {loading ? (
        <LoadingState message="正在加载公司和部门..." />
      ) : error ? (
        <ErrorState message={error} />
      ) : (
        <div
          style={{
            display: 'grid',
            gridTemplateColumns: 'minmax(320px, 1.2fr) minmax(280px, 0.8fr)',
            gap: 20,
          }}
        >
          <OrganizationTreePanel
            nodes={treeNodes}
            selectedKey={selectedNode?.key}
            onSelect={setSelectedNode}
          />

          <Panel title="组织摘要" meta="为后续人员和岗位页提供统一上下文">
            {treeNodes.length === 0 ? (
              <EmptyState message="当前没有可展示的公司或部门。" />
            ) : (
              <div
                style={{
                  display: 'grid',
                  gap: 16,
                }}
              >
                <div>
                  <div style={{ fontSize: 12, color: '#64748b', textTransform: 'uppercase' }}>名称</div>
                  <div style={{ marginTop: 8, fontSize: 22, fontWeight: 700 }}>{summary.title}</div>
                </div>
                <div>
                  <div style={{ fontSize: 12, color: '#64748b', textTransform: 'uppercase' }}>说明</div>
                  <div style={{ marginTop: 8, color: '#334155' }}>{summary.subtitle}</div>
                </div>
                <div>
                  <div style={{ fontSize: 12, color: '#64748b', textTransform: 'uppercase' }}>状态</div>
                  <div style={{ marginTop: 8, color: '#0f172a', fontWeight: 600 }}>{summary.status}</div>
                </div>
              </div>
            )}
          </Panel>
        </div>
      )}
    </div>
  );
}

function buildDepartmentNodes(
  companyId: string,
  departments: DepartmentDto[],
  parentDepartmentId: string | null = null,
): OrganizationTreeNode[] {
  return departments
    .filter((department) => (department.parentDepartmentId ?? null) === parentDepartmentId)
    .sort((left, right) => left.sortOrder - right.sortOrder || left.name.localeCompare(right.name))
    .map((department) => ({
      key: `department:${department.id}`,
      label: department.name,
      meta: `${department.code} · ${companyId}`,
      kind: 'department',
      isEnabled: department.isEnabled,
      children: buildDepartmentNodes(companyId, departments, department.id),
    }));
}
