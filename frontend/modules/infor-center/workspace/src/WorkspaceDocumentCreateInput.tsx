import { useEffect, useId, useRef, useState } from 'react';
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
import { documentBusinessValues, type WorkspaceDocumentDraft } from './workspaceDocumentUpload';
import { WorkspaceDocumentSession, type DocumentUploadSnapshot, type UploadChoice } from './workspaceDocumentSession';

/** Document 输入只持有本次暂存；共享 Drawer 继续拥有草稿保护、提交和成功刷新。 */
export default function WorkspaceDocumentCreateInput({ value, onChange, queryScope, featurePosition, onDraftStateChange, submitting }: StandardCrudActionInputRenderContext) {
  const { t } = useLocalization();
  const inputId = useId(); const attachmentInputId = useId();
  const fileInput = useRef<HTMLInputElement>(null); const attachmentInput = useRef<HTMLInputElement>(null);
  const draft = typeof value === 'object' && value !== null ? value as WorkspaceDocumentDraft : {};
  const latest = useRef({ draft, onChange, queryScope }); latest.current = { draft, onChange, queryScope };
  const [view, setView] = useState<{ session: WorkspaceDocumentSession; snapshot: DocumentUploadSnapshot }>();
  const positionKey = JSON.stringify(queryScope);
  // 每次实际生命周期 setup 创建独立会话；StrictMode 重挂载不能复用已经结束的实例。
  useEffect(() => {
    const session = new WorkspaceDocumentSession(latest.current.queryScope, featurePosition, () => latest.current.draft);
    const observe = () => setView({ session, snapshot: session.getSnapshot() });
    const unsubscribe = session.subscribe(observe);
    observe();
    latest.current.onChange({ ...documentBusinessValues(latest.current.draft), session });
    return () => { unsubscribe(); session.close(); };
  }, [positionKey, featurePosition]);
  const uploads = view?.snapshot.choices ?? [];
  const main = uploads.find(item => item.role === 'main'); const attachments = uploads.filter(item => item.role === 'attachment');
  const fieldsReady = ['ECODE', 'ENAME'].every(key => typeof draft[key] === 'string' && String(draft[key]).trim().length > 0);
  const dirty = Object.values(documentBusinessValues(draft)).some(item => typeof item === 'string' && item.length > 0) || uploads.length > 0;
  const ready = fieldsReady && Boolean(view?.session.canSubmit()) && !submitting;
  const unknownSave = view?.snapshot.phase === 'unknown';
  const failure = view?.snapshot.tooManyAttachments ? t('Document.TooManyAttachments', '最多可选择100个附件。') : view?.snapshot.failure;
  const blocked = submitting || !view || view.snapshot.closed || view.snapshot.phase !== 'idle';
  useEffect(() => { onDraftStateChange?.({ dirty, ready }); }, [dirty, ready, onDraftStateChange]);
  const publish = (next: WorkspaceDocumentDraft) => onChange({ ...documentBusinessValues(next), session: view?.session });
  const choose = (files: File[], role: UploadChoice['role']) => view?.session.choose(files, role);
  const remove = (choice: UploadChoice) => view?.session.remove(choice.id);
  const retry = (choice: UploadChoice) => view?.session.retry(choice.id);
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
            <Button htmlType="button" disabled={!fieldsReady || blocked || main?.state === 'uploading'}
              onClick={() => fileInput.current?.click()}>{t('Document.SelectMainFile', '选择主文件')}</Button>
            {main ? <Button htmlType="button" disabled={blocked}
              onClick={() => remove(main)}>{t('Document.CancelUpload', '取消上传')}</Button> : null}
          </Space>
          <input ref={fileInput} id={inputId} type="file" hidden accept=".jpg,.jpeg,.png,.pdf,.doc,.docx,.xls,.xlsx"
            disabled={!fieldsReady || blocked || main?.state === 'uploading'}
            onChange={event => { const file = event.currentTarget.files?.[0]; event.currentTarget.value = ''; if (file) choose([file], 'main'); }} />
          {main ? <Typography.Text title={main.file.name} style={{ wordBreak: 'break-all' }}>{main.file.name}</Typography.Text>
            : <Typography.Text type="secondary">{t('Document.NoMainFile', '尚未选择主文件')}</Typography.Text>}
          {main ? <UploadStatus choice={main} /> : null}
          <Space wrap>
            <label htmlFor={attachmentInputId}><Typography.Text strong>{t('Document.Attachments', '附件')}</Typography.Text></label>
            <Button htmlType="button" disabled={!fieldsReady || blocked || attachments.length >= 100}
              onClick={() => attachmentInput.current?.click()}>{t('Document.SelectAttachments', '选择附件')}</Button>
          </Space>
          <input ref={attachmentInput} id={attachmentInputId} type="file" hidden multiple accept=".jpg,.jpeg,.png,.pdf,.doc,.docx,.xls,.xlsx"
            disabled={!fieldsReady || blocked || attachments.length >= 100}
            onChange={event => { const files = Array.from(event.currentTarget.files ?? []); event.currentTarget.value = ''; if (files.length) choose(files, 'attachment'); }} />
          {attachments.map(choice => <div key={choice.id} role="group" aria-label={`${t('Document.Attachment', '附件')} ${choice.file.name}`}>
            <Space direction="vertical" style={{ width: '100%' }}>
              <Space wrap><Typography.Text title={choice.file.name} style={{ wordBreak: 'break-all' }}>{choice.file.name}</Typography.Text>
                <Button htmlType="button" size="small" disabled={blocked} aria-label={`${t('Document.RemoveAttachment', '移除附件')} ${choice.file.name}`}
                  onClick={() => remove(choice)}>{t('Document.RemoveAttachment', '移除附件')}</Button>
                {choice.state === 'failed' ? <Button htmlType="button" size="small" disabled={blocked}
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
