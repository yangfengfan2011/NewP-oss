import type { ReactNode } from 'react';
import { useLocalization } from '@icp-platform/core';
import type { StandardCrudResourceRegistration } from '@icp-platform/sdk';
import { DetailPage } from '@icp-platform/ui/crud-detail';
import type { StandardCrudResolvedDetailFieldSchema } from '../metadata/standardCrudSchemaResolver';
import type { StandardCrudDetailPresentation } from './standardCrudDetailPresentation';
import StandardCrudDetailContributions from './StandardCrudDetailContributions';

type ReadyDetailPresentation = Extract<StandardCrudDetailPresentation, { kind: 'ready' }>;

interface StandardCrudResolvedDetailSectionsProps {
  presentation: ReadyDetailPresentation;
  registration: Pick<StandardCrudResourceRegistration, 'detailFieldRenderers' | 'enumOptions' | 'detailViewKey'>;
  textFor: (key: string, fallback: string) => string;
}

function formatStandardValue(
  field: StandardCrudResolvedDetailFieldSchema,
  value: unknown,
  registration: Pick<StandardCrudResourceRegistration, 'enumOptions'>,
  localization: ReturnType<typeof useLocalization>,
): ReactNode {
  if (value === null) {
    return '—';
  }
  if (field.rendererKey === 'standard.enum') {
    const option = field.enumKey
      ? registration.enumOptions?.[field.enumKey]?.find((item) => item.value === value)
      : undefined;
    if (option) {
      return localization.getEnumerationLabel(field.enumKey!, String(value), option.label);
    }
  }
  if (field.rendererKey === 'standard.boolean') {
    return value === true ? localization.t('Platform.Yes', '是') : value === false ? localization.t('Platform.No', '否') : String(value);
  }

  return String(value);
}

/** Renders the shared runtime availability plan used by both the Drawer and the full detail page. */
export default function StandardCrudResolvedDetailSections({
  presentation,
  registration,
  textFor,
}: StandardCrudResolvedDetailSectionsProps) {
  const localization = useLocalization();
  return <div className="np-crud-detail-sections">{presentation.sections.map((section) => {
    if (section.state === 'unavailable') {
      return (
        <section
          key={section.key}
          className="np-standard-crud-detail-section-unavailable"
          data-diagnostic-code="STANDARD_CRUD_DETAIL_SECTION_UNAVAILABLE"
          data-section-key={section.key}
        >
          <h3>{textFor(section.titleKey, section.titleKey)}</h3>
          <div role="status">{localization.t('Platform.DataUnavailable', '数据不可用')}</div>
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
                  {localization.t('Platform.DataUnavailable', '数据不可用')}
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
              : formatStandardValue(field, field.value, registration, localization);
          },
        }))}
      />
    );
  })}<StandardCrudDetailContributions key={`${registration.detailViewKey}:${String(presentation.sourceFields.EID)}`}
    viewKey={registration.detailViewKey} recordId={String(presentation.sourceFields.EID)} /></div>;
}
