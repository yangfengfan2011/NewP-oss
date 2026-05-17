import type { ReactNode } from 'react';
import type { ProFormProps } from '@ant-design/pro-components';
import { ProForm } from '@ant-design/pro-components';

export interface FormPageProps<TValues extends Record<string, unknown> = Record<string, unknown>> {
  initialValues?: Partial<TValues>;
  disabled?: boolean;
  component?: ProFormProps<TValues>['component'];
  submitter?: ProFormProps<TValues>['submitter'];
  onFinish: (values: TValues) => Promise<boolean | void> | boolean | void;
  children: ReactNode;
}

export function FormPage<TValues extends Record<string, unknown> = Record<string, unknown>>({
  initialValues,
  disabled,
  component,
  submitter,
  onFinish,
  children,
}: FormPageProps<TValues>) {
  return (
    <ProForm<TValues>
      layout="vertical"
      disabled={disabled}
      component={component}
      initialValues={initialValues}
      submitter={submitter}
      onFinish={async (values) => {
        const result = await onFinish(values as TValues);
        return result ?? true;
      }}
    >
      {children}
    </ProForm>
  );
}
