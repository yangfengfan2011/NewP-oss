import { Panel, StatusBadge, SummaryCard } from '@icp-platform/ui';
import type { PersonDetailDto } from '../services/organizationService';

interface PersonEditorFormProps {
  person: PersonDetailDto;
}

const fieldGridStyle: React.CSSProperties = {
  display: 'grid',
  gridTemplateColumns: 'repeat(auto-fit, minmax(180px, 1fr))',
  gap: 16,
};

const fieldCardStyle: React.CSSProperties = {
  border: '1px solid #e2e8f0',
  borderRadius: 14,
  padding: 16,
  background: '#ffffff',
};

export default function PersonEditorForm({ person }: PersonEditorFormProps) {
  return (
    <Panel title="基础信息" meta="首版先集中展示人员主档与组织归属">
      <div className="sh-section-grid">
        <SummaryCard label="人员编号" value={person.personNo} meta="人员主键以外的业务识别码" />
        <SummaryCard label="所属公司" value={person.companyId} meta="当前先展示标识，后续可补名称映射" />
        <SummaryCard label="所属部门" value={person.departmentId} meta="保持与组织树选中上下文一致" />
      </div>

      <div style={{ marginTop: 20, ...fieldGridStyle }}>
        <div style={fieldCardStyle}>
          <div style={{ fontSize: 12, color: '#64748b', textTransform: 'uppercase' }}>姓名</div>
          <div style={{ marginTop: 8, fontSize: 22, fontWeight: 700 }}>{person.name}</div>
        </div>
        <div style={fieldCardStyle}>
          <div style={{ fontSize: 12, color: '#64748b', textTransform: 'uppercase' }}>岗位</div>
          <div style={{ marginTop: 8, fontSize: 16, fontWeight: 600 }}>{person.positionId ?? '未分配岗位'}</div>
        </div>
        <div style={fieldCardStyle}>
          <div style={{ fontSize: 12, color: '#64748b', textTransform: 'uppercase' }}>用工状态</div>
          <div style={{ marginTop: 8 }}>
            <StatusBadge variant="info" label={person.employmentStatus} />
          </div>
        </div>
        <div style={fieldCardStyle}>
          <div style={{ fontSize: 12, color: '#64748b', textTransform: 'uppercase' }}>启用状态</div>
          <div style={{ marginTop: 8 }}>
            <StatusBadge variant={person.isEnabled ? 'success' : 'warning'} label={person.isEnabled ? '启用' : '停用'} />
          </div>
        </div>
      </div>
    </Panel>
  );
}
