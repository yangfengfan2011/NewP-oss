import { useMemo } from 'react';
import { useLocalization } from '@icp-platform/core';
import type { StandardCrudOption } from '@icp-platform/sdk';

/** 枚举键仍由资源与 Model dataSource 引用拥有，宿主仅投影当前语言标签。 */
export function useLocalizedStandardCrudRegistration<T extends { enumOptions?: Record<string, StandardCrudOption[]> }>(registration: T): T {
  const { getEnumerationLabel, t } = useLocalization();
  return useMemo(() => ({ ...registration, enumOptions: Object.fromEntries(
    Object.entries(registration.enumOptions ?? {}).map(([key, options]) => [key,
      options.map(option => ({ ...option, label: option.labelKey ? t(option.labelKey, option.label) : getEnumerationLabel(key, option.value, option.label) })),
    ]),
  ) }), [registration, getEnumerationLabel, t]);
}
