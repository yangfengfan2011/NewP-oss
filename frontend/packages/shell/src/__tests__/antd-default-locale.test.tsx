import { act, createRef, type ComponentRef, type ReactNode } from 'react';
import { createRoot, type Root } from 'react-dom/client';
import { afterEach, beforeEach, describe, expect, it } from 'vitest';
import { ConfigProvider as CommonJsConfigProvider, Form as CommonJsForm, Input as CommonJsInput } from 'antd';
import ConfigProvider, { type ConfigProviderProps } from 'antd/es/config-provider';
import Form, { type FormProps } from 'antd/es/form';
import Input from 'antd/es/input';
import { changeConfirmLocale, getConfirmLocale } from 'antd/es/modal/locale';
import { changeConfirmLocale as changeCommonJsConfirmLocale, getConfirmLocale as getCommonJsConfirmLocale } from 'antd/lib/modal/locale';

let root: Root;
let container: HTMLDivElement;

beforeEach(() => {
  container = document.createElement('div');
  document.body.append(container);
  root = createRoot(container);
});

afterEach(async () => {
  await act(async () => root.unmount());
  container.remove();
});

async function render(node: ReactNode) {
  await act(async () => root.render(node));
}

const locale = {
  locale: 'custom',
  Form: {
    defaultValidateMessages: {
      required: 'Locale requires ${label}',
      types: { email: 'Locale rejects ${label}' },
    },
  },
};

async function expectFormMessages(
  providerProps: ConfigProviderProps,
  expected: [string, string],
  validateMessages?: FormProps['validateMessages'],
  commonJs = false,
) {
  const Provider = commonJs ? CommonJsConfigProvider : ConfigProvider;
  const FormControl = commonJs ? CommonJsForm : Form;
  const InputControl = commonJs ? CommonJsInput : Input;
  const form = createRef<ComponentRef<typeof Form>>();
  await render(<Provider {...providerProps}>
    <FormControl ref={form} initialValues={{ email: 'invalid' }} validateMessages={validateMessages}>
      <FormControl.Item name="name" label="Name" rules={[{ required: true }]}><InputControl /></FormControl.Item>
      <FormControl.Item name="email" label="Email" rules={[{ type: 'email' }]}><InputControl /></FormControl.Item>
    </FormControl>
  </Provider>);
  await act(async () => {
    await expect(form.current!.validateFields()).rejects.toMatchObject({
      errorFields: [
        { name: ['name'], errors: [expected[0]] },
        { name: ['email'], errors: [expected[1]] },
      ],
    });
  });
}

describe('Ant Design default Form messages', () => {
  it('uses the ConfigProvider English required and nested type defaults in a real Form', async () => {
    await expectFormMessages({}, ['Please enter Name', 'Email is not a valid email']);
  });

  it('uses locale Form overrides for required and nested type messages', async () => {
    await expectFormMessages({ locale }, ['Locale requires Name', 'Locale rejects Email']);
  });

  it('prioritizes provider form messages and preserves unoverridden locale messages', async () => {
    await expectFormMessages({ locale, form: { validateMessages: { required: 'Provider requires ${label}' } } },
      ['Provider requires Name', 'Locale rejects Email']);
  });

  it('keeps explicit Form messages ahead of provider messages', async () => {
    await expectFormMessages({ locale, form: { validateMessages: { required: 'Provider requires ${label}' } } },
      ['Form requires Name', 'Locale rejects Email'], { required: 'Form requires ${label}' });
  });

  it('keeps CommonJS provider overrides and unoverridden English nested defaults', async () => {
    await expectFormMessages({ form: { validateMessages: { required: 'Provider requires ${label}' } } },
      ['Provider requires Name', 'Email is not a valid email'], undefined, true);
  });
});

const defaultModalLocale = { okText: 'OK', cancelText: 'Cancel', justOkText: 'OK' };

describe('Ant Design default Modal locale', () => {
  it.each([
    { entry: 'ESM', change: changeConfirmLocale, get: getConfirmLocale },
    { entry: 'CommonJS', change: changeCommonJsConfirmLocale, get: getCommonJsConfirmLocale },
  ])('restores $entry defaults after stacked partial locale registrations are removed', ({ change, get }) => {
    const cleanup: Array<() => void> = [];
    try {
      expect(get()).toEqual(defaultModalLocale);
      // 上游实现允许局部 locale；类型声明仍要求完整对象。
      const removeFirst = change({ okText: '确定' } as Parameters<typeof change>[0])!;
      cleanup.push(removeFirst);
      expect(get()).toEqual({ ...defaultModalLocale, okText: '确定' });
      const removeSecond = change({ okText: 'Confirm', cancelText: '返回' } as Parameters<typeof change>[0])!;
      cleanup.push(removeSecond);
      expect(get()).toEqual({ ...defaultModalLocale, okText: 'Confirm', cancelText: '返回' });
      removeSecond();
      expect(get()).toEqual({ ...defaultModalLocale, okText: '确定' });
      removeFirst();
      expect(get()).toEqual(defaultModalLocale);
    } finally {
      cleanup.reverse().forEach(remove => remove());
    }
  });

  it('registers and restores Modal defaults through the real ConfigProvider lifecycle', async () => {
    await render(<ConfigProvider locale={{ locale: 'custom', Modal: { ...defaultModalLocale, okText: '确定' } }}>
      <span>consumer</span>
    </ConfigProvider>);
    expect(getConfirmLocale()).toEqual({ ...defaultModalLocale, okText: '确定' });
    await render(null);
    expect(getConfirmLocale()).toEqual(defaultModalLocale);
  });
});
