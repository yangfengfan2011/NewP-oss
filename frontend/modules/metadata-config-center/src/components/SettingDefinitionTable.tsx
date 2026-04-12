import { SettingDefinitionDto } from '../services/metadataConfigCenterService';

interface SettingDefinitionTableProps {
  definitions: SettingDefinitionDto[];
  onChange: (nextDefinitions: SettingDefinitionDto[]) => void;
}

export default function SettingDefinitionTable({ definitions, onChange }: SettingDefinitionTableProps) {
  function updateDefinition(index: number, field: keyof SettingDefinitionDto, value: string) {
    const nextDefinitions = definitions.map((definition, currentIndex) =>
      currentIndex === index ? { ...definition, [field]: value } : definition,
    );
    onChange(nextDefinitions);
  }

  if (definitions.length === 0) {
    return (
      <div
        style={{
          border: '1px dashed #cbd5e1',
          borderRadius: 14,
          background: '#f8fafc',
          padding: 28,
          color: '#475569',
        }}
      >
        当前模块尚未发现可编辑的 SettingDefinition。
      </div>
    );
  }

  return (
    <div
      style={{
        border: '1px solid #dbe1ea',
        borderRadius: 14,
        overflow: 'hidden',
        background: '#ffffff',
      }}
    >
      <table style={{ width: '100%', borderCollapse: 'collapse' }}>
        <thead style={{ background: '#f8fafc' }}>
          <tr>
            <th style={{ padding: 12, textAlign: 'left' }}>Key</th>
            <th style={{ padding: 12, textAlign: 'left' }}>DisplayName</th>
            <th style={{ padding: 12, textAlign: 'left' }}>DefaultValue</th>
            <th style={{ padding: 12, textAlign: 'left' }}>Group</th>
            <th style={{ padding: 12, textAlign: 'left' }}>SourceFile</th>
          </tr>
        </thead>
        <tbody>
          {definitions.map((definition, index) => (
            <tr key={`${definition.key}-${definition.sourceFile}`}>
              <td style={{ padding: 12, borderTop: '1px solid #e5e7eb', fontFamily: 'Consolas, monospace' }}>
                {definition.key}
              </td>
              <td style={{ padding: 12, borderTop: '1px solid #e5e7eb' }}>
                <input
                  type="text"
                  value={definition.displayName}
                  onChange={(event) => updateDefinition(index, 'displayName', event.target.value)}
                  style={{
                    width: '100%',
                    border: '1px solid #cbd5e1',
                    borderRadius: 8,
                    padding: '8px 10px',
                  }}
                />
              </td>
              <td style={{ padding: 12, borderTop: '1px solid #e5e7eb' }}>
                <input
                  type="text"
                  value={definition.defaultValue}
                  onChange={(event) => updateDefinition(index, 'defaultValue', event.target.value)}
                  style={{
                    width: '100%',
                    border: '1px solid #cbd5e1',
                    borderRadius: 8,
                    padding: '8px 10px',
                  }}
                />
              </td>
              <td style={{ padding: 12, borderTop: '1px solid #e5e7eb' }}>{definition.group}</td>
              <td style={{ padding: 12, borderTop: '1px solid #e5e7eb', fontFamily: 'Consolas, monospace' }}>
                {definition.sourceFile}
              </td>
            </tr>
          ))}
        </tbody>
      </table>
    </div>
  );
}
