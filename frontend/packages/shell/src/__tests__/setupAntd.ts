import { vi } from 'vitest';

vi.mock('@icp-platform/core', async (importOriginal) => {
  const actual = await importOriginal<typeof import('@icp-platform/core')>();

  return {
    ...actual,
    platformFetch: (input: RequestInfo | URL, init?: RequestInit) => fetch(input, init),
  };
});

(globalThis as typeof globalThis & { IS_REACT_ACT_ENVIRONMENT?: boolean }).IS_REACT_ACT_ENVIRONMENT = true;

if (typeof window !== 'undefined') {
  Object.defineProperty(window, 'matchMedia', {
    writable: true,
    value: (query: string) => ({
      matches: false,
      media: query,
      onchange: null,
      addListener: () => undefined,
      removeListener: () => undefined,
      addEventListener: () => undefined,
      removeEventListener: () => undefined,
      dispatchEvent: () => false,
    }),
  });

  const getComputedStyle = window.getComputedStyle.bind(window);
  Object.defineProperty(window, 'getComputedStyle', {
    writable: true,
    value: (element: Element, pseudoElement?: string | null) => {
      if (pseudoElement) {
        return {
          getPropertyValue: () => '',
          width: '0px',
          height: '0px',
        };
      }

      return getComputedStyle(element);
    },
  });
}
