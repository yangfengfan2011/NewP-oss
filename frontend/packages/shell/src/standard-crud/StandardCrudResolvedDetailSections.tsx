import type { ReactNode } from 'react';
import type {
  StandardCrudResourceRegistration,
} from '@icp-platform/sdk';
import { DetailPage } from '@icp-platform/ui/crud-detail';
import type {
  StandardCrudResolvedDetailFieldSchema,
  StandardCrudResolvedDetailSchema,
} from '../metadata/standardCrudSchemaResolver';

interface StandardCrudResolvedDetailSectionsProps {
  schema: StandardCrudResolvedDetailSchema;
  detailFields: Record<string, unknown>;
  registration: Pick<StandardCrudResourceRegistration, 'detailFieldRenderers' | 'enumOptions'>;
  textFor: (key: string, fallback: string) => string;
}

function formatStandardValue(
  field: StandardCrudResolvedDetailFieldSchema,
  value: unknown,
  registration: Pick<StandardCrudResourceRegistration, 'enumOptions'>,
): ReactNode {
  if (field.rendererKey === 'standard.enum') {
    const option = field.enumKey
      ? registration.enumOptions?.[field.enumKey]?.find((item) => item.value === value)
      : undefined;
    if (option) {
      return option.label;
    }
  }

  if (value === null || value === undefined || value === '') {
    return '-';
  }
  if (field.rendererKey === 'standard.boolean') {
    return value === true ? '是' : value === false ? '否' : String(value);
  }

  return String(value);
}

/**
 * 统一渲染严格解析后的普通详情分区；Drawer 与完整详情页共享此组件，避免再次从 Model 表单推断展示结构。
 */
export default function StandardCrudResolvedDetailSections({
  schema,
  detailFields,
  registration,
  textFor,
}: StandardCrudResolvedDetailSectionsProps) {
  return schema.sections.map((section) => (
    <DetailPage
      key={section.key}
      title={textFor(section.titleKey, section.titleKey)}
      dataSource={detailFields}
      columns={section.fields.map((field) => {
        const bindingRenderer = field.rendererSource === 'binding'
          ? registration.detailFieldRenderers?.[field.rendererKey]
          : undefined;

        return {
          key: field.key,
          dataIndex: field.key,
          title: textFor(field.labelKey, field.labelKey),
          render: () => bindingRenderer
            ? bindingRenderer({ fieldKey: field.key, value: detailFields[field.key], detailFields })
            : formatStandardValue(field, detailFields[field.key], registration),
        };
      })}
    />
  ));
}
