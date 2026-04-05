import { useState } from 'react';
import FileUpload from '../components/FileUpload';
import FileList from '../components/FileList';

export default function FilesHome() {
  const [refreshTrigger, setRefreshTrigger] = useState(0);

  const handleUploadSuccess = (fileId: string) => {
    console.log('文件上传成功:', fileId);
    setRefreshTrigger((prev) => prev + 1);
  };

  const handleUploadError = (error: Error) => {
    alert(`上传失败: ${error.message}`);
  };

  return (
    <div className="sh-page">
      <section className="sh-page-header">
        <div className="sh-page-header-main">
          <span className="sh-page-eyebrow">文件管理</span>
          <h1 className="sh-page-title">文件中心</h1>
          <p className="sh-page-description">
            上传、下载和管理文件，支持多种文件类型和基础操作，是第二阶段业务侧页面模式的代表页面之一。
          </p>
        </div>
      </section>

      <div className="sh-section-grid">
        <div className="sh-summary-card">
          <span className="sh-summary-card-label">上传方式</span>
          <span className="sh-summary-card-value">2 种</span>
          <span className="sh-summary-card-meta">拖拽或点击选择</span>
        </div>
        <div className="sh-summary-card">
          <span className="sh-summary-card-label">刷新触发</span>
          <span className="sh-summary-card-value">{refreshTrigger}</span>
          <span className="sh-summary-card-meta">上传成功后自动刷新</span>
        </div>
      </div>

      <div className="sh-helper-block">
        <h3 className="sh-helper-block-title">使用说明</h3>
        <p>支持拖拽上传或点击选择文件。上传成功后，文件列表会自动刷新并沿用统一的状态与操作结构。</p>
      </div>

      <FileUpload onUploadSuccess={handleUploadSuccess} onUploadError={handleUploadError} />

      <FileList refreshTrigger={refreshTrigger} />
    </div>
  );
}
