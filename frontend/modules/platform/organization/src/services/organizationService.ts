import {
  isStandardCrudRecord,
  normalizeStandardCrudDetail,
  normalizeStandardCrudListResult,
  normalizeStandardCrudSummaryBlock,
  readStandardCrudBoolean,
  readStandardCrudNullableString,
  readStandardCrudString,
  readStandardCrudRequestError,
  type ActionDataAccessRequirement,
  type StandardCrudDetail,
  type StandardCrudListResult,
  type StandardCrudLookupPage,
  type StandardCrudLookupRequest,
  type StandardCrudLookupExecutionContext,
  type StandardCrudMutationValues,
  type StandardCrudOption,
  type StandardCrudSummaryBlock,
} from '@icp-platform/sdk';
import { platformFetch } from '@icp-platform/core';

interface OrganizationLookupPageResponse {
  items: StandardCrudOption[];
  currentItems: StandardCrudOption[];
  page: number;
  pageSize: number;
  hasMore: boolean;
}

export type IdentityAccountStatus = 'Enabled' | 'Disabled' | 'Deleted';

export interface PersonUserBindingAccountDto {
  accountEid: string;
  ecode: string;
  ename: string;
  status: IdentityAccountStatus;
  revision: number;
}

export interface PersonUserBindingCandidateDto extends PersonUserBindingAccountDto {
}

export interface PersonUserBindingDto {
  personEid: string;
  accountEid: string;
  boundAt: string;
  boundBy: string;
  reason: string;
  account: PersonUserBindingAccountDto | null;
}

export interface PersonUserBindingSummaryData {
  personEid: string;
  personName: string;
  isPersonEnabled: boolean;
  accountEid: string | null;
  boundAt: string | null;
  boundBy: string | null;
  reason: string | null;
  account: PersonUserBindingAccountDto | null;
  isAccountAvailable: boolean;
  isAccountReadable: boolean;
}

export interface PersonListRow {
  id: string;
  PersonNo: string;
  ENAME: string;
  BirthDate: string | null;
  BirthDate_DisplayValue: string | null;
  EmploymentStatus: string;
  EmploymentStartedAt: string | null;
  EmploymentStartedAt_DisplayValue: string | null;
  EmploymentEndedAt: string | null;
  EmploymentEndedAt_DisplayValue: string | null;
  IsEnabled: boolean;
  AccountEID?: string | null;
  CompanyId: string | null;
  CompanyId_DisplayValue: string | null;
  DepartmentName: string;
  PositionName?: string | null;
  personNo: string;
  name: string;
  companyId: string;
  departmentId: string;
  departmentName: string;
  positionId?: string | null;
  positionName?: string | null;
  employmentStatus: string;
  employmentStatusLabel: string;
  isEnabled: boolean;
  accountEid?: string | null;
}

