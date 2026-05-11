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

export interface StandardCrudSummaryBlock<T = unknown> {
  status: SummaryBlockStatus;
  data: T | null;
}

export interface PersonUserLinkSummaryData {
  personId: string;
  userId: string;
  linkedAt: string;
  linkedBy: string;
}

export interface StandardCrudListResult<TItem> {
  items: TItem[];
  total: number;
  page: number;
  pageSize: number;
  sortBy?: string | null;
  sortDirection?: string | null;
}

export interface StandardCrudDetail {
  fields: Record<string, unknown>;
  summaries: Record<string, StandardCrudSummaryBlock<unknown> | undefined>;
}

export interface PersonListRow {
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

export interface PersonUpsertRequest {
  personNo: string;
  name: string;
  companyId: string | null;
  departmentId: string | null;
  positionId?: string | null;
  employmentStatus?: string | null;
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
): StandardCrudSummaryBlock<T> {
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

function normalizePersonListItem(value: unknown): PersonListRow {
  const row = isRecord(value) ? value : {};
  const employmentStatus = readString(row, ['EmploymentStatus'], 'FullTime');

  return {
    id: readString(row, ['EID']),
    personNo: readString(row, ['PersonNo']),
    name: readString(row, ['ENAME']),
    companyId: readString(row, ['CompanyId']),
    companyName: readString(row, ['CompanyName'], readString(row, ['CompanyId'])),
    departmentId: readString(row, ['DepartmentId']),
    departmentName: readString(row, ['DepartmentName'], readString(row, ['DepartmentId'])),
    positionId: readNullableString(row, ['PositionId']),
    positionName: readNullableString(row, ['PositionName']),
    employmentStatus,
    employmentStatusLabel: getEmploymentStatusLabel(employmentStatus),
    isEnabled: readBoolean(row, ['IsEnabled'], true),
  };
}

function normalizePersonDetail(value: unknown): PersonDetailView {
  const detail = isRecord(value) ? value : {};
  const fields = isRecord(detail['fields']) ? detail['fields'] : {};
  const summaries = isRecord(detail['summaries']) ? detail['summaries'] : {};
  const employmentStatus = readString(fields, ['EmploymentStatus'], 'FullTime');

  return {
    id: readString(fields, ['EID']),
    personNo: readString(fields, ['PersonNo']),
    name: readString(fields, ['ENAME']),
    companyId: readString(fields, ['CompanyId']),
    departmentId: readString(fields, ['DepartmentId']),
    positionId: readNullableString(fields, ['PositionId']),
    employmentStatus,
    employmentStatusLabel: getEmploymentStatusLabel(employmentStatus),
    isEnabled: readBoolean(fields, ['IsEnabled'], true),
    createdAt: readDateString(fields, ['CreatedAt']),
    companyName: readNullableString(fields, ['CompanyName']),
    departmentName: readNullableString(fields, ['DepartmentName']),
    positionName: readNullableString(fields, ['PositionName']),
    userLinkSummary: normalizeSummaryBlock(summaries['userLink'], (data) => {
      const link = isRecord(data) ? data : {};
      return {
        personId: readString(link, ['PersonId', 'personId']),
        userId: readString(link, ['UserId', 'userId']),
        linkedAt: readString(link, ['LinkedAt', 'linkedAt']),
        linkedBy: readString(link, ['LinkedBy', 'linkedBy']),
      };
    }),
    authorizationSummary: normalizeSummaryBlock(summaries['authorizationSummary'], (data) => {
      if (Array.isArray(data)) {
        return data.filter((item): item is string => typeof item === 'string');
      }

      return [];
    }),
  };
}

function normalizePersonListResponse(value: unknown): StandardCrudListResult<PersonListRow> {
  const result = isRecord(value) ? value : {};
  const rawItems = Array.isArray(result['items']) ? result['items'] : [];
  const items = rawItems.map(normalizePersonListItem);

  return {
    items,
    total: typeof result['total'] === 'number' ? result['total'] : items.length,
    page: typeof result['page'] === 'number' ? result['page'] : 1,
    pageSize: typeof result['pageSize'] === 'number' ? result['pageSize'] : items.length,
    sortBy: typeof result['sortBy'] === 'string' ? result['sortBy'] : null,
    sortDirection: typeof result['sortDirection'] === 'string' ? result['sortDirection'] : null,
  };
}

function toStandardPersonValues(dto: PersonUpsertRequest): Record<string, unknown> {
  return {
    PersonNo: dto.personNo,
    ENAME: dto.name,
    CompanyId: dto.companyId && dto.companyId.trim().length > 0 ? dto.companyId : null,
    DepartmentId: dto.departmentId && dto.departmentId.trim().length > 0 ? dto.departmentId : null,
    PositionId: dto.positionId && dto.positionId.trim().length > 0 ? dto.positionId : null,
    EmploymentStatus: dto.employmentStatus && dto.employmentStatus.trim().length > 0 ? dto.employmentStatus : null,
    IsEnabled: dto.isEnabled,
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
  return normalizePersonListResponse(await fetchJson<unknown>(path, '加载人员列表失败'));
}

export async function getPersonDetail(id: string): Promise<PersonDetailView> {
  return normalizePersonDetail(await fetchJson<unknown>(`/api/organization/persons/${id}`, '加载人员详情失败'));
}

export async function createPerson(dto: PersonUpsertRequest): Promise<PersonDetailView> {
  return normalizePersonDetail(
    await requestJson<unknown>(
      '/api/organization/persons',
      {
        method: 'POST',
        headers: {
          'Content-Type': 'application/json',
        },
        body: JSON.stringify(toStandardPersonValues(dto)),
      },
      '新建人员失败',
    ),
  );
}

export async function updatePerson(personId: string, dto: PersonUpsertRequest): Promise<PersonDetailView> {
  return normalizePersonDetail(
    await requestJson<unknown>(
      `/api/organization/persons/${personId}`,
      {
        method: 'PUT',
        headers: {
          'Content-Type': 'application/json',
        },
        body: JSON.stringify(toStandardPersonValues(dto)),
      },
      '保存人员信息失败',
    ),
  );
}

export async function disablePerson(personId: string): Promise<PersonDetailView> {
  return normalizePersonDetail(
    await requestJson<unknown>(
      `/api/organization/persons/${personId}/disable`,
      {
        method: 'POST',
      },
      '停用人员失败',
    ),
  );
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
