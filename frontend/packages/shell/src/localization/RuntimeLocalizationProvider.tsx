import { createContext, useContext, useEffect, useState, type ReactNode } from 'react';
import { LocalizationProvider, useLocalization } from '@icp-platform/core';
import { LanguageSelect } from '../components/LanguageSelect';
import { getFallbackRuntimeLocalizationManifest, loadRuntimeLocalizationManifest } from './runtimeLocalizationManifest';
import { PlatformRequestLocaleBridge } from './PlatformRequestLocaleBridge';

const RuntimeLocalizationContext = createContext({ ready: false, failed: false, retry: () => {} });

/** Provider 身份保持稳定，网络恢复只替换词典，不重建认证状态或表单。 */
export function RuntimeLocalizationProvider({ children }: { children: ReactNode }) {
  const [manifest, setManifest] = useState(getFallbackRuntimeLocalizationManifest);
  const [ready, setReady] = useState(false);
  const [failed, setFailed] = useState(false);
  const [attempt, setAttempt] = useState(0);
  useEffect(() => {
    let active = true;
    setFailed(false);
    void loadRuntimeLocalizationManifest().then(value => {
      if (!active) return;
      setManifest(value);
      setReady(true);
    }).catch(() => { if (active) setFailed(true); });
    return () => { active = false; };
  }, [attempt]);
  return (
    <LocalizationProvider {...manifest} storageKey="icp.language">
      <PlatformRequestLocaleBridge>
        <RuntimeLocalizationContext.Provider value={{ ready, failed, retry: () => setAttempt(value => value + 1) }}>
          {children}
        </RuntimeLocalizationContext.Provider>
      </PlatformRequestLocaleBridge>
    </LocalizationProvider>
  );
}

export function LocalizationRecovery() {
  const { ready, failed, retry } = useContext(RuntimeLocalizationContext);
  const { t } = useLocalization();
  if (ready) return null;
  return (
    <div className="sh-localization-recovery" role={failed ? 'alert' : 'status'}>
      <span>{failed ? t('PlatformAuthentication.DictionaryUnavailable') : t('PlatformAuthentication.LoadingDictionary')}</span>
      {failed && <button type="button" className="sh-btn" onClick={retry}>{t('PlatformAuthentication.Retry')}</button>}
    </div>
  );
}

export function AuthenticationFrame({ children }: { children: ReactNode }) {
  return <div className="sh-authentication-frame">
    <div className="sh-authentication-toolbar"><LanguageSelect /></div>
    <LocalizationRecovery />
    {children}
  </div>;
}

/** 正式业务只在有效词典到达后挂载；最小内置词典不表示 runtime 已就绪。 */
export function LocalizedBusinessBoundary({ children }: { children: ReactNode }) {
  const { ready } = useContext(RuntimeLocalizationContext);
  return ready ? children : <AuthenticationFrame>{null}</AuthenticationFrame>;
}
