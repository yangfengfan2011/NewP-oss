import { platformFetch } from '@icp-platform/core';

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
}

export interface IdentitySecurityEvent {
  eid: string;
  eventType: string;
  reason: string;
  occurredAt: string;
  traceId: string;
  actorKind: string;
  actorUserEID?: string;
}

export interface IdentityPage<T> {
  items: T[];
  page: number;
  pageSize: number;
  totalCount: number;
}

export interface IdentityUserDetail {
  account: IdentityAccount;
  events: IdentityPage<IdentitySecurityEvent>;
}

export interface IdentityCredentialIssuance {
  account: IdentityAccount;
  temporaryCredential: string;
}

export interface IdentityListFilters {
  query?: string;
  status?: IdentityAccountStatus;
  isPersonnelBound?: boolean;
  isLocalAdministrator?: boolean;
  page: number;
  pageSize: number;
}

export interface IdentitySecurityCommand {
  expectedRevision: number;
  reason: string;
}

export class IdentityApiError extends Error {
  constructor(
    public readonly code: string,
    public readonly status: number,
    public readonly current?: IdentityAccount,
    public readonly errors?: Record<string, string[]>,
  ) {
    super(code);
    this.name = 'IdentityApiError';
  }
}

export async function listUsers(filters: IdentityListFilters): Promise<IdentityPage<IdentityAccount>> {
  const query = new URLSearchParams();
  if (filters.query) query.set('query', filters.query);
  if (filters.status) query.set('status', filters.status);
  if (filters.isPersonnelBound !== undefined) query.set('isPersonnelBound', String(filters.isPersonnelBound));
  if (filters.isLocalAdministrator !== undefined) query.set('isLocalAdministrator', String(filters.isLocalAdministrator));
  query.set('page', String(filters.page));
  query.set('pageSize', String(filters.pageSize));
  return requestJson(`/api/identity/users?${query.toString()}`, { method: 'GET' });
}

export function getUser(userEid: string, eventPage = 1): Promise<IdentityUserDetail> {
  return requestJson(`/api/identity/users/${encodeURIComponent(userEid)}?eventPage=${eventPage}&eventPageSize=20`, {
    method: 'GET',
  });
}

export function createUser(input: { ecode: string; ename: string }): Promise<IdentityCredentialIssuance> {
  return requestJson('/api/identity/users', jsonRequest('POST', input));
}

export function updateUserName(
  userEid: string,
  input: { ename: string; expectedRevision: number },
): Promise<IdentityAccount> {
  return requestJson(`/api/identity/users/${encodeURIComponent(userEid)}`, jsonRequest('PUT', input));
}

export function setUserStatus(
  userEid: string,
  input: IdentitySecurityCommand & { targetStatus: Exclude<IdentityAccountStatus, 'Deleted'> },
): Promise<IdentityAccount> {
  return requestJson(`/api/identity/users/${encodeURIComponent(userEid)}/status`, jsonRequest('PUT', input));
}

export function resetCredential(
  userEid: string,
  input: IdentitySecurityCommand,
): Promise<IdentityCredentialIssuance> {
  return requestJson(`/api/identity/users/${encodeURIComponent(userEid)}/credential-reset`, jsonRequest('POST', input));
}

export function deleteUser(userEid: string, input: IdentitySecurityCommand): Promise<IdentityAccount> {
  return requestJson(`/api/identity/users/${encodeURIComponent(userEid)}/delete`, jsonRequest('POST', input));
}

function jsonRequest(method: string, body: unknown): RequestInit {
  return {
    method,
    headers: { 'Content-Type': 'application/json', Accept: 'application/json' },
    body: JSON.stringify(body),
  };
}

async function requestJson<T>(url: string, init: RequestInit): Promise<T> {
  const response = await platformFetch(url, init);
  if (!response.ok) {
    let problem: {
      code?: string;
      current?: IdentityAccount;
      errors?: Record<string, string[]>;
    } = {};
    try {
      problem = await response.json() as typeof problem;
    } catch {
      // 非 JSON 失败仍保留稳定 HTTP fallback，不在模块内自行重试。
    }
    throw new IdentityApiError(problem.code ?? `HTTP_${response.status}`, response.status, problem.current, problem.errors);
  }
  return await response.json() as T;
}
