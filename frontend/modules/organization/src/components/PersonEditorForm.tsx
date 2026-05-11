import { useEffect, useMemo, useState, type FormEvent } from 'react';
import { Button, ErrorState, Input, Panel, Select, StatusBadge, SummaryCard } from '@icp-platform/ui';
import {
  employmentStatusOptions,
  getCompanies,
  getDepartments,
  getEmploymentStatusLabel,
  getPositions,
  type CompanyDto,
  type DepartmentDto,
  type PersonDetailView,
  type PersonUpsertRequest,
  type PositionDto,
} from '../services/organizationService';
import type { PersonDetailRuntimeSection } from '../runtime/personCenterRuntime.types';

interface PersonEditorFormProps {
  person: PersonDetailView;
  onSave: (person: PersonUpsertRequest) => Promise<void>;
  sections: PersonDetailRuntimeSection[];
  resolveTitle: (titleKey: string, fallback: string) => string;
  mode?: 'create' | 'edit' | 'view';
  saveLabel?: string;
  hideSummaryCards?: boolean;
}

interface PersonDraft {
  personNo: string;
  name: string;
  companyId: string;
  departmentId: string;
  positionId: string;
  employmentStatus: string;
  isEnabled: boolean;
}

function buildDraft(person: PersonDetailView): PersonDraft {
  return {
    personNo: person.personNo,
    name: person.name,
    companyId: person.companyId,
    departmentId: person.departmentId,
    positionId: person.positionId ?? '',
    employmentStatus: person.employmentStatus,
    isEnabled: person.isEnabled,
  };
}

function ensureCurrentOption<T extends { value: string; label: string }>(
  options: T[],
  value: string,
  label: string,
): T[] {
  if (!value || options.some((option) => option.value === value)) {
    return options;
  }

  return [{ value, label } as T, ...options];
}

function hasAnyField(fields: string[], ...candidates: string[]): boolean {
  return candidates.some((candidate) => fields.includes(candidate));
}

