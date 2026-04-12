const staticModules = [
  {
    moduleId: 'ConfigCenter',
    manifest: 'modules/ConfigCenter/metadata/metadata.manifest.json',
    enabledTypes: 'settings',
    status: '样本已就绪'
  }
];

export default function MetadataModuleOverview() {
  return (
    <div style={{ padding: 24 }}>
      <header style={{ marginBottom: 24 }}>
        <p style={{ margin: 0, color: '#6b7280', fontSize: 14 }}>元数据配置中心</p>
        <h1 style={{ margin: '8px 0 0', fontSize: 28 }}>模块元数据概览</h1>
        <p style={{ marginTop: 8, maxWidth: 720, color: '#4b5563', lineHeight: 1.6 }}>
          当前阶段先固化独立模块骨架、manifest 契约入口和 SettingDefinition 的最小样本。
          ConfigCenter 继续只负责 ConfigItem 值层，不承担定义层编辑职责。
        </p>
      </header>

      <section style={{ marginBottom: 24 }}>
        <div
          style={{
            border: '1px solid #e5e7eb',
            borderRadius: 12,
            overflow: 'hidden',
            background: '#ffffff'
          }}
        >
          <table style={{ width: '100%', borderCollapse: 'collapse' }}>
            <thead style={{ background: '#f9fafb' }}>
              <tr>
                <th style={{ padding: 12, textAlign: 'left' }}>模块</th>
                <th style={{ padding: 12, textAlign: 'left' }}>Manifest</th>
                <th style={{ padding: 12, textAlign: 'left' }}>已启用类型</th>
                <th style={{ padding: 12, textAlign: 'left' }}>状态</th>
              </tr>
            </thead>
            <tbody>
              {staticModules.map((module) => (
                <tr key={module.moduleId}>
                  <td style={{ padding: 12, borderTop: '1px solid #e5e7eb' }}>{module.moduleId}</td>
                  <td style={{ padding: 12, borderTop: '1px solid #e5e7eb', fontFamily: 'Consolas, monospace' }}>
                    {module.manifest}
                  </td>
                  <td style={{ padding: 12, borderTop: '1px solid #e5e7eb' }}>{module.enabledTypes}</td>
                  <td style={{ padding: 12, borderTop: '1px solid #e5e7eb' }}>{module.status}</td>
                </tr>
              ))}
            </tbody>
          </table>
        </div>
      </section>

      <section
        style={{
          border: '1px dashed #d1d5db',
          borderRadius: 12,
          padding: 20,
          background: '#f9fafb',
          color: '#4b5563'
        }}
      >
        <h2 style={{ marginTop: 0, fontSize: 18 }}>空状态提示</h2>
        <p style={{ marginBottom: 0, lineHeight: 1.6 }}>
          SettingDefinition 编辑页将在下一阶段接入。当前页面只展示模块样本和接入状态，
          用于确认独立元数据配置中心已经具备独立入口。
        </p>
      </section>
    </div>
  );
}
