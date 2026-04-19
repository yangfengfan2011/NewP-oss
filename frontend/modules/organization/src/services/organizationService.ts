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

export type SummaryBlockStatus = 'ready' | 'notConfigured' | 'dependencyUnavailable';

export interface PersonSummaryBlockDto<T = unknown> {
  status: SummaryBlockStatus;
  data: T | null;
}

export interface PersonUserLinkSummaryData {
  personId: string;
  userId: string;
  linkedAt: string;
  linkedBy: string;
}

export interface PersonListItemDto {
  id: string;
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

export interface PersonListResultDto {
  items: PersonListItemDto[];
  total: number;
}

export interface PersonDetailDto {
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
  userLinkSummary: PersonSummaryBlockDto<PersonUserLinkSummaryData>;
  authorizationSummary: PersonSummaryBlockDto<string[]>;
}

export interface PersonUpsertRequest {
  personNo: string;
  name: string;
  companyId: string;
  departmentId: string;
  positionId?: string | null;
  employmentStatus: string;
  isEnabled: boolean;
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

async function requestJson<T>(path: string, init: RequestInit, errorMessage: string): Promise<T> {
  const response = await fetch(path, init);
  if (!response.ok) {
    throw new Error(errorMessage);
  }

  if (response.status === 204) {
    return undefined as T;
  }

  return response.json() as Promise<T>;
}

async function fetchJson<T>(path: string, errorMessage: string): Promise<T> {
  return requestJson<T>(path, { method: 'GET' }, errorMessage);
}

function isRecord(value: unknown): value is Record<string, unknown> {
  return typeof value === 'object' && value !== null && !Array.isArray(value);
}

function readString(record: Record<string, unknown>, keys: string[], fallback = ''): string {
  for (const key of keys) {
    const value = record[key];
    if (typeof value === 'string' && value.trim().length > 0) {
      return value;
    }
  }

  return fallback;
}

function readNullableString(record: Record<string, unknown>, keys: string[]): string | null {
  for (const key of keys) {
    const value = record[key];
    if (typeof value === 'string' && value.trim().length > 0) {
      return value;
    }
    if (value === null) {
      return null;
    }
  }

  return null;
}

function readBoolean(record: Record<string, unknown>, keys: string[], fallback = false): boolean {
  for (const key of keys) {
    const value = record[key];
    if (typeof value === 'boolean') {
      return value;
    }
  }

  return fallback;
}

function readDateString(record: Record<string, unknown>, keys: string[], fallback = ''): string {
  return readString(record, keys, fallback);
}

function normalizeSummaryBlock<T>(
  value: unknown,
  mapData?: (data: unknown) => T,
): PersonSummaryBlockDto<T> {
  // 状态化摘要的关键不是“有没有内容”，而是“依赖异常时页面还能否只读降级”。
  if (isRecord(value)) {
    const status = value['status'];
    if (status === 'ready' || status === 'notConfigured' || status === 'dependencyUnavailable') {
      const rawData = value['data'];
      return {
        status: status as SummaryBlockStatus,
        data: rawData == null ? null : (mapData ? mapData(rawData) : (rawData as T)),
      };
    }
  }

  if (Array.isArray(value)) {
    return {
      status: value.length > 0 ? 'ready' : 'notConfigured',
      data: value.length > 0 ? (mapData ? mapData(value) : (value as T)) : null,
    };
  }

  if (value == null) {
    return { status: 'notConfigured', data: null };
  }

  return {
    status: 'ready',
    data: mapData ? mapData(value) : (value as T),
  };
}

function normalizePersonListItem(value: unknown): PersonListItemDto {
  if (!isRecord(value)) {
    return {
      id: '',
      personNo: '',
      name: '',
      companyId: '',
      companyName: '',
      departmentId: '',
      departmentName: '',
      positionId: null,
      positionName: null,
      employmentStatus: '',
      employmentStatusLabel: '',
      isEnabled: false,
    };
  }

  const employmentStatus = readString(value, ['employmentStatus', 'EmploymentStatus'], 'FullTime');
  return {
    id: readString(value, ['id', 'Id']),
    personNo: readString(value, ['personNo', 'PersonNo']),
    name: readString(value, ['name', 'Name']),
    companyId: readString(value, ['companyId', 'CompanyId']),
    companyName: readString(
      value,
      ['companyName', 'CompanyName', 'companyId_DisplayValue', 'CompanyId_DisplayValue'],
      readString(value, ['companyId', 'CompanyId']),
    ),
    departmentId: readString(value, ['departmentId', 'DepartmentId']),
    departmentName: readString(
      value,
      ['departmentName', 'DepartmentName', 'departmentId_DisplayValue', 'DepartmentId_DisplayValue'],
      readString(value, ['departmentId', 'DepartmentId']),
    ),
    positionId: readNullableString(value, ['positionId', 'PositionId']),
    positionName: readNullableString(
      value,
      ['positionName', 'PositionName', 'positionId_DisplayValue', 'PositionId_DisplayValue'],
    ),
    employmentStatus,
    employmentStatusLabel: readString(
      value,
      ['employmentStatusLabel', 'EmploymentStatus_DisplayValue', 'employmentStatus_DisplayValue'],
      getEmploymentStatusLabel(employmentStatus),
    ),
    isEnabled: readBoolean(value, ['isEnabled', 'IsEnabled'], true),
  };
}

function normalizePersonDetail(value: unknown): PersonDetailDto {
  if (!isRecord(value)) {
    return {
      id: '',
      personNo: '',
      name: '',
      companyId: '',
      departmentId: '',
      positionId: null,
      employmentStatus: 'FullTime',
      employmentStatusLabel: getEmploymentStatusLabel('FullTime'),
      isEnabled: false,
      createdAt: '',
      companyName: null,
      departmentName: null,
      positionName: null,
      userLinkSummary: { status: 'notConfigured', data: null },
      authorizationSummary: { status: 'notConfigured', data: null },
    };
  }

  const employmentStatus = readString(value, ['employmentStatus', 'EmploymentStatus'], 'FullTime');
  const rawUserLink = value['userLinkSummary'] ?? value['userLink'];
  const rawAuthorizationSummary = value['authorizationSummary'];

  return {
    id: readString(value, ['id', 'Id']),
    personNo: readString(value, ['personNo', 'PersonNo']),
    name: readString(value, ['name', 'Name']),
    companyId: readString(value, ['companyId', 'CompanyId']),
    departmentId: readString(value, ['departmentId', 'DepartmentId']),
    positionId: readNullableString(value, ['positionId', 'PositionId']),
    employmentStatus,
    employmentStatusLabel: readString(
      value,
      ['employmentStatusLabel', 'EmploymentStatus_DisplayValue', 'employmentStatus_DisplayValue'],
      getEmploymentStatusLabel(employmentStatus),
    ),
    isEnabled: readBoolean(value, ['isEnabled', 'IsEnabled'], true),
    createdAt: readDateString(value, ['createdAt', 'CreatedAt']),
    companyName: readNullableString(value, ['companyName', 'CompanyName', 'companyId_DisplayValue', 'CompanyId_DisplayValue']),
    departmentName: readNullableString(
      value,
      ['departmentName', 'DepartmentName', 'departmentId_DisplayValue', 'DepartmentId_DisplayValue'],
    ),
    positionName: readNullableString(
      value,
      ['positionName', 'PositionName', 'positionId_DisplayValue', 'PositionId_DisplayValue'],
    ),
    userLinkSummary: normalizeSummaryBlock(rawUserLink, (data) => {
      const link = isRecord(data) ? data : null;
      return {
        personId: readString(link ?? {}, ['personId', 'PersonId']),
        userId: readString(link ?? {}, ['userId', 'UserId']),
        linkedAt: readString(link ?? {}, ['linkedAt', 'LinkedAt']),
        linkedBy: readString(link ?? {}, ['linkedBy', 'LinkedBy']),
      };
    }),
    authorizationSummary: normalizeSummaryBlock(rawAuthorizationSummary, (data) => {
      if (Array.isArray(data)) {
        return data.filter((item): item is string => typeof item === 'string');
      }

      if (isRecord(data) && Array.isArray(data.items)) {
        return data.items.filter((item): item is string => typeof item === 'string');
      }

      return [];
    }),
  };
}

function normalizePersonListResponse(value: unknown): PersonListResultDto {
  if (Array.isArray(value)) {
    const items = value.map(normalizePersonListItem);
    return { items, total: items.length };
  }

  if (isRecord(value)) {
    const rawItems = Array.isArray(value['items']) ? value['items'] : [];
    const items = rawItems.map(normalizePersonListItem);
    const total = typeof value['total'] === 'number' ? value['total'] : items.length;
    return { items, total };
  }

  return { items: [], total: 0 };
}

async function refreshPersonDetail(personId: string): Promise<PersonDetailDto> {
  return normalizePersonDetail(
    await fetchJson<unknown>(`/api/organization/persons/${personId}`, '加载人员详情失败'),
  );
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
}): Promise<PersonListResultDto> {
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
  return normalizePersonListResponse(await fetchJson<unknown>(path, '加载人员列表失败'));
}

export async function getPersonDetail(id: string): Promise<PersonDetailDto> {
  return normalizePersonDetail(await fetchJson<unknown>(`/api/organization/persons/${id}`, '加载人员详情失败'));
}

export async function updatePerson(personId: string, dto: PersonUpsertRequest): Promise<PersonDetailDto> {
  await requestJson<unknown>(
    `/api/organization/persons/${personId}`,
    {
      method: 'PUT',
      headers: {
        'Content-Type': 'application/json',
      },
      body: JSON.stringify(dto),
    },
    '保存人员信息失败',
  );

  return refreshPersonDetail(personId);
}

export async function disablePerson(personId: string): Promise<PersonDetailDto> {
  await requestJson<unknown>(
    `/api/organization/persons/${personId}/disable`,
    {
      method: 'POST',
    },
    '停用人员失败',
  );

  return refreshPersonDetail(personId);
}

export async function searchIdentityUsers(keyword: string): Promise<IdentityUserOptionDto[]> {
  return fetchJson<IdentityUserOptionDto[]>(
    `/api/organization/persons/user-options?keyword=${encodeURIComponent(keyword)}`,
    '加载可绑定账号失败',
  );
}

export async function linkPersonUser(personId: string, userId: string): Promise<PersonUserLinkDto> {
  const response = await fetch(`/api/organization/persons/${personId}/user-link`, {
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
