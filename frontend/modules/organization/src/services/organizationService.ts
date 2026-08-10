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

export interface PersonUserLinkDto {
  personId: string;
  userId: string;
  linkedAt: string;
  linkedBy: string;
}

export interface IdentityUserOptionDto {
  userId: string;
  displayName: string;
}

export interface PersonUserLinkSummaryData {
  personId: string;
  userId: string;
  linkedAt: string;
  linkedBy: string;
}

export interface PersonListRow {
  id: string;
  PersonNo: string;
  ENAME: string;
  EmploymentStatus: string;
  IsEnabled: boolean;
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
  createdAt: string;
  companyName?: string | null;
  departmentName?: string | null;
  positionName?: string | null;
  userLinkSummary: StandardCrudSummaryBlock<PersonUserLinkSummaryData>;
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

  return {
    id: readStandardCrudString(row, ['EID']),
    // 列表 Host 按 metadata 字段读取，模块 mapper 明确补齐协议字段，避免 Shell 引入隐式字段别名。
    PersonNo: personNo,
    ENAME: name,
    EmploymentStatus: employmentStatus,
    IsEnabled: isEnabled,
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
    createdAt: readDateString(fields, ['CreatedAt']),
    companyName: readStandardCrudNullableString(fields, ['CompanyName']),
    departmentName: readStandardCrudNullableString(fields, ['DepartmentName']),
    positionName: readStandardCrudNullableString(fields, ['PositionName']),
    userLinkSummary: normalizeStandardCrudSummaryBlock(summaries['userLink'], (data) => {
      const link = isStandardCrudRecord(data) ? data : {};
      return {
        personId: readStandardCrudString(link, ['PersonId', 'personId']),
        userId: readStandardCrudString(link, ['UserId', 'userId']),
        linkedAt: readStandardCrudString(link, ['LinkedAt', 'linkedAt']),
        linkedBy: readStandardCrudString(link, ['LinkedBy', 'linkedBy']),
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

export async function disablePerson(personId: string): Promise<PersonDetailView> {
  return mapPersonDetailView(
    await requestJson<unknown>(
      `/api/organization/persons/${personId}/disable`,
      {
        method: 'POST',
      },
      '停用人员失败',
    ),
  );
}

export async function enablePerson(personId: string): Promise<PersonDetailView> {
  return updatePerson(personId, { IsEnabled: true });
}

export async function searchIdentityUsers(keyword: string): Promise<IdentityUserOptionDto[]> {
  return fetchJson<IdentityUserOptionDto[]>(
    `/api/organization/persons/user-options?keyword=${encodeURIComponent(keyword)}`,
    '加载可绑定账号失败',
  );
}

export async function linkPersonUser(personId: string, userId: string): Promise<PersonUserLinkDto> {
  const response = await platformFetch(`/api/organization/persons/${personId}/user-link`, {
    method: 'PUT',
    headers: {
      'Content-Type': 'application/json',
    },
    body: JSON.stringify({ userId }),
  });

  if (!response.ok) {
    throw new Error('绑定账号失败');
  }

  return response.json() as Promise<PersonUserLinkDto>;
}
