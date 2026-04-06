export interface ModuleStatus {
  moduleId: string;
  displayName: string;
  message: string;
}

export async function fetchModuleStatus(): Promise<ModuleStatus> {
  const response = await fetch('/api/module-template-sample/status');
  if (!response.ok) {
    throw new Error(`Failed to fetch status: ${response.statusText}`);
  }
  return response.json();
}
