import { MetadataModuleManifest } from '../services/metadataConfigCenterService';

interface ModuleTypeSidebarProps {
  modules: MetadataModuleManifest[];
  selectedModuleId: string | null;
  selectedType: string;
  onSelect: (moduleId: string, type: string) => void;
}

export default function ModuleTypeSidebar({
  modules,
  selectedModuleId,
  selectedType,
  onSelect,
}: ModuleTypeSidebarProps) {
  return (
    <aside
      style={{
        border: '1px solid #dbe1ea',
        borderRadius: 14,
        background: '#ffffff',
        padding: 16,
      }}
    >
      <h2 style={{ marginTop: 0, fontSize: 18 }}>模块与类型</h2>
      <div style={{ display: 'grid', gap: 12 }}>
        {modules.map((module) => {
          const types = Object.entries(module.types).filter(([, descriptor]) => descriptor.enabled);

          return (
            <div
              key={module.moduleId}
              style={{
                border: '1px solid #e5e7eb',
                borderRadius: 12,
                padding: 12,
                background: selectedModuleId === module.moduleId ? '#f5f8ff' : '#fbfcfe',
              }}
            >
              <div style={{ marginBottom: 8, fontWeight: 700 }}>{module.moduleId}</div>
              <div style={{ display: 'grid', gap: 8 }}>
                {types.map(([type, descriptor]) => {
                  const active = selectedModuleId === module.moduleId && selectedType === type;

                  return (
                    <button
                      key={`${module.moduleId}-${type}`}
                      type="button"
                      onClick={() => onSelect(module.moduleId, type)}
                      style={{
                        border: active ? '1px solid #2563eb' : '1px solid #d1d5db',
                        background: active ? '#2563eb' : '#ffffff',
                        color: active ? '#ffffff' : '#111827',
                        borderRadius: 10,
                        padding: '10px 12px',
                        textAlign: 'left',
                        cursor: 'pointer',
                      }}
                    >
                      <div style={{ fontWeight: 600 }}>SettingDefinition</div>
                      <div style={{ marginTop: 4, fontSize: 12, opacity: 0.82 }}>
                        {type} / {descriptor.sourcePath}
                      </div>
                    </button>
                  );
                })}
              </div>
            </div>
          );
        })}
      </div>
    </aside>
  );
}
