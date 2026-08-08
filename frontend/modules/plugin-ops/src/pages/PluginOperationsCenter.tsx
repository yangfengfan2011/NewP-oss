import { ChangeEvent, useEffect, useState } from 'react';
import {
  disableModule,
  enableModule,
  getModules,
  installUpload,
  rollbackModule,
  uninstallModule,
  verifyUpload,
} from '../api/pluginOperationsClient';
import type {
  PluginModuleStatus,
  PluginOperationResult,
  PluginUploadVerifyResult,
} from '../types/pluginOperations';

type NoticeTone = 'info' | 'success' | 'warning' | 'error';

interface NoticeState {
  tone: NoticeTone;
  message: string;
}

function getStatusLabel(status: string): string {
  switch (status) {
    case 'Enabled':
      return '已启用';
    case 'Installed':
      return '已安装';
    case 'Disabled':
      return '已禁用';
    case 'PendingRestart':
      return '等待重启';
    case 'Uninstalled':
      return '已卸载';
    case 'Failed':
      return '失败';
    case 'NotInstalled':
      return '未安装';
    default:
      return status;
  }
}

function getVersionsLabel(versions: string[]): string {
  return versions.length > 0 ? versions.join(', ') : '无';
}

function getNoticeTone(result: PluginOperationResult): NoticeTone {
  if (result.status === 'Failed') {
    return 'error';
  }

  if (result.status === 'PendingRestart') {
    return 'warning';
  }

  const message = result.message.trim().toLowerCase();
  if (message.includes('warning') || message.includes('warn') || message.includes('警告')) {
    return 'warning';
  }

  return 'success';
}

function getInitialSelectedVersions(modules: PluginModuleStatus[]): Record<string, string> {
  const selections: Record<string, string> = {};
  modules.forEach((module) => {
    selections[module.moduleId] =
      module.desiredVersion ??
      module.activeVersion ??
      module.currentEnabledVersion ??
      module.installedVersions[module.installedVersions.length - 1] ??
      '';
  });
  return selections;
}

