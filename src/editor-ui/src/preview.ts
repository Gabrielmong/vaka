import { api, errorText } from './api';
import { getState, setState, subscribe } from './store';
import type { Layout } from './types';

let inflight = false;
let queued = false;

/** Renders the current layout on the server. One request in flight; extra calls coalesce. */
export async function requestRender() {
  const s = getState();
  if (!s.layout) return;
  if (inflight) { queued = true; return; }
  inflight = true;
  const json = JSON.stringify(s.layout);
  try {
    const scale = Math.min(3, Math.max(0.5, (window.devicePixelRatio || 1) * s.zoom));
    const r = await api.render(json, scale);
    const url = 'data:image/png;base64,' + r.image;
    try { const img = new Image(); img.src = url; await img.decode(); } catch { /* show anyway */ }
    setState({ frame: url, bounds: r.bounds, rendered: JSON.parse(json) as Layout, renderError: null });
  } catch (e) {
    setState({ renderError: errorText(e) });
  } finally {
    inflight = false;
    if (queued) { queued = false; void requestRender(); }
  }
}

/** Re-render on every layout/zoom change, and once a second for live sensor values. */
export function startPreview() {
  let layout: Layout | null = null;
  let zoom = 0;
  subscribe(() => {
    const s = getState();
    if (s.layout !== layout || s.zoom !== zoom) {
      layout = s.layout;
      zoom = s.zoom;
      void requestRender();
    }
  });
  setInterval(() => {
    if (!document.hidden && !getState().dragging) void requestRender();
  }, 1000);
}
