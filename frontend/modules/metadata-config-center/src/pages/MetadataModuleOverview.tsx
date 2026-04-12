interface MetadataModuleOverviewProps {
  moduleCount: number;
  selectedModuleId: string | null;
}

export default function MetadataModuleOverview({
  moduleCount,
  selectedModuleId,
}: MetadataModuleOverviewProps) {
  return (
    <section
      style={{
        border: '1px solid #dbe1ea',
        borderRadius: 18,
        background: 'linear-gradient(135deg, #fff8ed 0%, #ffffff 52%, #edf4ff 100%)',
        padding: 24,
      }}
    >
      <header style={{ marginBottom: 20 }}>
        <p style={{ margin: 0, color: '#6b7280', fontSize: 14 }}>元数据配置中心</p>
        <h1 style={{ margin: '8px 0 0', fontSize: 28 }}>SettingDefinition 最小工作台</h1>
        <p style={{ marginTop: 8, maxWidth: 720, color: '#4b5563', lineHeight: 1.6 }}>
          当前阶段先打通独立元数据配置中心、manifest 装配、SettingDefinition 读取/保存和“已保存未应用”提示。
          ConfigCenter 继续只负责 ConfigItem 值层，不承担定义层编辑职责。
        </p>
      </header>

      <div
        style={{
          display: 'grid',
          gridTemplateColumns: 'repeat(auto-fit, minmax(180px, 1fr))',
          gap: 14,
        }}
      >
        <div style={{ borderRadius: 14, background: '#ffffff', padding: 16, border: '1px solid #e5e7eb' }}>
          <div style={{ fontSize: 12, color: '#6b7280', textTransform: 'uppercase' }}>已发现模块</div>
          <div style={{ marginTop: 8, fontSize: 28, fontWeight: 700 }}>{moduleCount}</div>
        </div>
        <div style={{ borderRadius: 14, background: '#ffffff', padding: 16, border: '1px solid #e5e7eb' }}>
          <div style={{ fontSize: 12, color: '#6b7280', textTransform: 'uppercase' }}>当前模块</div>
          <div style={{ marginTop: 8, fontSize: 20, fontWeight: 700 }}>{selectedModuleId ?? '未选择'}</div>
        </div>
        <div style={{ borderRadius: 14, background: '#ffffff', padding: 16, border: '1px solid #e5e7eb' }}>
          <div style={{ fontSize: 12, color: '#6b7280', textTransform: 'uppercase' }}>工作流</div>
          <div style={{ marginTop: 8, fontSize: 16, fontWeight: 700 }}>保存 / 应用 / Git 同步分离</div>
        </div>
      </div>
    </section>
  );
}
