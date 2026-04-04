export interface FileInfo {
  fileId: string;
  fileName: string;
  fileSize: number;
  contentType: string;
  uploadedBy?: string;
  uploadedAt: string;
  entityType?: string;
  entityId?: string;
  isDeleted: boolean;
}

export interface UploadResponse {
  fileId: string;
  fileName: string;
  fileSize: number;
  contentType: string;
  uploadedAt: string;
}

export class FileService {
  private baseUrl = '/api/files';

  async upload(
    file: File,
    entityType?: string,
    entityId?: string
  ): Promise<UploadResponse> {
    const formData = new FormData();
    formData.append('file', file);
    if (entityType) formData.append('entityType', entityType);
    if (entityId) formData.append('entityId', entityId);

    const response = await fetch(`${this.baseUrl}/upload`, {
      method: 'POST',
      body: formData
    });

    if (!response.ok) {
      throw new Error('文件上传失败');
    }

    return response.json();
  }

  async download(fileId: string): Promise<void> {
    const response = await fetch(`${this.baseUrl}/${fileId}/download`);

    if (!response.ok) {
      throw new Error('文件下载失败');
    }

    const blob = await response.blob();
    const contentDisposition = response.headers.get('Content-Disposition');
    const fileName = contentDisposition
      ? contentDisposition.split('filename=')[1]?.replace(/"/g, '')
      : 'download';

    const url = window.URL.createObjectURL(blob);
    const a = document.createElement('a');
    a.href = url;
    a.download = fileName;
    document.body.appendChild(a);
    a.click();
    window.URL.revokeObjectURL(url);
    document.body.removeChild(a);
  }

  async getFileInfo(fileId: string): Promise<FileInfo> {
    const response = await fetch(`${this.baseUrl}/${fileId}`);

    if (!response.ok) {
      throw new Error('获取文件信息失败');
    }

    return response.json();
  }

  async query(
    entityType?: string,
    entityId?: string,
    includeDeleted = false
  ): Promise<FileInfo[]> {
    const params = new URLSearchParams();
    if (entityType) params.append('entityType', entityType);
    if (entityId) params.append('entityId', entityId);
    if (includeDeleted) params.append('includeDeleted', 'true');

    const response = await fetch(`${this.baseUrl}?${params}`);

    if (!response.ok) {
      throw new Error('查询文件列表失败');
    }

    return response.json();
  }

  async delete(fileId: string): Promise<void> {
    const response = await fetch(`${this.baseUrl}/${fileId}`, {
      method: 'DELETE'
    });

    if (!response.ok) {
      throw new Error('删除文件失败');
    }
  }

  async permanentDelete(fileId: string): Promise<void> {
    const response = await fetch(`${this.baseUrl}/${fileId}/permanent`, {
      method: 'DELETE'
    });

    if (!response.ok) {
      throw new Error('永久删除文件失败');
    }
  }

  formatFileSize(bytes: number): string {
    if (bytes === 0) return '0 B';
    const k = 1024;
    const sizes = ['B', 'KB', 'MB', 'GB'];
    const i = Math.floor(Math.log(bytes) / Math.log(k));
    return `${(bytes / Math.pow(k, i)).toFixed(2)} ${sizes[i]}`;
  }
}

export const fileService = new FileService();
