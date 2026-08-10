import {
  isStandardCrudRecord,
  normalizeStandardCrudDetail,
  normalizeStandardCrudListResult,
  normalizeStandardCrudSummaryBlock,
  readStandardCrudBoolean,
  readStandardCrudNullableString,
  readStandardCrudString,
  type StandardCrudDetail,
  type StandardCrudListResult,
  type StandardCrudMutationValues,
  type StandardCrudSummaryBlock,
} from '@icp-platform/sdk';
import { platformFetch } from '@icp-platform/core';

export interface CompanyDto {
  id: string;
  code: string;
  name: string;
  isEnabled: boolean;
  sortOrder: number;
  createdAt: string;
}

export interface DepartmentDto {
  id: string;
  companyId: string;
  parentDepartmentId?: string | null;
  code: string;
  name: string;
  isEnabled: boolean;
  sortOrder: number;
  createdAt: string;
}

export interface PositionDto {
  id: string;
  companyId: string;
  departmentId: string;
  code: string;
  name: string;
  employmentStatus: string;
  isEnabled: boolean;
  sortOrder: number;
  createdAt: string;
}

export type IdentityAccountStatus = 'Enabled' | 'Disabled' | 'Deleted';

export interface PersonUserBindingAccountDto {
  userEid: string;
  ecode: string;
  ename: string;
  status: IdentityAccountStatus;
  revision: number;
}

export interface PersonUserBindingCandidateDto extends PersonUserBindingAccountDto {
}

export interface PersonUserBindingDto {
  personEid: string;
  userEid: string;
  boundAt: string;
  boundBy: string;
  reason: string;
  account: PersonUserBindingAccountDto | null;
}

export interface PersonUserBindingSummaryData {
  personEid: string;
  personName: string;
  isPersonEnabled: boolean;
  userEid: string | null;
  boundAt: string | null;
  boundBy: string | null;
  reason: string | null;
  account: PersonUserBindingAccountDto | null;
  isAccountAvailable: boolean;
}

export interface PersonListRow {
  id: string;
  PersonNo: string;
  ENAME: string;
  EmploymentStatus: string;
  IsEnabled: boolean;
  Revision: number;
  CompanyName: string;
  DepartmentName: string;
  PositionName?: string | null;
  personNo: string;
  name: string;
  companyId: string;
  companyName: string;
  departmentId: string;
  departmentName: string;
  positionId?: string | null;
  positionName?: string | null;
  employmentStatus: string;
  employmentStatusLabel: string;
  isEnabled: boolean;
  revision: number;
}

export interface PersonDetailView {
  id: string;
  personNo: string;
  name: string;
  companyId: string;
  departmentId: string;
  positionId?: string | null;
  employmentStatus: string;
  employmentStatusLabel: string;
  isEnabled: boolean;
  revision: number;
  createdAt: string;
  companyName?: string | null;
  departmentName?: string | null;
  positionName?: string | null;
  userBindingSummary: StandardCrudSummaryBlock<PersonUserBindingSummaryData>;
  authorizationSummary: StandardCrudSummaryBlock<string[]>;
}

export const employmentStatusOptions = [
  { value: 'FullTime', label: '正式' },
  { value: 'PartTime', label: '兼职' },
  { value: 'Contractor', label: '外包' },
  { value: 'Intern', label: '实习' },
];

export function getEmploymentStatusLabel(status: string): string {
  return employmentStatusOptions.find((option) => option.value === status)?.label ?? status;
}

function readErrorMessage(value: unknown): string | undefined {
  if (typeof value === 'string' && value.trim().length > 0) {
    return value;
  }
  if (!value || typeof value !== 'object' || Array.isArray(value)) {
    return undefined;
  }

  const record = value as Record<string, unknown>;
  if (typeof record.message === 'string' && record.message.trim().length > 0) {
    return record.message;
  }

  const nestedError = record.error;
  if (nestedError && typeof nestedError === 'object' && !Array.isArray(nestedError)) {
    const nestedMessage = (nestedError as Record<string, unknown>).message;
    if (typeof nestedMessage === 'string' && nestedMessage.trim().length > 0) {
      return nestedMessage;
    }
  }

  return undefined;
}

// 非 2xx 响应只在这里解释错误体，调用方仍负责提供稳定兜底文案，避免格式漂移时暴露空提示。
async function readResponseErrorMessage(response: Response, fallback: string): Promise<string> {
  const contentType = response.headers.get('content-type') ?? '';
  try {
    if (contentType.toLowerCase().includes('application/json')) {
      return readErrorMessage(await response.json()) ?? fallback;
    }

    const text = await response.text();
    return readErrorMessage(text) ?? fallback;
  } catch {
    return fallback;
  }
}