export default function PersonEditorForm({
  person,
  onSave,
  sections,
  resolveTitle,
  mode = 'edit',
  saveLabel,
  hideSummaryCards = false,
}: PersonEditorFormProps) {
  const isReadOnly = mode === 'view';
  const [draft, setDraft] = useState<PersonDraft>(() => buildDraft(person));
  const [companies, setCompanies] = useState<CompanyDto[]>([]);
  const [departments, setDepartments] = useState<DepartmentDto[]>([]);
  const [positions, setPositions] = useState<PositionDto[]>([]);
  const [companyLoading, setCompanyLoading] = useState(true);
  const [departmentLoading, setDepartmentLoading] = useState(false);
  const [positionLoading, setPositionLoading] = useState(false);
  const [saving, setSaving] = useState(false);
  const [lookupError, setLookupError] = useState<string | null>(null);
  const [saveError, setSaveError] = useState<string | null>(null);

  useEffect(() => {
    // 详情页保存后会把新读模型回填到本组件，确保表单草稿始终贴着真实详情。
    setDraft(buildDraft(person));
    setSaveError(null);
  }, [person]);

  useEffect(() => {
    let cancelled = false;

    async function loadCompanies() {
      try {
        setLookupError(null);
        setCompanyLoading(true);
        const nextCompanies = await getCompanies();
        if (!cancelled) {
          setCompanies(nextCompanies);
        }
      }
      catch (loadError) {
        if (!cancelled) {
          setCompanies([]);
          setLookupError(loadError instanceof Error ? loadError.message : '加载公司选项失败');
        }
      }
      finally {
        if (!cancelled) {
          setCompanyLoading(false);
        }
      }
    }

    void loadCompanies();

    return () => {
      cancelled = true;
    };
  }, []);

  useEffect(() => {
    let cancelled = false;

    if (!draft.companyId) {
      setDepartments([]);
      setDepartmentLoading(false);
      setDraft((current) =>
        current.departmentId || current.positionId
          ? { ...current, departmentId: '', positionId: '' }
          : current,
      );
      return () => {
        cancelled = true;
      };
    }

    async function loadDepartments() {
      try {
        setLookupError(null);
        setDepartmentLoading(true);
        const nextDepartments = await getDepartments(draft.companyId);
        if (cancelled) {
          return;
        }

        setDepartments(nextDepartments);
        // 公司切换后，部门如果不再属于当前公司，就自动清空，避免提交悬空组织归属。
        setDraft((current) => {
          if (current.companyId !== draft.companyId) {
            return current;
          }

          if (!current.departmentId) {
            return current;
          }

          const hasDepartment = nextDepartments.some((item) => item.id === current.departmentId);
          return hasDepartment ? current : { ...current, departmentId: '', positionId: '' };
        });
      }
      catch (loadError) {
        if (!cancelled) {
          setDepartments([]);
          setLookupError(loadError instanceof Error ? loadError.message : '加载部门选项失败');
        }
      }
      finally {
        if (!cancelled) {
          setDepartmentLoading(false);
        }
      }
    }

    void loadDepartments();

    return () => {
      cancelled = true;
    };
  }, [draft.companyId]);

  useEffect(() => {
    let cancelled = false;

    if (!draft.departmentId) {
      setPositions([]);
      setPositionLoading(false);
      setDraft((current) => (current.positionId ? { ...current, positionId: '' } : current));
      return () => {
        cancelled = true;
      };
    }

    async function loadPositions() {
      try {
        setLookupError(null);
        setPositionLoading(true);
        const nextPositions = await getPositions(draft.departmentId);
        if (cancelled) {
          return;
        }

        setPositions(nextPositions);
        setDraft((current) => {
          if (current.departmentId !== draft.departmentId) {
            return current;
          }

          if (!current.positionId) {
            return current;
          }

          const hasPosition = nextPositions.some((item) => item.id === current.positionId);
          return hasPosition ? current : { ...current, positionId: '' };
        });
      }
      catch (loadError) {
        if (!cancelled) {
          setPositions([]);
          setLookupError(loadError instanceof Error ? loadError.message : '加载岗位选项失败');
        }
      }
      finally {
        if (!cancelled) {
          setPositionLoading(false);
        }
      }
    }

    void loadPositions();

    return () => {
      cancelled = true;
    };
  }, [draft.departmentId]);

  const companyOptions = useMemo(
    () =>
      ensureCurrentOption(
        companies.map((company) => ({
          value: company.id,
          label: company.name,
        })),
        draft.companyId,
        person.companyName ?? draft.companyId,
      ),
    [companies, draft.companyId, person.companyName],
  );

  const departmentOptions = useMemo(
    () =>
      ensureCurrentOption(
        departments.map((department) => ({
          value: department.id,
          label: department.name,
        })),
        draft.departmentId,
        person.departmentName ?? draft.departmentId,
      ),
    [departments, draft.departmentId, person.departmentName],
  );

  const positionOptions = useMemo(
    () =>
      ensureCurrentOption(
        positions.map((position) => ({
          value: position.id,
          label: position.name,
        })),
        draft.positionId,
        person.positionName ?? draft.positionId,
      ),
    [person.positionName, positions, draft.positionId],
  );

  async function handleSubmit(event: FormEvent<HTMLFormElement>) {
    event.preventDefault();

    if (isReadOnly) {
      return;
    }

    try {
      setSaving(true);
      setSaveError(null);
      await onSave({
        personNo: draft.personNo.trim(),
        name: draft.name.trim(),
        companyId: draft.companyId || null,
        departmentId: draft.departmentId || null,
        positionId: draft.positionId || null,
        employmentStatus: draft.employmentStatus || null,
        isEnabled: draft.isEnabled,
      });
    }
    catch (submitError) {
      setSaveError(submitError instanceof Error ? submitError.message : '保存人员信息失败');
    }
    finally {
      setSaving(false);
    }
  }

  return (
    <>
      {hideSummaryCards ? null : (
        <div className="sh-section-grid">
          <SummaryCard label="人员编号" value={person.personNo} meta="人员主数据的业务识别码" />
          <SummaryCard
            label="组织归属"
            value={`${person.companyName ?? person.companyId} / ${person.departmentName ?? person.departmentId}`}
            meta={person.positionName ?? person.positionId ?? '未分配岗位'}
          />
          <SummaryCard
            label="当前状态"
            value={<StatusBadge variant={person.isEnabled ? 'success' : 'warning'} label={person.isEnabled ? '启用' : '停用'} />}
            meta={getEmploymentStatusLabel(person.employmentStatus)}
          />
        </div>
      )}

      {lookupError ? <ErrorState message={lookupError} /> : null}
      {saveError ? <ErrorState message={saveError} /> : null}

      <form id="person-editor-form" onSubmit={(event) => void handleSubmit(event)} style={{ marginTop: 8 }}>
        {!isReadOnly ? (
          <div
            style={{
              marginBottom: 12,
              color: '#b45309',
              fontSize: 12,
              fontWeight: 600,
            }}
          >
            * 必填
          </div>
        ) : null}
        {(sections.length > 0 ? sections : [
          { key: 'basic', titleKey: 'Section_Basic', fields: ['PersonNo', 'ENAME', 'CompanyId', 'DepartmentId', 'PositionId', 'EmploymentStatus'] },
        ]).map((section, index) => (
          <Panel
            key={section.key}
            title={resolveTitle(section.titleKey, section.key)}
            compact
            actions={!isReadOnly && index === 0 ? (
              <Button size="sm" variant="primary" type="submit" form="person-editor-form" disabled={saving}>
                {saving ? '保存中...' : (saveLabel ?? (mode === 'create' ? '保存并关闭' : '保存修改'))}
              </Button>
            ) : undefined}
          >
            <div
              style={{
                display: 'grid',
                gridTemplateColumns: 'repeat(auto-fit, minmax(180px, 1fr))',
                gap: 12,
              }}
            >
              {section.fields.includes('PersonNo') ? (
                <Input
                  id="person-no"
                  label="工号 *"
                  value={draft.personNo}
                  readOnly={isReadOnly || mode !== 'create'}
                  placeholder={mode === 'create' ? '输入人员工号' : '工号首轮保持只读'}
                  onChange={(event) => setDraft((current) => ({ ...current, personNo: event.target.value }))}
                  style={{ minHeight: 40, fontSize: 14 }}
                  required
                />
              ) : null}
              {hasAnyField(section.fields, 'ENAME', 'Name') ? (
                <Input
                  id="person-name"
                  label="姓名 *"
                  value={draft.name}
                  readOnly={isReadOnly}
                  onChange={(event) => setDraft((current) => ({ ...current, name: event.target.value }))}
                  placeholder="输入人员姓名"
                  style={{ minHeight: 40, fontSize: 14 }}
                  required
                />
              ) : null}
              {section.fields.includes('CompanyId') ? (
                <Select
                  label="所属公司"
                  value={draft.companyId}
                  onChange={(event) =>
                    setDraft((current) => ({
                      ...current,
                      companyId: event.target.value,
                      departmentId: '',
                      positionId: '',
                    }))
                  }
                  options={companyOptions}
                  placeholder="请选择公司"
                  disabled={isReadOnly || companyLoading}
                  style={{ minHeight: 40, fontSize: 14 }}
                />
              ) : null}
              {section.fields.includes('DepartmentId') ? (
                <Select
                  label="所属部门"
                  value={draft.departmentId}
                  onChange={(event) =>
                    setDraft((current) => ({
                      ...current,
                      departmentId: event.target.value,
                      positionId: '',
                    }))
                  }
                  options={departmentOptions}
                  placeholder={draft.companyId ? '请选择部门' : '请先选择公司'}
                  disabled={isReadOnly || !draft.companyId || departmentLoading}
                  style={{ minHeight: 40, fontSize: 14 }}
                />
              ) : null}
              {section.fields.includes('PositionId') ? (
                <Select
                  label="岗位"
                  value={draft.positionId}
                  onChange={(event) => setDraft((current) => ({ ...current, positionId: event.target.value }))}
                  options={positionOptions}
                  placeholder={draft.departmentId ? '请选择岗位' : '可先保持空白'}
                  disabled={isReadOnly || !draft.departmentId || positionLoading}
                  style={{ minHeight: 40, fontSize: 14 }}
                />
              ) : null}
              {section.fields.includes('EmploymentStatus') ? (
                <Select
                  label="用工状态"
                  value={draft.employmentStatus}
                  onChange={(event) => setDraft((current) => ({ ...current, employmentStatus: event.target.value }))}
                  options={employmentStatusOptions}
                  placeholder="请选择状态"
                  disabled={isReadOnly}
                  style={{ minHeight: 40, fontSize: 14 }}
                />
              ) : null}
            </div>
          </Panel>
        ))}
      </form>
    </>
  );
}
