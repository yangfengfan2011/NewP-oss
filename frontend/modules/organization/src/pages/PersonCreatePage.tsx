import { useMemo, useState } from 'react';
import { applyStandardCrudLookupValueChange, useStandardCrudLookupRuntime } from '@icp-platform/sdk';
import { PageHeader, StandardCrudForm } from '@icp-platform/ui';
import { createPerson } from '../services/organizationService';
import { personStandardCrudResource } from '../standard-crud/personStandardCrudResource';

function createInitialValues(): Record<string, unknown> {
  return {
    personNo: '',
    name: '',
    companyId: '',
    departmentId: '',
    positionId: '',
    employmentStatus: 'FullTime',
    isEnabled: true,
  };
}

function resolveCreateFields() {
  // 资源注册中的工号在详情编辑态只读；创建页需要保留同一字段契约但开放初次录入。
  return (personStandardCrudResource.fields ?? []).map((field) =>
    field.key === 'personNo' ? { ...field, readOnly: false } : field,
  );
}

export default function PersonCreatePage() {
  const fields = useMemo(() => resolveCreateFields(), []);
  const formSections = personStandardCrudResource.formSections ?? [];
  const [values, setValues] = useState<Record<string, unknown>>(() => createInitialValues());
  const [saving, setSaving] = useState(false);
  const [saveError, setSaveError] = useState<string | null>(null);
  const { lookupOptions, lookupLoading, lookupErrors } = useStandardCrudLookupRuntime({
    fields,
    providers: personStandardCrudResource.lookupProviders,
    values,
  });

  function handleChange(fieldKey: string, value: unknown) {
    setValues((current) => applyStandardCrudLookupValueChange(current, fieldKey, value, fields));
  }

  async function handleCreate() {
    try {
      setSaving(true);
      setSaveError(null);
      const mutationValues = personStandardCrudResource.buildMutationValues
        ? personStandardCrudResource.buildMutationValues(values)
        : values;
      const created = await createPerson(mutationValues);
      // 新建页只负责兼容路由入口；创建成功后交还标准详情页维护后续编辑状态。
      window.location.assign(`/organization/persons/${created.id}`);
    } catch (submitError) {
      setSaveError(submitError instanceof Error ? submitError.message : '新建人员失败');
    } finally {
      setSaving(false);
    }
  }

  return (
    <div className="sh-page">
      <PageHeader
        eyebrow="组织结构"
        title="新建人员"
        description="新建页使用标准 CRUD 字段定义提交，lookup 加载由标准运行时接管。"
      />

      <div style={{ marginBottom: 12, color: '#b45309', fontSize: 12, fontWeight: 600 }}>
        * 必填
      </div>

      <StandardCrudForm
        formId="organization-person-create-form"
        mode="create"
        fields={fields}
        sections={formSections}
        values={values}
        lookupOptions={lookupOptions}
        lookupLoading={lookupLoading}
        lookupErrors={lookupErrors}
        onChange={handleChange}
        onSubmit={handleCreate}
        saving={saving}
        error={saveError}
        saveLabel="保存并进入详情"
        titleForSection={(titleKey) => titleKey}
      />
    </div>
  );
}