async function requestJson<T>(path: string, init: RequestInit, errorMessage: string): Promise<T> {
  const response = await platformFetch(path, init);
  if (!response.ok) {
    throw new Error(await readResponseErrorMessage(response, errorMessage));
  }

  if (response.status === 204) {
    return undefined as T;
  }

  return response.json() as Promise<T>;
}

async function fetchJson<T>(path: string, errorMessage: string): Promise<T> {
  return requestJson<T>(path, { method: 'GET' }, errorMessage);
}

function readDateString(record: Record<string, unknown>, keys: string[], fallback = ''): string {
  return readStandardCrudString(record, keys, fallback);
}

export function mapPersonListItem(value: unknown): PersonListRow {
  const row = isStandardCrudRecord(value) ? value : {};
  const employmentStatus = readStandardCrudString(row, ['EmploymentStatus']);
  const isEnabled = readStandardCrudBoolean(row, ['IsEnabled'], true);
  const personNo = readStandardCrudString(row, ['PersonNo']);
  const name = readStandardCrudString(row, ['ENAME']);
  const companyName = readStandardCrudString(row, ['CompanyName']);
  const departmentName = readStandardCrudString(row, ['DepartmentName']);
  const positionName = readStandardCrudNullableString(row, ['PositionName']);
  const revision = Number(row.Revision ?? row.revision ?? 0);

  return {
    id: readStandardCrudString(row, ['EID']),
    // 列表 Host 按 metadata 字段读取，模块 mapper 明确补齐协议字段，避免 Shell 引入隐式字段别名。
    PersonNo: personNo,
    ENAME: name,
    EmploymentStatus: employmentStatus,
    IsEnabled: isEnabled,
    Revision: revision,
    CompanyName: companyName,
    DepartmentName: departmentName,
    PositionName: positionName,
    personNo,
    name,
    companyId: readStandardCrudString(row, ['CompanyId']),
    companyName,
    departmentId: readStandardCrudString(row, ['DepartmentId']),
    departmentName,
    positionId: readStandardCrudNullableString(row, ['PositionId']),
    positionName,
    employmentStatus,
    employmentStatusLabel: getEmploymentStatusLabel(employmentStatus),
    isEnabled,
    revision,
  };
}

export function mapPersonDetailView(value: StandardCrudDetail | unknown): PersonDetailView {
  const detail = normalizeStandardCrudDetail(value);
  const fields = detail.fields;
  const summaries = detail.summaries;
  const employmentStatus = readStandardCrudString(fields, ['EmploymentStatus']);

  return {
    id: readStandardCrudString(fields, ['EID']),
    personNo: readStandardCrudString(fields, ['PersonNo']),
    name: readStandardCrudString(fields, ['ENAME']),
    companyId: readStandardCrudString(fields, ['CompanyId']),
    departmentId: readStandardCrudString(fields, ['DepartmentId']),
    positionId: readStandardCrudNullableString(fields, ['PositionId']),
    employmentStatus,
    employmentStatusLabel: getEmploymentStatusLabel(employmentStatus),
    isEnabled: readStandardCrudBoolean(fields, ['IsEnabled'], true),
    revision: Number(fields.Revision ?? fields.revision ?? 0),
    createdAt: readDateString(fields, ['CreatedAt']),
    companyName: readStandardCrudNullableString(fields, ['CompanyName']),
    departmentName: readStandardCrudNullableString(fields, ['DepartmentName']),
    positionName: readStandardCrudNullableString(fields, ['PositionName']),
    userBindingSummary: normalizeStandardCrudSummaryBlock(summaries['userBinding'], (data) => {
      const binding = isStandardCrudRecord(data) ? data : {};
      const rawAccount = isStandardCrudRecord(binding.Account ?? binding.account)
        ? (binding.Account ?? binding.account) as Record<string, unknown>
        : null;
      return {
        personEid: readStandardCrudString(binding, ['PersonEid', 'personEid']),
        personName: readStandardCrudString(binding, ['PersonName', 'personName']),
        isPersonEnabled: readStandardCrudBoolean(binding, ['IsPersonEnabled', 'isPersonEnabled'], false),
        userEid: readStandardCrudNullableString(binding, ['UserEid', 'userEid']),
        boundAt: readStandardCrudNullableString(binding, ['BoundAt', 'boundAt']),
        boundBy: readStandardCrudNullableString(binding, ['BoundBy', 'boundBy']),
        reason: readStandardCrudNullableString(binding, ['Reason', 'reason']),
        account: rawAccount ? {
          userEid: readStandardCrudString(rawAccount, ['UserEid', 'userEid']),
          ecode: readStandardCrudString(rawAccount, ['ECODE', 'ecode']),
          ename: readStandardCrudString(rawAccount, ['ENAME', 'ename']),
          status: readStandardCrudString(rawAccount, ['Status', 'status']) as IdentityAccountStatus,
          revision: Number(rawAccount.Revision ?? rawAccount.revision ?? 0),
        } : null,
        isAccountAvailable: readStandardCrudBoolean(binding, ['IsAccountAvailable', 'isAccountAvailable'], false),
      };
    }),
    authorizationSummary: normalizeStandardCrudSummaryBlock(summaries['authorizationSummary'], (data) => {
      if (Array.isArray(data)) {
        return data.filter((item): item is string => typeof item === 'string');
      }

      return [];
    }),
  };
}

