import { useEffect, useMemo, useState } from 'react';
import ModuleTypeSidebar from '../components/ModuleTypeSidebar';
import SaveApplyToolbar from '../components/SaveApplyToolbar';
import SettingDefinitionTable from '../components/SettingDefinitionTable';
import MetadataModuleOverview from './MetadataModuleOverview';
import {
  applySettings,
  ApplySettingDefinitionsResult,
  getModules,
  getSettings,
  MetadataModuleManifest,
  saveSettings,
  SaveSettingDefinitionsResult,
  SettingDefinitionDto,
} from '../services/metadataConfigCenterService';

export default function SettingDefinitionManagement() {
  const [modules, setModules] = useState<MetadataModuleManifest[]>([]);
  const [selectedModuleId, setSelectedModuleId] = useState<string | null>(null);
  const [selectedType, setSelectedType] = useState('settings');
  const [savedDefinitions, setSavedDefinitions] = useState<SettingDefinitionDto[]>([]);
  const [draftDefinitions, setDraftDefinitions] = useState<SettingDefinitionDto[]>([]);
  const [status, setStatus] = useState<SaveSettingDefinitionsResult | null>(null);
  const [loading, setLoading] = useState(true);
  const [saving, setSaving] = useState(false);
  const [applying, setApplying] = useState(false);
  const [error, setError] = useState<string | null>(null);

  useEffect(() => {
    void loadModules();
  }, []);

  useEffect(() => {
    if (selectedModuleId && selectedType === 'settings') {
      void loadSettings(selectedModuleId);
    }
  }, [selectedModuleId, selectedType]);

  const hasUnsavedChanges = useMemo(() => {
    return JSON.stringify(savedDefinitions) !== JSON.stringify(draftDefinitions);
  }, [savedDefinitions, draftDefinitions]);

  async function loadModules() {
    try {
      setLoading(true);
      setError(null);
      const data = await getModules();
      setModules(data);

      if (data.length > 0 && !selectedModuleId) {
        setSelectedModuleId(data[0].moduleId);
      }
    } catch (loadError) {
      setError(loadError instanceof Error ? loadError.message : '模块加载失败');
    } finally {
      setLoading(false);
    }
  }

  async function loadSettings(moduleId: string) {
    try {
      setLoading(true);
      setError(null);
      const data = await getSettings(moduleId);
      setSavedDefinitions(data);
      setDraftDefinitions(data);
      setStatus(null);
    } catch (loadError) {
      setError(loadError instanceof Error ? loadError.message : 'SettingDefinition 加载失败');
    } finally {
      setLoading(false);
    }
  }

  async function handleSave() {
    if (!selectedModuleId) {
      return;
    }

    try {
      setSaving(true);
      setError(null);
      const result = await saveSettings(selectedModuleId, { definitions: draftDefinitions });
      setSavedDefinitions(result.definitions);
      setDraftDefinitions(result.definitions);
      setStatus(result);
    } catch (saveError) {
      setError(saveError instanceof Error ? saveError.message : '保存失败');
    } finally {
      setSaving(false);
    }
  }

  async function handleApply() {
    if (!selectedModuleId) {
      return;
    }

    try {
      setApplying(true);
      setError(null);
      const result = await applySettings(selectedModuleId);
      setStatus((previousStatus) => mergeApplyResult(previousStatus, result, draftDefinitions, hasUnsavedChanges));
    } catch (applyError) {
      setError(applyError instanceof Error ? applyError.message : '应用失败');
    } finally {
      setApplying(false);
    }
  }

  function handleSelect(moduleId: string, type: string) {
    setSelectedModuleId(moduleId);
    setSelectedType(type);
  }

  const effectiveStatus = status ?? {
    definitions: draftDefinitions,
    status: hasUnsavedChanges ? '存在未保存修改' : '当前没有待处理变更',
    hasUnsavedChanges,
    hasUnappliedChanges: false,
    gitSyncPending: false,
  };

  return (
    <div
      style={{
        minHeight: '100%',
        background: 'linear-gradient(180deg, #f7f9fc 0%, #eef4ff 100%)',
        padding: 24,
      }}
    >
      <div style={{ display: 'grid', gap: 20 }}>
        <MetadataModuleOverview moduleCount={modules.length} selectedModuleId={selectedModuleId} />

        <SaveApplyToolbar
          canSave={hasUnsavedChanges && draftDefinitions.length > 0}
          saving={saving}
          onSave={() => void handleSave()}
          canApply={!hasUnsavedChanges && effectiveStatus.hasUnappliedChanges}
          applying={applying}
          onApply={() => void handleApply()}
          statusMessage={effectiveStatus.status}
          hasUnsavedChanges={hasUnsavedChanges}
          hasUnappliedChanges={effectiveStatus.hasUnappliedChanges}
          gitSyncPending={effectiveStatus.gitSyncPending}
        />

        {error && (
          <div
            style={{
              border: '1px solid #fecaca',
              background: '#fff1f2',
              color: '#b91c1c',
              borderRadius: 12,
              padding: 14,
            }}
          >
            {error}
          </div>
        )}

        <div
          style={{
            display: 'grid',
            gridTemplateColumns: '280px minmax(0, 1fr)',
            gap: 20,
            alignItems: 'start',
          }}
        >
          <ModuleTypeSidebar
            modules={modules}
            selectedModuleId={selectedModuleId}
            selectedType={selectedType}
            onSelect={handleSelect}
          />

          <section style={{ display: 'grid', gap: 16 }}>
            <div
              style={{
                border: '1px solid #dbe1ea',
                borderRadius: 14,
                background: '#ffffff',
                padding: 16,
              }}
            >
              <h2 style={{ marginTop: 0, marginBottom: 8 }}>
                {selectedModuleId ? `${selectedModuleId} / SettingDefinition` : 'SettingDefinition'}
              </h2>
              <p style={{ margin: 0, color: '#475569', lineHeight: 1.6 }}>
                当前阶段只打通 SettingDefinition 的最小编辑闭环。你可以直接修改 DisplayName 或 DefaultValue，
                点击保存后会看到“已保存未应用”的状态提示，而应用和 Git 同步保持分离。
              </p>
            </div>

            {loading ? (
              <div
                style={{
                  border: '1px dashed #cbd5e1',
                  borderRadius: 14,
                  background: '#ffffff',
                  padding: 28,
                  color: '#475569',
                }}
              >
                元数据加载中...
              </div>
            ) : (
              <SettingDefinitionTable definitions={draftDefinitions} onChange={setDraftDefinitions} />
            )}
          </section>
        </div>
      </div>
    </div>
  );
}

function mergeApplyResult(
  previousStatus: SaveSettingDefinitionsResult | null,
  applyResult: ApplySettingDefinitionsResult,
  draftDefinitions: SettingDefinitionDto[],
  hasUnsavedChanges: boolean,
): SaveSettingDefinitionsResult {
  return {
    definitions: previousStatus?.definitions ?? draftDefinitions,
    status: applyResult.status,
    hasUnsavedChanges,
    hasUnappliedChanges: applyResult.hasUnappliedChanges,
    gitSyncPending: previousStatus?.gitSyncPending ?? false,
  };
}
