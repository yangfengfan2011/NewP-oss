import { useEffect, useMemo, useState } from 'react';
import { EmptyState, PageHeader, Panel, Select, SummaryCard, TableWrap } from '@icp-platform/ui';
import {
  getCompanies,
  getDepartments,
  getPositions,
  type CompanyDto,
  type DepartmentDto,
  type PositionDto,
} from '../services/organizationService';

const positionColumns = [
  { key: 'code', title: '岗位编码' },
  { key: 'name', title: '岗位名称' },
  { key: 'employmentStatus', title: '用工状态' },
  { key: 'sortOrder', title: '排序' },
];

export default function PositionManagementPage() {
  const [companies, setCompanies] = useState<CompanyDto[]>([]);
  const [departments, setDepartments] = useState<DepartmentDto[]>([]);
  const [positions, setPositions] = useState<PositionDto[]>([]);
  const [companyId, setCompanyId] = useState('');
  const [departmentId, setDepartmentId] = useState('');

  useEffect(() => {
    let cancelled = false;

    void getCompanies()
      .then((nextCompanies) => {
        if (!cancelled) {
          setCompanies(nextCompanies);
          setCompanyId(nextCompanies[0]?.id ?? '');
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

  useEffect(() => {
    if (!companyId) {
      setDepartments([]);
      setDepartmentId('');
      return;
    }

    let cancelled = false;

    void getDepartments(companyId)
      .then((nextDepartments) => {
        if (!cancelled) {
          setDepartments(nextDepartments);
          setDepartmentId((currentDepartmentId) =>
            currentDepartmentId && nextDepartments.some((item) => item.id === currentDepartmentId)
              ? currentDepartmentId
              : (nextDepartments[0]?.id ?? ''),
          );
        }
      })
      .catch(() => {
        if (!cancelled) {
          setDepartments([]);
          setDepartmentId('');
        }
      });

    return () => {
      cancelled = true;
    };
  }, [companyId]);

  useEffect(() => {
    if (!departmentId) {
      setPositions([]);
      return;
    }

    let cancelled = false;

    void getPositions(departmentId)
      .then((nextPositions) => {
        if (!cancelled) {
          setPositions(nextPositions);
        }
      })
      .catch(() => {
        if (!cancelled) {
          setPositions([]);
        }
      });

    return () => {
      cancelled = true;
    };
  }, [departmentId]);

  const currentDepartmentName = useMemo(
    () => departments.find((item) => item.id === departmentId)?.name ?? '未选择部门',
    [departments, departmentId],
  );

  return (
    <div className="sh-page">
      <PageHeader
        eyebrow="组织结构"
        title="岗位管理"
        description="岗位页首版用最小筛选和表格承载岗位视图，保持与组织树和人员页一致的组织上下文。"
      />

      <div className="sh-section-grid">
        <SummaryCard label="公司数量" value={companies.length} meta="岗位归属以公司/部门组织上下文展开" />
        <SummaryCard label="当前部门" value={currentDepartmentName} meta="用于限定岗位列表范围" />
        <SummaryCard label="岗位数量" value={positions.length} meta="当前部门下岗位汇总" />
      </div>

      <Panel title="筛选区" meta="岗位管理页首版固定公司与部门双筛选">
        <div
          style={{
            display: 'grid',
            gridTemplateColumns: 'repeat(auto-fit, minmax(220px, 1fr))',
            gap: 16,
          }}
        >
          <Select
            label="所属公司"
            value={companyId}
            onChange={(event) => setCompanyId(event.target.value)}
            options={companies.map((company) => ({
              value: company.id,
              label: company.name,
            }))}
          />
          <Select
            label="所属部门"
            value={departmentId}
            onChange={(event) => setDepartmentId(event.target.value)}
            options={departments.map((department) => ({
              value: department.id,
              label: department.name,
            }))}
          />
        </div>
      </Panel>

      <Panel title="岗位列表" meta="当前部门下的岗位视图">
        {positions.length === 0 ? (
          <EmptyState message="当前筛选下暂无岗位数据。" />
        ) : (
          <TableWrap columns={positionColumns} data={positions} rowKey={(item) => item.id} />
        )}
      </Panel>
    </div>
  );
}
