const items = [
  { id: 1, name: '示例项目 1', status: '进行中' },
  { id: 2, name: '示例项目 2', status: '已完成' },
  { id: 3, name: '示例项目 3', status: '待开始' },
];

const statusClassMap: Record<string, string> = {
  已完成: 'sh-status-badge-success',
  进行中: 'sh-status-badge-warning',
  待开始: 'sh-status-badge-neutral',
};

export default function DemoList() {
  const completedCount = items.filter((item) => item.status === '已完成').length;

  return (
    <div className="sh-page">
      <section className="sh-page-header">
        <div className="sh-page-header-main">
          <span className="sh-page-eyebrow">演示模块</span>
          <h1 className="sh-page-title">示例列表</h1>
          <p className="sh-page-description">
            展示统一列表页在第二阶段的标准结构，包括摘要区、表格区和状态表达方式。
          </p>
        </div>
      </section>

      <div className="sh-section-grid">
        <div className="sh-summary-card">
          <span className="sh-summary-card-label">项目总数</span>
          <span className="sh-summary-card-value">{items.length}</span>
          <span className="sh-summary-card-meta">当前示例数据集</span>
        </div>
        <div className="sh-summary-card">
          <span className="sh-summary-card-label">已完成项目</span>
          <span className="sh-summary-card-value">{completedCount}</span>
          <span className="sh-summary-card-meta">剩余项目继续沿同一模式扩展</span>
        </div>
      </div>

      <div className="sh-helper-block">
        <h3 className="sh-helper-block-title">页面模式</h3>
        <p>列表类页面优先统一摘要、表格和状态样式，筛选和高级交互留在后续真实业务页面中逐步补齐。</p>
      </div>

      <section className="sh-page-panel">
        <div className="sh-page-panel-header">
          <h2 className="sh-page-panel-title">项目列表</h2>
          <span className="sh-page-panel-meta">{items.length} 个项目</span>
        </div>
        <div className="sh-page-panel-body">
          <div className="sh-table-wrap">
            <table className="sh-table">
              <thead>
                <tr>
                  <th>ID</th>
                  <th>名称</th>
                  <th>状态</th>
                </tr>
              </thead>
              <tbody>
                {items.map((item) => (
                  <tr key={item.id}>
                    <td>{item.id}</td>
                    <td>{item.name}</td>
                    <td>
                      <span className={`sh-status-badge ${statusClassMap[item.status]}`}>
                        {item.status}
                      </span>
                    </td>
                  </tr>
                ))}
              </tbody>
            </table>
          </div>
        </div>
      </section>
    </div>
  );
}
