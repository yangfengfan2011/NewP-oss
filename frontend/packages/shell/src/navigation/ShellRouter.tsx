import { createContext, useContext, useLayoutEffect, useState, type ReactNode } from 'react';
import { createBrowserRouter, RouterProvider } from 'react-router-dom';

const ContentContext = createContext<ReactNode>(null);
function RouterContent() { return <>{useContext(ContentContext)}</>; }

/** 稳定的路由实例提供全导航 blocker；上下文传递最新 Shell 内容而不重建历史。 */
export function ShellRouter({ children }: { children: ReactNode }) {
  const [router, setRouter] = useState<ReturnType<typeof createBrowserRouter> | null>(null);
  useLayoutEffect(() => {
    const candidate = createBrowserRouter([{ path: '*', element: <RouterContent /> }]);
    setRouter(candidate);
    return () => candidate.dispose();
  }, []);
  return <ContentContext.Provider value={children}>{router && <RouterProvider router={router} />}</ContentContext.Provider>;
}
