import type { ReactNode } from 'react';
import type { StandardCrudResourceRegistration } from '@icp-platform/sdk';
import { DetailPage } from '@icp-platform/ui/crud-detail';
import type { StandardCrudResolvedDetailFieldSchema } from '../metadata/standardCrudSchemaResolver';
import type { StandardCrudDetailPresentation } from './standardCrudDetailPresentation';

type ReadyDetailPresentation = Extract<StandardCrudDetailPresentation, { kind: 'ready' }>;

interface StandardCrudResolvedDetailSectionsProps {
  presentation: ReadyDetailPresentation;
  registration: Pick<StandardCrudResourceRegistration, 'detailFieldRenderers' | 'enumOptions'>;
  textFor: (key: string, fallback: string) => string;
}

function formatStandardValue(
  field: StandardCrudResolvedDetailFieldSchema,
  value: unknown,
  registration: Pick<StandardCrudResourceRegistration, 'enumOptions'>,
): ReactNode {
  if (value === null) {
    return '—';
  }
  if (field.rendererKey === 'standard.enum') {
    const option = field.enumKey
      ? registration.enumOptions?.[field.enumKey]?.find((item) => item.value === value)
      : undefined;
    if (option) {
      return option.label;
    }
  }
  if (field.rendererKey === 'standard.boolean') {
    return value === true ? '是' : value === false ? '否' : String(value);
  }

  return String(value);
}

/** Renders the shared runtime availability plan used by both the Drawer and the full detail page. */
export default function StandardCrudResolvedDetailSections({
  presentation,
  registration,
  textFor,
}: StandardCrudResolvedDetailSectionsProps) {
  return presentation.sections.map((section) => {
    if (section.state === 'unavailable') {
      return (
        <section
          key={section.key}
          className="np-standard-crud-detail-section-unavailable"
          data-diagnostic-code="STANDARD_CRUD_DETAIL_SECTION_UNAVAILABLE"
          data-section-key={section.key}
        >
          <h3>{textFor(section.titleKey, section.titleKey)}</h3>
          <div role="status">数据不可用</div>
        </section>
      );
    }

    return (
      <DetailPage
        key={section.key}
        title={textFor(section.titleKey, section.titleKey)}
        dataSource={presentation.sourceFields}
        columns={section.fields.map((field) => ({
          key: field.key,
          dataIndex: field.key,
          title: textFor(field.labelKey, field.labelKey),
          render: () => {
            if (field.state === 'unavailable') {
              return (
                <span
                  role="status"
                  data-diagnostic-code="STANDARD_CRUD_DETAIL_FIELD_UNAVAILABLE"
                  data-field-key={field.key}
                >
                  数据不可用
                </span>
              );
            }
            if (field.value === null) {
              return <span data-field-key={field.key}>—</span>;
            }

            const bindingRenderer = field.rendererSource === 'binding'
              ? registration.detailFieldRenderers?.[field.rendererKey]
              : undefined;
            return bindingRenderer
              ? bindingRenderer({
                  fieldKey: field.key,
                  value: field.value,
                  detailFields: presentation.sourceFields,
                })
              : formatStandardValue(field, field.value, registration);
          },
        }))}
      />
    );
  });
}
