export interface TemplateStatus {
  moduleId: string;
  message: string;
}

export async function fetchTemplateStatus(): Promise<TemplateStatus> {
  const response = await fetch('/api/template-module/status');
  if (!response.ok) {
    throw new Error(`Failed to fetch status: ${response.statusText}`);
  }
  return response.json();
}
