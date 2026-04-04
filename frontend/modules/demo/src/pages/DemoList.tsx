export default function DemoList() {
  const items = [
    { id: 1, name: '示例项目 1', status: '进行中' },
    { id: 2, name: '示例项目 2', status: '已完成' },
    { id: 3, name: '示例项目 3', status: '待开始' }
  ];

  return (
    <div>
      <h2>Demo 列表</h2>
      <table style={{ width: '100%', borderCollapse: 'collapse', marginTop: '1rem' }}>
        <thead>
          <tr style={{ borderBottom: '2px solid #e8e8e8' }}>
            <th style={{ padding: '0.75rem', textAlign: 'left' }}>ID</th>
            <th style={{ padding: '0.75rem', textAlign: 'left' }}>名称</th>
            <th style={{ padding: '0.75rem', textAlign: 'left' }}>状态</th>
          </tr>
        </thead>
        <tbody>
          {items.map(item => (
            <tr key={item.id} style={{ borderBottom: '1px solid #e8e8e8' }}>
              <td style={{ padding: '0.75rem' }}>{item.id}</td>
              <td style={{ padding: '0.75rem' }}>{item.name}</td>
              <td style={{ padding: '0.75rem' }}>{item.status}</td>
            </tr>
          ))}
        </tbody>
      </table>
    </div>
  );
}