export default function PluginOperationsCenter() {
  const [modules, setModules] = useState<PluginModuleStatus[]>([]);
  const [loading, setLoading] = useState(true);
  const [notice, setNotice] = useState<NoticeState | null>(null);
  const [selectedFile, setSelectedFile] = useState<File | null>(null);
  const [verifyResult, setVerifyResult] = useState<PluginUploadVerifyResult | null>(null);
  const [selectedVersions, setSelectedVersions] = useState<Record<string, string>>({});
  const [busyAction, setBusyAction] = useState<string | null>(null);

  useEffect(() => {
    void loadModules();
  }, []);

  async function loadModules(): Promise<void> {
    // 模块状态以后端查询结果为准；每次运维动作后都重新拉取，避免前端本地状态和
    // 运行时真实状态分叉，尤其是启用、回滚和卸载这种会改变可操作边界的流程。
    setLoading(true);
    try {
      const data = await getModules();
      setModules(data);
      setSelectedVersions(getInitialSelectedVersions(data));
    } catch (error) {
      const message = error instanceof Error ? error.message : '加载插件状态失败。';
      setNotice({ tone: 'error', message });
    } finally {
      setLoading(false);
    }
  }

  function handleFileChange(event: ChangeEvent<HTMLInputElement>): void {
    const file = event.target.files?.[0] ?? null;
    setSelectedFile(file);
    setVerifyResult(null);
  }

  async function handleVerify(): Promise<void> {
    if (!selectedFile) {
      setNotice({ tone: 'error', message: '请先选择一个统一插件包。' });
      return;
    }

    // 先校验再安装，避免把不可恢复的包直接送入安装链路；
    // 校验结果也会成为后续“是否允许安装”的用户边界提示。
    setBusyAction('verify');
    try {
      const result = await verifyUpload(selectedFile);
      setVerifyResult(result);
      setNotice({
        tone: result.isValid ? 'success' : 'error',
        message: result.isValid ? '插件包校验通过。' : '插件包校验未通过，请先处理错误后再安装。',
      });
    } catch (error) {
      const message = error instanceof Error ? error.message : '插件包校验失败。';
      setNotice({ tone: 'error', message });
    } finally {
      setBusyAction(null);
    }
  }

  async function runLifecycleAction(
    actionKey: string,
    operation: () => Promise<PluginOperationResult>,
  ): Promise<void> {
    // 同一时间只允许一个生命周期动作生效，防止并发点击把启用、禁用、回滚
    // 交错执行，导致页面展示的状态和后端实际状态不一致。
    setBusyAction(actionKey);
    try {
      const result = await operation();
      setNotice({
        tone: getNoticeTone(result),
        message: result.message || `${result.moduleId} ${getStatusLabel(result.status)}。`,
      });
      await loadModules();
    } catch (error) {
      const message = error instanceof Error ? error.message : '插件运维操作失败。';
      setNotice({ tone: 'error', message });
    } finally {
      setBusyAction(null);
    }
  }

  const installedCount = modules.filter((module) => module.installedVersions.length > 0).length;
  const enabledCount = modules.filter(
    (module) => Boolean(module.activeVersion ?? module.currentEnabledVersion),
  ).length;
  const failedCount = modules.filter((module) => module.status === 'Failed').length;

  if (loading) {
    return <div className="sh-page-loading">插件运维中心加载中...</div>;
  }

  return (
    <div className="sh-page">
      <section className="sh-page-header">
        <div className="sh-page-header-main">
          <span className="sh-page-eyebrow">运维中心</span>
          <h1 className="sh-page-title">插件运维中心</h1>
          <p className="sh-page-description">
            统一查看 installed、desired、active 与 LKG；启用、禁用和回滚会在下一次 Host 冷启动生效。
          </p>
        </div>
      </section>

      <div className="sh-section-grid-3">
        <div className="sh-summary-card">
          <span className="sh-summary-card-label">已安装模块</span>
          <span className="sh-summary-card-value">{installedCount}</span>
          <span className="sh-summary-card-meta">存在安装版本的模块数</span>
        </div>

        <div className="sh-summary-card">
          <span className="sh-summary-card-label">当前启用</span>
          <span className="sh-summary-card-value">{enabledCount}</span>
          <span className="sh-summary-card-meta">状态为已启用的模块数</span>
        </div>

        <div className="sh-summary-card">
          <span className="sh-summary-card-label">失败状态</span>
          <span className="sh-summary-card-value">{failedCount}</span>
          <span className="sh-summary-card-meta">需要管理员处理的模块数</span>
        </div>
      </div>

      <div className="sh-helper-block">
        <h3 className="sh-helper-block-title">操作边界</h3>
        <p>安装不会自动启用；desired 与 active 不一致时必须重启。卸载只在禁用已成功提交后开放，数据与迁移账本会保留。</p>
      </div>

      {notice ? (
        <section className="sh-page-panel">
          <div className="sh-page-panel-header">
            <h2 className="sh-page-panel-title">最近反馈</h2>
          </div>
          <div className="sh-page-panel-body">
            <p data-tone={notice.tone}>{notice.message}</p>
          </div>
        </section>
      ) : null}

      <section className="sh-page-panel">
        <div className="sh-page-panel-header">
          <h2 className="sh-page-panel-title">上传与校验</h2>
          <div className="sh-page-panel-meta">先校验，后安装</div>
        </div>
        <div className="sh-page-panel-body">
          <div className="sh-form-grid">
            <label className="sh-form-field">
              <span>统一插件包</span>
              <input type="file" accept=".zip" onChange={handleFileChange} />
            </label>
            <button
              type="button"
              className="sh-btn sh-btn-primary"
              onClick={() => void handleVerify()}
              disabled={busyAction === 'verify'}
            >
              {busyAction === 'verify' ? '校验中...' : '校验上传包'}
            </button>
          </div>

          {verifyResult ? (
            <div className="sh-helper-block">
              <h3 className="sh-helper-block-title">校验结果</h3>
              <p>上传记录：{verifyResult.uploadId}</p>
              <p>模块：{verifyResult.displayName || verifyResult.moduleId || '未识别'}</p>
              <p>版本：{verifyResult.version || '未识别'}</p>
              <p>结果：{verifyResult.isValid ? '通过' : '失败'}</p>
              {verifyResult.errors.length > 0 ? (
                <ul>
                  {verifyResult.errors.map((error) => (
                    <li key={error}>{error}</li>
                  ))}
                </ul>
              ) : null}
              {verifyResult.isValid ? (
                <button
                  type="button"
                  className="sh-btn sh-btn-primary"
                  onClick={() => void runLifecycleAction(
                    'install-upload',
                    async () => await installUpload(verifyResult.uploadId),
                  )}
                  disabled={busyAction === 'install-upload'}
                >
                  {busyAction === 'install-upload' ? '安装中...' : '安装已校验包'}
                </button>
              ) : null}
            </div>
          ) : null}
        </div>
      </section>

      <section className="sh-page-panel">
        <div className="sh-page-panel-header">
          <h2 className="sh-page-panel-title">模块状态</h2>
          <div className="sh-page-panel-meta">共 {modules.length} 个模块</div>
        </div>
        <div className="sh-page-panel-body">
          <div className="sh-table-wrap">
            <table className="sh-table">
              <thead>
                <tr>
                  <th>模块</th>
                  <th>状态</th>
                  <th>已安装版本</th>
                  <th>期望状态</th>
                  <th>当前活动</th>
                  <th>最近良好</th>
                  <th>最近操作</th>
                  <th>最近错误</th>
                  <th>运维动作</th>
                </tr>
              </thead>
              <tbody>
                {modules.map((module) => (
                  <tr key={module.moduleId}>
                    <td>
                      <div>{module.displayName}</div>
                      <div>{module.moduleId}</div>
                    </td>
                    <td>{getStatusLabel(module.status)}</td>
                    <td>{getVersionsLabel(module.installedVersions)}</td>
                    <td>
                      {module.desiredEnabled ? module.desiredVersion ?? '未选择' : '禁用'}
                      {module.restartRequired ? '（等待重启）' : ''}
                    </td>
                    <td>{module.activeVersion ?? module.currentEnabledVersion ?? '未启用'}</td>
                    <td>{module.lastKnownGoodVersion ?? '无'}</td>
                    <td>{module.lastOperation ?? '无'}</td>
                    <td>{module.lastError ?? '无'}</td>
                    <td>
                      <div className="sh-form-grid">
                        <label className="sh-form-field">
                          <span>启用版本</span>
                          <select
                            value={selectedVersions[module.moduleId] ?? ''}
                            onChange={(event) => {
                              // 启用目标版本由用户显式选择，不自动回填当前启用版本；
                              // 这样可以区分“查看当前状态”和“准备执行的下一次切换”。
                              const nextVersion = event.target.value;
                              setSelectedVersions((current) => ({
                                ...current,
                                [module.moduleId]: nextVersion,
                              }));
                            }}
                          >
                            {module.installedVersions.length === 0 ? (
                              <option value="">暂无可选版本</option>
                            ) : (
                              module.installedVersions.map((version) => (
                                <option key={version} value={version}>
                                  {version}
                                </option>
                              ))
                            )}
                          </select>
                        </label>
                        <button
                          type="button"
                          className="sh-btn sh-btn-primary"
                          onClick={() => void runLifecycleAction(
                            `enable-${module.moduleId}`,
                            async () => await enableModule(
                              module.moduleId,
                              selectedVersions[module.moduleId] ?? '',
                            ),
                          )}
                          disabled={
                            !module.canEnable
                            || !(selectedVersions[module.moduleId] ?? '').trim()
                            || busyAction === `enable-${module.moduleId}`
                          }
                        >
                          启用
                        </button>
                        <button
                          type="button"
                          className="sh-btn"
                          onClick={() => void runLifecycleAction(
                            `disable-${module.moduleId}`,
                            async () => await disableModule(module.moduleId),
                          )}
                          disabled={
                            !module.canDisable || busyAction === `disable-${module.moduleId}`
                          }
                        >
                          禁用
                        </button>
                        <button
                          type="button"
                          className="sh-btn"
                          onClick={() => void runLifecycleAction(
                            `rollback-${module.moduleId}`,
                            async () => await rollbackModule(module.moduleId),
                          )}
                          disabled={
                            !module.canRollback || busyAction === `rollback-${module.moduleId}`
                          }
                        >
                          回滚
                        </button>
                        <button
                          type="button"
                          className="sh-btn"
                          onClick={() => void runLifecycleAction(
                            `uninstall-${module.moduleId}`,
                            async () => await uninstallModule(module.moduleId),
                          )}
                          disabled={
                            !module.canUninstall || busyAction === `uninstall-${module.moduleId}`
                          }
                        >
                          卸载
                        </button>
                      </div>
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
