import { useEffect, useId, useMemo, useRef, useState } from 'react';
import { formatRequestError, useLocalization } from '@icp-platform/core';
import type { StandardCrudActionInputRenderContext, StandardCrudFieldDefinition } from '@icp-platform/sdk';
import { StandardCrudForm } from '@icp-platform/ui/crud-form';
import Alert from 'antd/es/alert';
import Button from 'antd/es/button';
import Card from 'antd/es/card';
import Progress from 'antd/es/progress';
import Space from 'antd/es/space';
import Spin from 'antd/es/spin';
import Typography from 'antd/es/typography';
import { cancelDocumentStage, stageDocumentFile, type DocumentStage, type DocumentSubmissionLifecycle, type WorkspaceDocumentDraft } from './workspaceDocumentUpload';

/** Document 输入只持有本次暂存；共享 Drawer 继续拥有草稿保护、提交和成功刷新。 */
export default function WorkspaceDocumentCreateInput({ value, onChange, queryScope, featurePosition, onDraftStateChange, submitting }: StandardCrudActionInputRenderContext) {
  const { t } = useLocalization();
  const inputId = useId(); const fileInput = useRef<HTMLInputElement>(null);
  const draft = typeof value === 'object' && value !== null ? value as WorkspaceDocumentDraft : {};
  const latest = useRef({ draft, onChange }); latest.current = { draft, onChange };
  const generation = useRef(0); const mounted = useRef(true); const controller = useRef<AbortController>();
  const staged = useRef<DocumentStage>(); const commitState = useRef<'idle' | 'submitting' | 'committed' | 'unknown'>('idle');
  const [upload, setUpload] = useState<{ state: 'empty' | 'uploading' | 'staged' | 'failed'; name?: string; percent?: number }>({ state: 'empty' });
  const [failure, setFailure] = useState<unknown>(); const [unknownSave, setUnknownSave] = useState(false);
  const positionKey = JSON.stringify(queryScope);
  const submission = useMemo<DocumentSubmissionLifecycle>(() => ({
    begin: () => { commitState.current = 'submitting'; },
    committed: () => { commitState.current = 'committed'; },
    failed: unknown => {
      commitState.current = unknown ? 'unknown' : 'idle';
      if (mounted.current) setUnknownSave(unknown);
    },
  }), []);
  // 位置、功能或主体边界卸载旧输入；在途/未知业务提交不能被盲取消。
  useEffect(() => {
    const lifetimeGeneration = generation;
    mounted.current = true;
    staged.current = undefined; commitState.current = 'idle'; setUpload({ state: 'empty' }); setFailure(undefined); setUnknownSave(false);
    latest.current.onChange({ ...latest.current.draft, mainUploadId: undefined, submission });
    return () => {
      mounted.current = false; lifetimeGeneration.current++; controller.current?.abort();
      if (staged.current && commitState.current === 'idle') void cancelDocumentStage(staged.current.uploadId, featurePosition).catch(() => undefined);
    };
  }, [positionKey, featurePosition, submission]);
  const fieldsReady = ['ECODE', 'ENAME'].every(key => typeof draft[key] === 'string' && String(draft[key]).trim().length > 0);
  const dirty = Object.values(draft).some(item => typeof item === 'string' && item.length > 0) || upload.state !== 'empty';
  useEffect(() => {
    onDraftStateChange?.({ dirty, ready: fieldsReady && upload.state === 'staged' && !unknownSave && !submitting });
  }, [dirty, fieldsReady, upload.state, unknownSave, submitting, onDraftStateChange]);
  const publish = (next: WorkspaceDocumentDraft) => latest.current.onChange({ ...next, mainUploadId: staged.current?.uploadId, submission });
  const cancel = async () => {
    generation.current++; controller.current?.abort();
    const previous = staged.current; staged.current = undefined;
    setUpload({ state: 'empty' }); setFailure(undefined); publish(latest.current.draft);
    if (previous) try { await cancelDocumentStage(previous.uploadId, featurePosition); } catch (error) { if (mounted.current) setFailure(error); }
  };
  const choose = async (file: File) => {
    if (commitState.current !== 'idle') return;
    const choice = generation.current + 1;
    await cancel();
    if (!mounted.current || choice !== generation.current) return;
    const attempt = generation.current; const abort = new AbortController(); controller.current = abort;
    setUpload({ state: 'uploading', name: file.name, percent: 0 });
    try {
      const result = await stageDocumentFile(file, latest.current.draft, queryScope, featurePosition, abort.signal, (loaded, total) => {
        if (attempt === generation.current && mounted.current && !abort.signal.aborted)
          setUpload({ state: 'uploading', name: file.name, percent: total ? Math.min(100, Math.round(loaded / total * 100)) : undefined });
      });
      if (attempt !== generation.current || !mounted.current || abort.signal.aborted) {
        void cancelDocumentStage(result.uploadId, featurePosition).catch(() => undefined); return;
      }
      staged.current = result; setUpload({ state: 'staged', name: result.fileName }); publish(latest.current.draft);
    } catch (error) {
      if (attempt === generation.current && mounted.current && !abort.signal.aborted) {
        setUpload({ state: 'failed', name: file.name }); setFailure(error);
      }
    }
  };
  const fields: StandardCrudFieldDefinition[] = [
    { key: 'ECODE', modelField: 'ECODE', labelKey: t('Document.Code', '编码'), kind: 'text', required: true },
    { key: 'ENAME', modelField: 'ENAME', labelKey: t('Document.Name', '名称'), kind: 'text', required: true },
    { key: 'Description', modelField: 'Description', labelKey: t('Document.Field_Description', '说明'), kind: 'textarea' },
  ];
  return <>
    <StandardCrudForm formId="workspace-createDocument" mode="create" fields={fields} sections={[]} values={draft}
      onChange={(key, next) => publish({ ...draft, [key]: next })} onSubmit={() => undefined}
      saving={submitting ?? false} showSubmitAction={false} titleForSection={() => t('Workspace.Basic', '基本信息')} />
    <section aria-label={t('Document.Block_Files', '文件')}>
      <Card title={t('Document.Block_Files', '文件')} style={{ marginTop: 16 }}>
        <Space direction="vertical" size="middle" style={{ width: '100%' }}>
          <Space wrap>
            <label htmlFor={inputId}><Typography.Text strong>{t('Document.MainFile', '主文件')}</Typography.Text></label>
            <Button htmlType="button" disabled={!fieldsReady || submitting || unknownSave || upload.state === 'uploading'}
              onClick={() => fileInput.current?.click()}>{t('Document.SelectMainFile', '选择主文件')}</Button>
            {upload.state === 'uploading' || upload.state === 'staged' ? <Button htmlType="button" disabled={submitting || unknownSave}
              onClick={() => void cancel()}>{t('Document.CancelUpload', '取消上传')}</Button> : null}
          </Space>
          <input ref={fileInput} id={inputId} type="file" hidden accept=".jpg,.jpeg,.png,.pdf,.doc,.docx,.xls,.xlsx"
            disabled={!fieldsReady || submitting || unknownSave || upload.state === 'uploading'}
            onChange={event => { const file = event.currentTarget.files?.[0]; event.currentTarget.value = ''; if (file) void choose(file); }} />
          {upload.name ? <Typography.Text title={upload.name} style={{ wordBreak: 'break-all' }}>{upload.name}</Typography.Text>
            : <Typography.Text type="secondary">{t('Document.NoMainFile', '尚未选择主文件')}</Typography.Text>}
          {upload.state === 'uploading' ? upload.percent === undefined
            ? <Space role="status"><Spin size="small" /><Typography.Text>{t('Document.Uploading', '正在上传…')}</Typography.Text></Space>
            : <Progress percent={upload.percent} status="active" aria-label={t('Document.UploadProgress', '上传进度')} /> : null}
          {upload.state === 'staged' ? <Typography.Text role="status" type="success">{t('Document.Staged', '主文件已上传，保存后生效。')}</Typography.Text> : null}
          {failure ? <Alert type="error" showIcon message={formatRequestError(failure, t)} /> : null}
          {unknownSave ? <Alert type="warning" showIcon message={t('Document.SaveOutcomeUnknown', '保存结果尚未确认，请关闭后刷新列表核对；本次文件不会自动取消或重复保存。')} /> : null}
        </Space>
      </Card>
    </section>
  </>;
}
