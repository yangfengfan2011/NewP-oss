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

interface UploadChoice {
  id: number; role: 'main' | 'attachment'; file: File;
  state: 'queued' | 'uploading' | 'staged' | 'failed'; percent?: number; staged?: DocumentStage; failure?: unknown;
}

/** Document 输入只持有本次暂存；共享 Drawer 继续拥有草稿保护、提交和成功刷新。 */
export default function WorkspaceDocumentCreateInput({ value, onChange, queryScope, featurePosition, onDraftStateChange, submitting }: StandardCrudActionInputRenderContext) {
  const { t } = useLocalization();
  const inputId = useId(); const attachmentInputId = useId();
  const fileInput = useRef<HTMLInputElement>(null); const attachmentInput = useRef<HTMLInputElement>(null);
  const draft = typeof value === 'object' && value !== null ? value as WorkspaceDocumentDraft : {};
  const latest = useRef({ draft, onChange }); latest.current = { draft, onChange };
  const generation = useRef(0); const mounted = useRef(true); const sequence = useRef(0);
  const choices = useRef<UploadChoice[]>([]); const active = useRef<{ id: number; controller: AbortController }>();
  const pump = useRef<() => void>(); const commitState = useRef<'idle' | 'submitting' | 'committed' | 'unknown'>('idle');
  const [uploads, setUploads] = useState<UploadChoice[]>([]);
  const [failure, setFailure] = useState<unknown>(); const [unknownSave, setUnknownSave] = useState(false);
  const positionKey = JSON.stringify(queryScope);
  const submission = useMemo<DocumentSubmissionLifecycle>(() => ({
    begin: () => {
      if (commitState.current !== 'idle') return false;
      commitState.current = 'submitting'; return true;
    },
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
    choices.current = []; commitState.current = 'idle'; setUploads([]); setFailure(undefined); setUnknownSave(false);
    latest.current.onChange({ ...latest.current.draft, mainUploadId: undefined, attachmentUploadIds: [], submission });
    return () => {
      mounted.current = false; lifetimeGeneration.current++; active.current?.controller.abort();
      const abandoned = choices.current;
      if (commitState.current === 'idle') void (async () => {
        for (const choice of abandoned) if (choice.staged)
          try { await cancelDocumentStage(choice.staged.uploadId, featurePosition); } catch { /* TTL 收敛无法确认的取消。 */ }
      })();
    };
  }, [positionKey, featurePosition, submission]);
  const main = uploads.find(item => item.role === 'main'); const attachments = uploads.filter(item => item.role === 'attachment');
  const fieldsReady = ['ECODE', 'ENAME'].every(key => typeof draft[key] === 'string' && String(draft[key]).trim().length > 0);
  const dirty = Object.values(draft).some(item => typeof item === 'string' && item.length > 0) || uploads.length > 0;
  const filesReady = main?.state === 'staged' && uploads.every(item => item.state === 'staged');
  useEffect(() => {
    onDraftStateChange?.({ dirty, ready: fieldsReady && filesReady && !unknownSave && !submitting });
  }, [dirty, fieldsReady, filesReady, unknownSave, submitting, onDraftStateChange]);
  const publish = (next: WorkspaceDocumentDraft) => latest.current.onChange({ ...next,
    mainUploadId: choices.current.find(item => item.role === 'main')?.staged?.uploadId,
    attachmentUploadIds: choices.current.filter(item => item.role === 'attachment' && item.staged).map(item => item.staged!.uploadId), submission });
  const update = (next: UploadChoice[]) => { choices.current = next; setUploads(next); publish(latest.current.draft); };
  // 只有此泵持有一个 transport；移除在途项先 abort，迟到结果按本次选择身份收敛。
  const startNext = async () => {
    if (active.current || !mounted.current || commitState.current !== 'idle') return;
    const choice = choices.current.find(item => item.state === 'queued'); if (!choice) return;
    const attempt = generation.current; const controller = new AbortController(); const running = { id: choice.id, controller }; active.current = running;
    update(choices.current.map(item => item.id === choice.id ? { ...item, state: 'uploading', percent: 0 } : item));
    try {
      const result = await stageDocumentFile(choice.file, latest.current.draft, queryScope, featurePosition, controller.signal, (loaded, total) => {
        if (attempt === generation.current && mounted.current && !controller.signal.aborted)
          update(choices.current.map(item => item.id === choice.id ? { ...item, percent: total ? Math.min(100, Math.round(loaded / total * 100)) : undefined } : item));
      });
      if (attempt !== generation.current || !mounted.current || controller.signal.aborted || !choices.current.some(item => item.id === choice.id)) {
        void cancelDocumentStage(result.uploadId, featurePosition).catch(() => undefined); return;
      }
      update(choices.current.map(item => item.id === choice.id ? { ...item, state: 'staged', staged: result } : item));
    } catch (error) {
      if (attempt === generation.current && mounted.current && !controller.signal.aborted)
        update(choices.current.map(item => item.id === choice.id ? { ...item, state: 'failed', failure: error } : item));
    } finally {
      if (active.current === running) active.current = undefined;
      pump.current?.();
    }
  };
  pump.current = () => { void startNext(); };
  const remove = (choice: UploadChoice) => {
    if (commitState.current !== 'idle') return;
    if (active.current?.id === choice.id) active.current.controller.abort();
    update(choices.current.filter(item => item.id !== choice.id)); setFailure(undefined);
    if (choice.staged) void cancelDocumentStage(choice.staged.uploadId, featurePosition).catch(error => { if (mounted.current) setFailure(error); });
  };
  const choose = (files: File[], role: UploadChoice['role']) => {
    if (commitState.current !== 'idle') return;
    if (role === 'attachment' && choices.current.filter(item => item.role === 'attachment').length + files.length > 100) {
      setFailure(t('Document.TooManyAttachments', '最多可选择100个附件。')); return;
    }
    if (role === 'main') { const previous = choices.current.find(item => item.role === 'main'); if (previous) remove(previous); }
    setFailure(undefined);
    update([...choices.current, ...files.map(file => ({ id: ++sequence.current, role, file, state: 'queued' as const }))]);
    pump.current?.();
  };
  const retry = (choice: UploadChoice) => {
    if (commitState.current !== 'idle') return;
    update(choices.current.map(item => item.id === choice.id ? { ...item, state: 'queued', failure: undefined } : item)); pump.current?.();
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
            <Button htmlType="button" disabled={!fieldsReady || submitting || unknownSave || main?.state === 'uploading'}
              onClick={() => fileInput.current?.click()}>{t('Document.SelectMainFile', '选择主文件')}</Button>
            {main ? <Button htmlType="button" disabled={submitting || unknownSave}
              onClick={() => remove(main)}>{t('Document.CancelUpload', '取消上传')}</Button> : null}
          </Space>
          <input ref={fileInput} id={inputId} type="file" hidden accept=".jpg,.jpeg,.png,.pdf,.doc,.docx,.xls,.xlsx"
            disabled={!fieldsReady || submitting || unknownSave || main?.state === 'uploading'}
            onChange={event => { const file = event.currentTarget.files?.[0]; event.currentTarget.value = ''; if (file) choose([file], 'main'); }} />
          {main ? <Typography.Text title={main.file.name} style={{ wordBreak: 'break-all' }}>{main.file.name}</Typography.Text>
            : <Typography.Text type="secondary">{t('Document.NoMainFile', '尚未选择主文件')}</Typography.Text>}
          {main ? <UploadStatus choice={main} /> : null}
          <Space wrap>
            <label htmlFor={attachmentInputId}><Typography.Text strong>{t('Document.Attachments', '附件')}</Typography.Text></label>
            <Button htmlType="button" disabled={!fieldsReady || submitting || unknownSave || attachments.length >= 100}
              onClick={() => attachmentInput.current?.click()}>{t('Document.SelectAttachments', '选择附件')}</Button>
          </Space>
          <input ref={attachmentInput} id={attachmentInputId} type="file" hidden multiple accept=".jpg,.jpeg,.png,.pdf,.doc,.docx,.xls,.xlsx"
            disabled={!fieldsReady || submitting || unknownSave || attachments.length >= 100}
            onChange={event => { const files = Array.from(event.currentTarget.files ?? []); event.currentTarget.value = ''; if (files.length) choose(files, 'attachment'); }} />
          {attachments.map(choice => <div key={choice.id} role="group" aria-label={`${t('Document.Attachment', '附件')} ${choice.file.name}`}>
            <Space direction="vertical" style={{ width: '100%' }}>
              <Space wrap><Typography.Text title={choice.file.name} style={{ wordBreak: 'break-all' }}>{choice.file.name}</Typography.Text>
                <Button htmlType="button" size="small" disabled={submitting || unknownSave} aria-label={`${t('Document.RemoveAttachment', '移除附件')} ${choice.file.name}`}
                  onClick={() => remove(choice)}>{t('Document.RemoveAttachment', '移除附件')}</Button>
                {choice.state === 'failed' ? <Button htmlType="button" size="small" disabled={submitting || unknownSave}
                  aria-label={`${t('Document.RetryUpload', '重试上传')} ${choice.file.name}`} onClick={() => retry(choice)}>{t('Document.RetryUpload', '重试上传')}</Button> : null}
              </Space>
              <UploadStatus choice={choice} />
            </Space>
          </div>)}
          {failure ? <Alert type="error" showIcon message={typeof failure === 'string' ? failure : formatRequestError(failure, t)} /> : null}
          {unknownSave ? <Alert type="warning" showIcon message={t('Document.SaveOutcomeUnknown', '保存结果尚未确认，请关闭后刷新列表核对；本次文件不会自动取消或重复保存。')} /> : null}
        </Space>
      </Card>
    </section>
  </>;
}

function UploadStatus({ choice }: { choice: UploadChoice }) {
  const { t } = useLocalization();
  return <>
    {choice.state === 'queued' ? <Typography.Text role="status" type="secondary">{t('Document.UploadQueued', '等待上传')}</Typography.Text> : null}
    {choice.state === 'uploading' ? choice.percent === undefined
      ? <Space role="status"><Spin size="small" /><Typography.Text>{t('Document.Uploading', '正在上传…')}</Typography.Text></Space>
      : <Progress percent={choice.percent} status="active" aria-label={t('Document.UploadProgress', '上传进度')} /> : null}
    {choice.state === 'staged' ? <Typography.Text role="status" type="success">{choice.role === 'main'
      ? t('Document.Staged', '主文件已上传，保存后生效。') : t('Document.AttachmentStaged', '附件已上传，保存后生效。')}</Typography.Text> : null}
    {choice.failure ? <Alert type="error" showIcon message={formatRequestError(choice.failure, t)} /> : null}
  </>;
}
