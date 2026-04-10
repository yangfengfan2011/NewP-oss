import { useState } from 'react';
import { PageHeader, SummaryCard } from '@icp-platform/ui';
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
      <PageHeader
        eyebrow="文件管理"
        title="文件中心"
        description="上传、下载和管理文件，支持多种文件类型和基础操作，是第二阶段业务侧页面模式的代表页面之一。"
      />

      <div className="sh-section-grid">
        <SummaryCard label="上传方式" value="2 种" meta="拖拽或点击选择" />
        <SummaryCard label="刷新触发" value={refreshTrigger} meta="上传成功后自动刷新" />
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
