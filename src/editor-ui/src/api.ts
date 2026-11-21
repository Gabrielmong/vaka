import type { Detection, ImageInfo, Layout, LayoutsInfo, RenderResult, SensorAlias, SensorInfo, TemplateInfo } from './types';

/** Session token: passed once as ?t=, then kept in sessionStorage so reloads of this tab work. */
const TOKEN: string | null = (() => {
  const fromUrl = new URLSearchParams(location.search).get('t');
  if (fromUrl) {
    try { sessionStorage.setItem('sp-token', fromUrl); } catch { /* storage blocked */ }
    history.replaceState(null, '', location.pathname + location.hash); // keep it out of the address bar
    return fromUrl;
  }
  try { return sessionStorage.getItem('sp-token'); } catch { return null; }
})();

export const hasToken = () => !!TOKEN;

async function send(method: string, path: string, body?: string | Blob): Promise<Response> {
  const headers: Record<string, string> = { 'X-Panel-Token': TOKEN ?? '' };
  if (typeof body === 'string') headers['Content-Type'] = 'application/json';
  else if (body) headers['Content-Type'] = 'application/octet-stream';
  const res = await fetch(path, { method, headers, body });
  if (!res.ok) {
    let err: string | undefined;
    try { err = ((await res.json()) as { error?: string } | null)?.error; } catch { /* not JSON */ }
    throw Object.assign(new Error(err || `${res.status} ${res.statusText}`), { status: res.status });
  }
  return res;
}

async function request<T>(method: string, path: string, body?: string | Blob): Promise<T> {
  const text = await (await send(method, path, body)).text();
  return (text ? JSON.parse(text) : null) as T;
}

const q = (name: string) => `name=${encodeURIComponent(name)}`;

export const api = {
  getLayout: (name: string) => request<Layout>('GET', `/api/layout?${q(name)}`),
  saveLayout: (name: string, json: string) => request<{ ok: boolean }>('PUT', `/api/layout?${q(name)}`, json),
  layouts: () => request<LayoutsInfo>('GET', '/api/layouts'),
  createLayout: (name: string, copyFrom?: string, template?: string) =>
    request<{ name: string }>('POST', '/api/layouts', JSON.stringify({ name, copyFrom, template })),
  leaseFps: () => request<{ ok: boolean }>('POST', '/api/fps/lease'),
  templates: () => request<{ templates: TemplateInfo[] }>('GET', '/api/templates'),
  buildTemplate: (id: string) => request<{ layout: Layout; detection: Detection }>('POST', '/api/templates/build', JSON.stringify({ id })),
  redetect: (json: string) =>
    request<{ aliases: Record<string, SensorAlias>; detection: Detection }>('POST', '/api/templates/redetect', json),
  renameLayout: (from: string, to: string) => request<{ name: string }>('POST', '/api/layouts/rename', JSON.stringify({ from, to })),
  deleteLayout: (name: string) => request<{ ok: boolean }>('DELETE', `/api/layouts?${q(name)}`),
  setPages: (pages: string[], seconds: number) => request<{ ok: boolean }>('PUT', '/api/pages', JSON.stringify({ pages, seconds })),
  show: (name: string) => request<{ ok: boolean }>('POST', `/api/show?${q(name)}`),
  render: (json: string, scale: number) => request<RenderResult>('POST', `/api/render?scale=${scale.toFixed(2)}`, json),
  background: async (json: string, scale: number) => (await send('POST', `/api/background?scale=${scale}`, json)).blob(),
  images: () => request<ImageInfo[]>('GET', '/api/images'),
  uploadImage: (file: File) => request<{ name: string }>('POST', `/api/images?${q(file.name)}`, file),
  deleteImage: (name: string) => request<{ ok: boolean }>('DELETE', `/api/images?${q(name)}`),
  sensors: () => request<{ ready: boolean; sensors: SensorInfo[] }>('GET', '/api/sensors'),
  fonts: () => request<string[]>('GET', '/api/fonts'),
};

/** HTTP status of a failed request, when there was one. */
export const errorStatus = (e: unknown) => (e as { status?: number } | null)?.status;

export const errorText = (e: unknown) => (e instanceof Error ? e.message : String(e));
