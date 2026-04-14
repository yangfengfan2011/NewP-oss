import type {
  PluginModuleStatus,
  PluginOperationResult,
  PluginUploadVerifyResult,
} from '../types/pluginOperations';

type Fetcher = typeof fetch;

function buildRequest(init?: RequestInit): RequestInit {
  return {
    // 插件运维接口默认依赖宿主登录态，不允许因为某个调用方忘记带 cookie
    // 就让前后端在“未登录”与“没有权限”之间出现歧义。
    credentials: 'include',
    ...init,
  };
}

async function readJson<T>(response: Response): Promise<T> {
  return await response.json() as T;
}

async function ensureOk(response: Response): Promise<Response> {
  if (response.ok) {
    return response;
  }

  let message = `Request failed with status ${response.status}`;
  const contentType = response.headers.get('Content-Type') ?? '';

  // 这里统一把后端 problem/json、普通 JSON 和纯文本错误折叠成一个 Error.message，
  // 让页面层只处理“用户可见的失败文案”，不用每个动作重复拆响应格式。
  if (contentType.includes('application/json')) {
    const payload = await response.json() as {
      message?: string;
      errors?: string[];
    };
    if (payload.message && payload.message.trim().length > 0) {
      message = payload.message;
    } else if (payload.errors && payload.errors.length > 0) {
      message = payload.errors.join('；');
    }
  } else {
    const text = await response.text();
    if (text.trim().length > 0) {
      message = text;
    }
  }

  throw new Error(message);
}

async function readActionResult(request: Promise<Response>): Promise<PluginOperationResult> {
  const response = await request;
  const contentType = response.headers.get('Content-Type') ?? '';
  if (!contentType.includes('application/json')) {
    // 生命周期动作理论上应该返回统一 JSON 结果；
    // 如果后端退化成非 JSON，先走 ensureOk 抛出清晰错误，避免前端把异常响应当成功解析。
    await ensureOk(response);
  }

  return await readJson<PluginOperationResult>(response);
}

export async function getModules(fetcher: Fetcher = fetch): Promise<PluginModuleStatus[]> {
  const response = await fetcher('/api/plugin-ops/modules', buildRequest());
  await ensureOk(response);
  return await readJson<PluginModuleStatus[]>(response);
}

export async function verifyUpload(
  file: File,
  fetcher: Fetcher = fetch,
): Promise<PluginUploadVerifyResult> {
  const formData = new FormData();
  formData.append('file', file);

  const response = await fetcher(
    '/api/plugin-ops/uploads/verify',
    buildRequest({
      method: 'POST',
      body: formData,
    }),
  );

  const contentType = response.headers.get('Content-Type') ?? '';
  if (!contentType.includes('application/json')) {
    await ensureOk(response);
  }

  return await readJson<PluginUploadVerifyResult>(response);
}

export async function installUpload(
  uploadId: string,
  fetcher: Fetcher = fetch,
): Promise<PluginOperationResult> {
  return await readActionResult(
    fetcher(
      `/api/plugin-ops/uploads/${encodeURIComponent(uploadId)}/install`,
      buildRequest({
        method: 'POST',
      }),
    ),
  );
}

export async function enableModule(
  moduleId: string,
  version: string,
  fetcher: Fetcher = fetch,
): Promise<PluginOperationResult> {
  return await readActionResult(
    fetcher(
      `/api/plugin-ops/modules/${encodeURIComponent(moduleId)}/enable`,
      buildRequest({
        method: 'POST',
        headers: {
          'Content-Type': 'application/json',
        },
        body: JSON.stringify({ version }),
      }),
    ),
  );
}

export async function disableModule(
  moduleId: string,
  fetcher: Fetcher = fetch,
): Promise<PluginOperationResult> {
  return await readActionResult(
    fetcher(
      `/api/plugin-ops/modules/${encodeURIComponent(moduleId)}/disable`,
      buildRequest({
        method: 'POST',
      }),
    ),
  );
}

export async function uninstallModule(
  moduleId: string,
  fetcher: Fetcher = fetch,
): Promise<PluginOperationResult> {
  return await readActionResult(
    fetcher(
      `/api/plugin-ops/modules/${encodeURIComponent(moduleId)}`,
      buildRequest({
        method: 'DELETE',
      }),
    ),
  );
}

export async function rollbackModule(
  moduleId: string,
  fetcher: Fetcher = fetch,
): Promise<PluginOperationResult> {
  return await readActionResult(
    fetcher(
      `/api/plugin-ops/modules/${encodeURIComponent(moduleId)}/rollback`,
      buildRequest({
        method: 'POST',
      }),
    ),
  );
}

const pluginOperationsClient = {
  getModules,
  verifyUpload,
  installUpload,
  enableModule,
  disableModule,
  uninstallModule,
  rollbackModule,
};

export default pluginOperationsClient;
