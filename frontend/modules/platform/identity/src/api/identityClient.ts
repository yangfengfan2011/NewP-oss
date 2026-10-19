import { platformFetch } from '@icp-platform/core';
import { readStandardCrudRequestError, StandardCrudRequestError, type StandardCrudFieldErrorFact, type StandardCrudDetailReadContext, type StandardCrudFeatureRequestContext } from '@icp-platform/sdk';

export type IdentityAccountStatus = 'Enabled' | 'Disabled' | 'Deleted';

export interface IdentityAccount {
  eid: string;
  ecode: string;
  ename: string;
  status: IdentityAccountStatus;
  revision: number;
  isLocalAdministrator: boolean;
  isPersonnelBound: boolean;
  personnelDisableHold: boolean;
  createdBy?: string;
  createdAt?: string;
  updatedBy?: string;
  updatedAt?: string;
  CreatedBy_DisplayValue?: string | null;
  CreatedAt_DisplayValue?: string | null;
  UpdatedBy_DisplayValue?: string | null;
  UpdatedAt_DisplayValue?: string | null;
}

export interface IdentitySecurityEvent {
  eid: string;
  eventType: string;
  reason: string;
  occurredAt: string;
  occurredAt_DisplayValue: string | null;
  traceId: string;
  actorKind: string;
  actorAccountEID?: string;
}

export interface IdentityPage<T> {
  items: T[];
  page: number;
  pageSize: number;
  totalCount: number;
}

export interface IdentityCredentialIssuance {
  account: IdentityAccount;
  temporaryCredential: string;
}

export interface IdentitySecurityCommand {
  expectedRevision: number;
  reason: string;
}

interface IdentitySecurityEventsReadContext extends StandardCrudFeatureRequestContext {
  readonly signal?: AbortSignal;
}

export class IdentityApiError extends StandardCrudRequestError {
  constructor(
    code: string,
    status: number,
    public readonly current?: IdentityAccount,
    public readonly errors?: Record<string, string[]>,
    source?: StandardCrudRequestError,
  ) {
    const fieldErrors = source?.fieldErrors ?? Object.fromEntries(Object.entries(errors ?? {}).map(([field, codes]) => [field, codes[0] ?? '']));
    const details: StandardCrudFieldErrorFact[] = source?.fieldErrorDetails ?? Object.entries(fieldErrors).map(([field, value]) => ({ field, code: value }));
    super(source?.message ?? code, status, fieldErrors, {
      ...source?.problem, code,
      reasonKey: code === 'IDENTITY_REVISION_CONFLICT' && current ? 'IdentityMessages.RevisionConflictDetails'
        : source?.reasonKey ?? (code.startsWith('IDENTITY_') ? `IdentityErrors.${code}` : undefined),
      args: { ...source?.args, ...(code === 'IDENTITY_REVISION_CONFLICT' && current ? { status: current.status, revision: current.revision } : {}) },
      fieldErrors: details.map(item => ({ ...item,
        reasonKey: item.reasonKey ?? (item.code?.startsWith('IDENTITY_') ? `IdentityErrors.${item.code}` : undefined),
      })),
    }, source?.responseProblem, source?.cause);
    this.name = 'IdentityApiError';
  }
}

export function getAccountSnapshot(
  accountEid: string,
  readContext?: StandardCrudDetailReadContext,
): Promise<IdentityAccount> {
  return requestJson(`/api/identity/accounts/${encodeURIComponent(accountEid)}/snapshot`, {
    method: 'GET',
    ...(readContext?.signal ? { signal: readContext.signal } : {}),
    ...(readContext?.featurePosition || readContext?.dataAccess ? { headers: readHeaders(readContext) } : {}),
  });
}

export function listAccountSecurityEvents(
  accountEid: string,
  page: number,
  pageSize: number,
  readContext?: IdentitySecurityEventsReadContext,
): Promise<IdentityPage<IdentitySecurityEvent>> {
  return requestJson(
    `/api/identity/accounts/${encodeURIComponent(accountEid)}/security-events?page=${page}&pageSize=${pageSize}`,
    {
      method: 'GET',
      ...(readContext?.signal ? { signal: readContext.signal } : {}),
      ...(readContext?.featurePosition || readContext?.dataAccess ? { headers: readHeaders(readContext) } : {}),
    },
  );
}

export function createAccount(input: { ecode: string; ename: string }, context?: StandardCrudFeatureRequestContext): Promise<IdentityCredentialIssuance> {
  return requestJson('/api/identity/accounts', jsonRequest('POST', input, context));
}

export function updateAccountName(
  accountEid: string,
  input: { ename: string; expectedRevision: number },
  context?: StandardCrudFeatureRequestContext,
): Promise<IdentityAccount> {
  return requestJson(`/api/identity/accounts/${encodeURIComponent(accountEid)}`, jsonRequest('PUT', input, context));
}

export function setAccountStatus(
  accountEid: string,
  input: IdentitySecurityCommand & { targetStatus: Exclude<IdentityAccountStatus, 'Deleted'> },
  context?: StandardCrudFeatureRequestContext,
): Promise<IdentityAccount> {
  return requestJson(`/api/identity/accounts/${encodeURIComponent(accountEid)}/status/${input.targetStatus === 'Enabled' ? 'enable' : 'disable'}`, jsonRequest('PUT', input, context));
}

export function resetCredential(
  accountEid: string,
  input: IdentitySecurityCommand,
  context?: StandardCrudFeatureRequestContext,
): Promise<IdentityCredentialIssuance> {
  return requestJson(`/api/identity/accounts/${encodeURIComponent(accountEid)}/credential-reset`, jsonRequest('POST', input, context));
}

export function deleteAccount(accountEid: string, input: IdentitySecurityCommand, context?: StandardCrudFeatureRequestContext): Promise<IdentityAccount> {
  return requestJson(`/api/identity/accounts/${encodeURIComponent(accountEid)}/delete`, jsonRequest('POST', input, context));
}

function jsonRequest(method: string, body: unknown, context?: StandardCrudFeatureRequestContext): RequestInit {
  return {
    method,
    headers: { 'Content-Type': 'application/json', Accept: 'application/json', ...featureHeaders(context) },
    body: JSON.stringify(body),
  };
}

function featureHeaders(context?: StandardCrudFeatureRequestContext): Record<string, string> {
  return context?.featurePosition ? { 'X-NewP-Feature': context.featurePosition } : {};
}

function readHeaders(context?: StandardCrudFeatureRequestContext): Record<string, string> {
  return { ...featureHeaders(context), ...(context?.dataAccess ? { 'X-NewP-Data-Declaration': context.dataAccess.id } : {}) };
}

async function requestJson<T>(url: string, init: RequestInit): Promise<T> {
  const response = await platformFetch(url, init);
  if (!response.ok) {
    const failure = await readStandardCrudRequestError(response);
    const problem = failure.responseProblem as { current?: IdentityAccount; errors?: Record<string, string[]> };
    throw new IdentityApiError(failure.code ?? `HTTP_${response.status}`, response.status, problem.current, problem.errors, failure);
  }
  return await response.json() as T;
}