export async function getCompanies(): Promise<CompanyDto[]> {
  return fetchJson<CompanyDto[]>('/api/organization/companies', '加载公司列表失败');
}

export async function getDepartments(companyId: string): Promise<DepartmentDto[]> {
  return fetchJson<DepartmentDto[]>(
    `/api/organization/departments?companyId=${encodeURIComponent(companyId)}`,
    '加载部门列表失败',
  );
}

export async function getPositions(departmentId: string): Promise<PositionDto[]> {
  return fetchJson<PositionDto[]>(
    `/api/organization/positions?departmentId=${encodeURIComponent(departmentId)}`,
    '加载岗位列表失败',
  );
}

export async function getPersons(query: {
  keyword?: string;
  companyId?: string;
  departmentId?: string;
  employmentStatus?: string;
}): Promise<StandardCrudListResult<PersonListRow>> {
  const search = new URLSearchParams();

  if (query.keyword) {
    search.set('keyword', query.keyword);
  }

  if (query.companyId) {
    search.set('companyId', query.companyId);
  }

  if (query.departmentId) {
    search.set('departmentId', query.departmentId);
  }

  if (query.employmentStatus) {
    search.set('employmentStatus', query.employmentStatus);
  }

  const suffix = search.toString();
  const path = suffix.length > 0 ? `/api/organization/persons?${suffix}` : '/api/organization/persons';
  return normalizeStandardCrudListResult(await fetchJson<unknown>(path, '加载人员列表失败'), mapPersonListItem);
}

export async function getPersonDetail(id: string): Promise<PersonDetailView> {
  return mapPersonDetailView(await fetchJson<unknown>(`/api/organization/persons/${id}`, '加载人员详情失败'));
}

export async function createPerson(values: StandardCrudMutationValues): Promise<PersonDetailView> {
  return mapPersonDetailView(
    await requestJson<unknown>(
      '/api/organization/persons',
      {
        method: 'POST',
        headers: {
          'Content-Type': 'application/json',
        },
        body: JSON.stringify(values),
      },
      '新建人员失败',
    ),
  );
}

export async function updatePerson(personId: string, values: StandardCrudMutationValues): Promise<PersonDetailView> {
  return mapPersonDetailView(
    await requestJson<unknown>(
      `/api/organization/persons/${personId}`,
      {
        method: 'PUT',
        headers: {
          'Content-Type': 'application/json',
        },
        body: JSON.stringify(values),
      },
      '保存人员信息失败',
    ),
  );
}

export async function deletePerson(personId: string): Promise<void> {
  await requestJson<void>(
    `/api/organization/persons/${personId}`,
    { method: 'DELETE' },
    '删除人员失败',
  );
}

export async function setPersonLifecycleStatus(
  personId: string,
  input: { isEnabled: boolean; expectedRevision: number; reason: string },
): Promise<void> {
  await requestJson<unknown>(
    `/api/organization/persons/${encodeURIComponent(personId)}/status`,
    {
      method: 'PUT',
      headers: { 'Content-Type': 'application/json', Accept: 'application/json' },
      body: JSON.stringify(input),
    },
    input.isEnabled ? '恢复人员失败' : '停用人员失败',
  );
}

export async function searchPersonUserBindingCandidates(
  personEid: string,
  query: string,
): Promise<PersonUserBindingCandidateDto[]> {
  return fetchJson<PersonUserBindingCandidateDto[]>(
    `/api/organization/persons/${encodeURIComponent(personEid)}/user-binding/candidates?query=${encodeURIComponent(query)}`,
    '加载可绑定账号失败',
  );
}

export async function bindPersonUser(
  personEid: string,
  input: { userEid: string; expectedRevision: number; reason: string },
): Promise<PersonUserBindingDto> {
  return requestJson<PersonUserBindingDto>(
    `/api/organization/persons/${encodeURIComponent(personEid)}/user-binding`,
    {
      method: 'PUT',
      headers: { 'Content-Type': 'application/json', Accept: 'application/json' },
      body: JSON.stringify(input),
    },
    '绑定账号失败',
  );
}
