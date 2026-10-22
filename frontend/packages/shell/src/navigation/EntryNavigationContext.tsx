import { useCallback, type ReactNode } from 'react';
import { useNavigate } from 'react-router-dom';
import { EntryNavigationContext, type EntryNavigation } from './entryNavigation';
import { useRuntimeMetadata } from '../metadata/runtimeMetadataContext';

/** Shell 拥有导航路径解析；模块仅声明目标入口身份和输入。 */
export function EntryNavigationProvider({ children }: { children: ReactNode }) {
  const metadata = useRuntimeMetadata();
  const navigate = useNavigate();
  const open = useCallback((target: EntryNavigation) => {
    const matches = metadata?.navDocuments.flatMap(document => document.navigation.items)
      .filter(item => item.key === target.entryKey && item.path && item.page) ?? [];
    if (matches.length !== 1) throw new Error(`导航入口不可唯一解析：${target.entryKey}`);
    navigate(matches[0].path!, { state: { pageParameters: target.parameters ?? {} } });
  }, [metadata, navigate]);
  return <EntryNavigationContext.Provider value={open}>{children}</EntryNavigationContext.Provider>;
}
