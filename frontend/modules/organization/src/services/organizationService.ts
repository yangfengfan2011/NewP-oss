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

export interface PersonDetailDto {
  id: string;
  personNo: string;
  name: string;
  companyId: string;
  departmentId: string;
  positionId?: string | null;
  employmentStatus: string;
  isEnabled: boolean;
  createdAt: string;
  userLink?: PersonUserLinkDto | null;
  authorizationSummary: string[];
}

async function fetchJson<T>(path: string, errorMessage: string): Promise<T> {
  const response = await fetch(path);
  if (!response.ok) {
    throw new Error(errorMessage);
  }

  return response.json() as Promise<T>;
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

export async function getPersonDetail(id: string): Promise<PersonDetailDto> {
  return fetchJson<PersonDetailDto>(`/api/organization/persons/${id}`, '加载人员详情失败');
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