export interface PersonDetailView {
  EID: string;
  PersonNo: string;
  ENAME: string;
  BirthDate: string | null;
  BirthDate_DisplayValue: string | null;
  EmploymentStartedAt: string | null;
  EmploymentStartedAt_DisplayValue: string | null;
  EmploymentEndedAt: string | null;
  EmploymentEndedAt_DisplayValue: string | null;
  id: string;
  personNo: string;
  name: string;
  companyId: string;
  departmentId: string;
  positionId?: string | null;
  employmentStatus: string;
  employmentStatusLabel: string;
  isEnabled: boolean;
  createdBy: string;
  createdAt: string;
  updatedBy: string;
  updatedAt: string;
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

async function requestJson<T>(path: string, init: RequestInit, errorMessage: string): Promise<T> {
  const response = await platformFetch(path, init);
  if (!response.ok) {
    throw await readStandardCrudRequestError(response, errorMessage);
  }

  if (response.status === 204) {
    return undefined as T;
  }

  return response.json() as Promise<T>;
}

async function fetchJson<T>(path: string, errorMessage: string, signal?: AbortSignal, featurePosition?: string,
  dataAccess?: StandardCrudLookupExecutionContext['dataAccess']): Promise<T> {
  return requestJson<T>(path, { method: 'GET', signal, ...(featurePosition || dataAccess ? { headers: {
    ...(featurePosition ? { 'X-NewP-Feature': featurePosition } : {}),
    ...(dataAccess ? { 'X-NewP-Data-Declaration': dataAccess.id } : {}),
  } } : {}) }, errorMessage);
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
  const rawCompanyId = readStandardCrudNullableString(row, ['CompanyId']);
  const departmentName = readStandardCrudString(row, ['DepartmentName']);
  const positionName = readStandardCrudNullableString(row, ['PositionName']);
  return {
    id: readStandardCrudString(row, ['EID']),
    // 列表 Host 按 metadata 字段读取，模块 mapper 明确补齐协议字段，避免 Shell 引入隐式字段别名。
    PersonNo: personNo,
    ENAME: name,
    BirthDate: readStandardCrudNullableString(row, ['BirthDate']),
    BirthDate_DisplayValue: readStandardCrudNullableString(row, ['BirthDate_DisplayValue']),
    EmploymentStatus: employmentStatus,
    EmploymentStartedAt: readStandardCrudNullableString(row, ['EmploymentStartedAt']),
    EmploymentStartedAt_DisplayValue: readStandardCrudNullableString(row, ['EmploymentStartedAt_DisplayValue']),
    EmploymentEndedAt: readStandardCrudNullableString(row, ['EmploymentEndedAt']),
    EmploymentEndedAt_DisplayValue: readStandardCrudNullableString(row, ['EmploymentEndedAt_DisplayValue']),
    IsEnabled: isEnabled,
    AccountEID: readStandardCrudNullableString(row, ['AccountEID']),
    CompanyId: rawCompanyId,
    CompanyId_DisplayValue: readStandardCrudNullableString(row, ['CompanyId_DisplayValue']),
    DepartmentName: departmentName,
    PositionName: positionName,
    personNo,
    name,
    companyId: rawCompanyId ?? '',
    departmentId: readStandardCrudString(row, ['DepartmentId']),
    departmentName,
    positionId: readStandardCrudNullableString(row, ['PositionId']),
    positionName,
    employmentStatus,
    employmentStatusLabel: getEmploymentStatusLabel(employmentStatus),
    isEnabled,
    accountEid: readStandardCrudNullableString(row, ['AccountEID']),
  };
}

export function mapPersonDetailView(value: StandardCrudDetail | unknown): PersonDetailView {
  const detail = normalizeStandardCrudDetail(value);
  const fields = detail.fields;
  const summaries = detail.summaries;
  const employmentStatus = readStandardCrudString(fields, ['EmploymentStatus']);

  return {
    EID: readStandardCrudString(fields, ['EID']),
    PersonNo: readStandardCrudString(fields, ['PersonNo']),
    ENAME: readStandardCrudString(fields, ['ENAME']),
    BirthDate: readStandardCrudNullableString(fields, ['BirthDate']),
    BirthDate_DisplayValue: readStandardCrudNullableString(fields, ['BirthDate_DisplayValue']),
    EmploymentStartedAt: readStandardCrudNullableString(fields, ['EmploymentStartedAt']),
    EmploymentStartedAt_DisplayValue: readStandardCrudNullableString(fields, ['EmploymentStartedAt_DisplayValue']),
    EmploymentEndedAt: readStandardCrudNullableString(fields, ['EmploymentEndedAt']),
    EmploymentEndedAt_DisplayValue: readStandardCrudNullableString(fields, ['EmploymentEndedAt_DisplayValue']),
    id: readStandardCrudString(fields, ['EID']),
    personNo: readStandardCrudString(fields, ['PersonNo']),
    name: readStandardCrudString(fields, ['ENAME']),
    companyId: readStandardCrudString(fields, ['CompanyId']),
    departmentId: readStandardCrudString(fields, ['DepartmentId']),
    positionId: readStandardCrudNullableString(fields, ['PositionId']),
    employmentStatus,
    employmentStatusLabel: getEmploymentStatusLabel(employmentStatus),
    isEnabled: readStandardCrudBoolean(fields, ['IsEnabled'], true),
    createdBy: readStandardCrudString(fields, ['CreatedBy']),
    createdAt: readDateString(fields, ['CreatedAt']),
    updatedBy: readStandardCrudString(fields, ['UpdatedBy']),
    updatedAt: readDateString(fields, ['UpdatedAt']),
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
        accountEid: readStandardCrudNullableString(binding, ['AccountEid', 'accountEid']),
        boundAt: readStandardCrudNullableString(binding, ['BoundAt', 'boundAt']),
        boundBy: readStandardCrudNullableString(binding, ['BoundBy', 'boundBy']),
        reason: readStandardCrudNullableString(binding, ['Reason', 'reason']),
        account: rawAccount ? {
          accountEid: readStandardCrudString(rawAccount, ['AccountEid', 'accountEid']),
          ecode: readStandardCrudString(rawAccount, ['ECODE', 'ecode']),
          ename: readStandardCrudString(rawAccount, ['ENAME', 'ename']),
          status: readStandardCrudString(rawAccount, ['Status', 'status']) as IdentityAccountStatus,
          revision: Number(rawAccount.Revision ?? rawAccount.revision ?? 0),
        } : null,
        isAccountAvailable: readStandardCrudBoolean(binding, ['IsAccountAvailable', 'isAccountAvailable'], false),
        isAccountReadable: readStandardCrudBoolean(binding, ['IsAccountReadable', 'isAccountReadable'], true),
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

export async function getCompanyLookup(
  request: StandardCrudLookupRequest,
  includeDisabled: boolean,
  signal?: AbortSignal,
  featurePosition?: string,
  dataAccess?: StandardCrudLookupExecutionContext['dataAccess'],
): Promise<StandardCrudLookupPage> {
  const search = buildOrganizationLookupSearch(request, includeDisabled);
  return mapOrganizationLookupPage(await fetchJson<OrganizationLookupPageResponse>(
    `/api/organization/person-lookups/companies?${search.toString()}`,
    '加载公司列表失败',
    signal,
    featurePosition,
    dataAccess,
  ));
}

export async function getDepartmentCompanyLookup(
  request: StandardCrudLookupRequest,
  includeDisabled: boolean,
  signal?: AbortSignal,
  featurePosition?: string,
  dataAccess?: StandardCrudLookupExecutionContext['dataAccess'],
): Promise<StandardCrudLookupPage> {
  const search = buildOrganizationLookupSearch(request, includeDisabled);
  return mapOrganizationLookupPage(await fetchJson<OrganizationLookupPageResponse>(
    `/api/organization/department-lookups/companies?${search.toString()}`,
    '加载公司列表失败',
    signal,
    featurePosition,
    dataAccess,
  ));
}

export async function getDepartmentLookup(
  companyId: string,
  request: StandardCrudLookupRequest,
  includeDisabled: boolean,
  signal?: AbortSignal,
  featurePosition?: string,
  dataAccess?: StandardCrudLookupExecutionContext['dataAccess'],
): Promise<StandardCrudLookupPage> {
  const search = buildOrganizationLookupSearch(request, includeDisabled);
  search.set('companyId', companyId);
  return mapOrganizationLookupPage(await fetchJson<OrganizationLookupPageResponse>(
    `/api/organization/person-lookups/departments?${search.toString()}`,
    '加载部门列表失败',
    signal,
    featurePosition,
    dataAccess,
  ));
}

export async function getDepartmentParentLookup(
  companyId: string,
  currentDepartmentCode: string | undefined,
  request: StandardCrudLookupRequest,
  includeDisabled: boolean,
  signal?: AbortSignal,
  featurePosition?: string,
  dataAccess?: StandardCrudLookupExecutionContext['dataAccess'],
): Promise<StandardCrudLookupPage> {
  const search = buildOrganizationLookupSearch(request, includeDisabled);
  search.set('companyId', companyId);
  if (currentDepartmentCode) search.set('currentDepartmentCode', currentDepartmentCode);
  return mapOrganizationLookupPage(await fetchJson<OrganizationLookupPageResponse>(
    `/api/organization/department-lookups/parents?${search.toString()}`,
    '加载上级部门列表失败',
    signal,
    featurePosition,
    dataAccess,
  ));
}

// HTTP 响应使用服务端 items 命名，模块 provider 在边界转换为 SDK 的 options 命名。
function mapOrganizationLookupPage(response: OrganizationLookupPageResponse): StandardCrudLookupPage {
  return {
    options: response.items,
    currentOptions: response.currentItems,
    page: response.page,
    pageSize: response.pageSize,
    hasMore: response.hasMore,
  };
}

// 模块专用 Lookup 复用共享运行时的分页字段，但额外参数只停留在 Organization HTTP seam。
function buildOrganizationLookupSearch(
  request: StandardCrudLookupRequest,
  includeDisabled: boolean,
): URLSearchParams {
  const search = new URLSearchParams();
  if (request.keyword.length > 0) search.set('keyword', request.keyword);
  search.set('page', String(request.page));
  search.set('pageSize', String(request.pageSize));
  if (request.currentValueIds.length > 0) {
    search.set('currentValueIds', request.currentValueIds.join(','));
  }
  if (includeDisabled) search.set('includeDisabled', 'true');
  return search;
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

export async function deletePerson(personId: string, featurePosition?: string, dataAccess?: ActionDataAccessRequirement): Promise<void> {
  await requestJson<void>(
    `/api/organization/persons/${personId}`,
    { method: 'DELETE', ...(featurePosition || dataAccess ? { headers: {
      ...(featurePosition ? { 'X-NewP-Feature': featurePosition } : {}),
      ...(dataAccess ? { 'X-NewP-Data-Declaration': dataAccess.id } : {}),
    } } : {}) },
    '删除人员失败',
  );
}

export async function setPersonLifecycleStatus(
  personId: string,
  input: { isEnabled: boolean; reason: string },
  featurePosition?: string,
): Promise<void> {
  await requestJson<unknown>(
    `/api/organization/persons/${encodeURIComponent(personId)}/status/${input.isEnabled ? "enable" : "disable"}`,
    {
      method: 'PUT',
      headers: { 'Content-Type': 'application/json', Accept: 'application/json', ...(featurePosition ? { 'X-NewP-Feature': featurePosition } : {}) },
      body: JSON.stringify(input),
    },
    input.isEnabled ? '恢复人员失败' : '停用人员失败',
  );
}

export async function searchPersonUserBindingCandidates(
  personEid: string,
  query: string,
  featurePosition?: string,
): Promise<PersonUserBindingCandidateDto[]> {
  return fetchJson<PersonUserBindingCandidateDto[]>(
    `/api/organization/persons/${encodeURIComponent(personEid)}/user-binding/candidates?query=${encodeURIComponent(query)}`,
    '加载可绑定账号失败',
    undefined,
    featurePosition,
  );
}

export async function bindPersonUser(
  personEid: string,
  input: { accountEid: string; expectedRevision: number; reason: string },
  featurePosition?: string,
): Promise<PersonUserBindingDto> {
  return requestJson<PersonUserBindingDto>(
    `/api/organization/persons/${encodeURIComponent(personEid)}/user-binding`,
    {
      method: 'PUT',
      headers: { 'Content-Type': 'application/json', Accept: 'application/json', ...(featurePosition ? { 'X-NewP-Feature': featurePosition } : {}) },
      body: JSON.stringify(input),
    },
    '绑定账号失败',
  );
}

/** 初始化只由用户显式发起；身份、名称和保留部门标识均由服务端决定。 */
export async function initializeOrganization(featurePosition?: string): Promise<void> {
  await requestJson<unknown>('/api/organization/initialization', {
    method: 'POST', headers: { Accept: 'application/json', ...(featurePosition ? { 'X-NewP-Feature': featurePosition } : {}) },
  }, '初始化组织失败');
}

/** 本地管理员维护资格与可分配的普通功能资格分别核验。 */
export async function getOrganizationInitializationEligibility(featurePosition: string, signal: AbortSignal): Promise<boolean> {
  const result = await fetchJson<{ allowed: boolean }>('/api/organization/initialization/eligibility',
    '读取初始化资格失败', signal, featurePosition);
  return result.allowed;
}
