import type { EditorState, Sel } from './store';
import type { LayerName, Layout, PanelElement, Rect } from './types';

export type AddKind = 'rect' | 'ellipse' | 'line' | 'label' | 'image' | 'value' | 'clock' | 'bar' | 'ring' | 'graph';

const TEXT_BASE = { font: 'Segoe UI', weight: 400, align: 'left' as const, color: '#ffffff', w: 0 };

function defaultKey(layout: Layout) {
  const keys = Object.keys(layout.aliases);
  return keys.includes('cpu.temp') ? 'cpu.temp' : (keys[0] ?? 'cpu.temp');
}

export const ADD_KINDS: Record<AddKind, {
  label: string;
  description: string;
  layer: LayerName;
  make: (l: Layout) => Partial<PanelElement> & { type: PanelElement['type'] };
}> = {
  rect: { label: 'Rectangle', description: 'Card or panel, optional rounded corners', layer: 'static',
    make: () => ({ type: 'rect', w: 160, h: 100, fill: '#1b1b1b', strokeWidth: 1, radius: 0 }) },
  ellipse: { label: 'Ellipse', description: 'Circle or oval', layer: 'static',
    make: () => ({ type: 'ellipse', w: 100, h: 100, fill: '#1b1b1b', strokeWidth: 1 }) },
  line: { label: 'Line', description: 'Divider or accent line', layer: 'static',
    make: () => ({ type: 'line', x2: 160, y2: 0, color: '#ffffff', width: 2 }) },
  label: { label: 'Label', description: 'Fixed text', layer: 'static',
    make: () => ({ type: 'text', text: 'Label', size: 18, ...TEXT_BASE }) },
  value: { label: 'Sensor value', description: 'Live text, e.g. "54 °C"', layer: 'widgets',
    make: l => { const k = defaultKey(l); return { type: 'text', text: `{${k}:0}`, value: k, size: 28, ...TEXT_BASE }; } },
  clock: { label: 'Clock / date', description: 'Current time or date', layer: 'widgets',
    make: () => ({ type: 'text', text: '{time:HH:mm}', size: 40, ...TEXT_BASE }) },
  bar: { label: 'Bar', description: 'Progress bar bound to a sensor', layer: 'widgets',
    make: l => ({ type: 'bar', w: 200, h: 10, value: defaultKey(l), min: 0, max: 100, color: '#ffffff', track: '#3a3a3a', radius: 0 }) },
  ring: { label: 'Ring gauge', description: 'Circular gauge bound to a sensor', layer: 'widgets',
    make: l => ({ type: 'ring', w: 120, h: 120, value: defaultKey(l), min: 0, max: 100, color: '#ffffff', track: '#3a3a3a',
      thickness: 12, startAngle: 140, sweep: 360 }) },
  graph: { label: 'Graph', description: 'Sensor history as a line / area chart', layer: 'widgets',
    make: l => ({ type: 'graph', w: 240, h: 80, value: defaultKey(l), min: 0, max: 100, duration: 60, autoScale: false,
      color: '#5aa9ff', width: 1.5, fill: '#5aa9ff33', gridLines: 3, gridColor: '#ffffff1f' }) },
  image: { label: 'Image', description: 'Picture or logo (PNG, JPEG, WebP…)', layer: 'static',
    make: () => ({ type: 'image', w: 160, h: 100, src: '', fit: 'contain', radius: 0 }) },
};

export function snap(v: number, grid: number, off = false) {
  return off || grid <= 1 ? Math.round(v) : Math.round(v / grid) * grid;
}

export function describe(el: PanelElement) {
  if (el.id) return el.id;
  if (el.type === 'text') return (el.text || '(empty text)').replace(/\{([^{}:]+)(?::[^{}]*)?\}/g, '$1');
  if (el.type === 'image') return el.src || '(no image)';
  if ('value' in el && el.value) return el.type === 'graph' ? `${el.value} graph` : el.value;
  return el.type;
}

/** Bounds in canvas units, adjusted for edits made since the last server render. */
export function boundsOf(s: EditorState, layer: LayerName, index: number): Rect | null {
  const el = s.layout?.[layer][index];
  if (!el) return null;
  if (el.type === 'line') {
    const hw = Math.max((el.width ?? 1) / 2, 1);
    return [Math.min(el.x, el.x2) - hw, Math.min(el.y, el.y2) - hw, Math.abs(el.x2 - el.x) + 2 * hw, Math.abs(el.y2 - el.y) + 2 * hw];
  }
  if (el.type !== 'text' && el.w > 0 && el.h > 0) return [el.x, el.y, el.w, el.h];

  const size = el.type === 'text' ? (el.size ?? 16) : 16;
  const b = s.bounds[layer]?.[index];
  if (!b) return [el.x, el.y, el.w > 0 ? el.w : 40, size];
  const r = s.rendered?.[layer]?.[index];
  const sameType = r && r.type === el.type;
  return [b[0] + (sameType ? el.x - r.x : 0), b[1] + (sameType ? el.y - r.y : 0), b[2], b[3]];
}

export type HandleName = 'nw' | 'n' | 'ne' | 'e' | 'se' | 's' | 'sw' | 'w' | 'p1' | 'p2';
export interface Handle { name: HandleName; x: number; y: number; cursor: string }

export function handlesFor(s: EditorState, sel: NonNullable<Sel>): Handle[] {
  const el = s.layout?.[sel.layer][sel.index];
  if (!el) return [];
  if (el.type === 'line')
    return [{ name: 'p1', x: el.x, y: el.y, cursor: 'crosshair' }, { name: 'p2', x: el.x2, y: el.y2, cursor: 'crosshair' }];
  if (el.type === 'text') {
    if (!(el.w > 0)) return [];
    const b = boundsOf(s, sel.layer, sel.index)!;
    const my = b[1] + b[3] / 2;
    return [{ name: 'w', x: el.x, y: my, cursor: 'ew-resize' }, { name: 'e', x: el.x + el.w, y: my, cursor: 'ew-resize' }];
  }
  const { x, y, w, h } = el;
  return [
    { name: 'nw', x, y, cursor: 'nwse-resize' }, { name: 'n', x: x + w / 2, y, cursor: 'ns-resize' },
    { name: 'ne', x: x + w, y, cursor: 'nesw-resize' }, { name: 'e', x: x + w, y: y + h / 2, cursor: 'ew-resize' },
    { name: 'se', x: x + w, y: y + h, cursor: 'nwse-resize' }, { name: 's', x: x + w / 2, y: y + h, cursor: 'ns-resize' },
    { name: 'sw', x, y: y + h, cursor: 'nesw-resize' }, { name: 'w', x, y: y + h / 2, cursor: 'ew-resize' },
  ];
}

export function inside(p: { x: number; y: number }, b: Rect | null, tol = 0) {
  return !!b && p.x >= b[0] - tol && p.x <= b[0] + b[2] + tol && p.y >= b[1] - tol && p.y <= b[1] + b[3] + tol;
}

/** Topmost visible element under the point — widgets above the background. */
export function hitTest(s: EditorState, p: { x: number; y: number }): Sel {
  if (!s.layout) return null;
  const tol = 3 / s.zoom;
  for (const layer of ['widgets', 'static'] as const) {
    const items = s.layout[layer];
    for (let i = items.length - 1; i >= 0; i--) {
      if (items[i].hidden) continue;
      if (inside(p, boundsOf(s, layer, i), tol)) return { layer, index: i };
    }
  }
  return null;
}

/** Applies a resize drag to `el` starting from its original state `o`. */
export function resize(el: PanelElement, o: PanelElement, hnd: HandleName, dx: number, dy: number,
  sn: (v: number) => number, keepSquare: boolean) {
  if (el.type === 'line' && o.type === 'line') {
    if (hnd === 'p1') { el.x = sn(o.x + dx); el.y = sn(o.y + dy); }
    if (hnd === 'p2') { el.x2 = sn(o.x2 + dx); el.y2 = sn(o.y2 + dy); }
    return;
  }
  let x1 = o.x, x2 = o.x + o.w, y1 = o.y, y2 = o.y + (o.h || 0);
  if (hnd.includes('w')) x1 = Math.min(sn(o.x + dx), x2 - 1);
  if (hnd.includes('e')) x2 = Math.max(sn(o.x + o.w + dx), x1 + 1);
  if (hnd.includes('n')) y1 = Math.min(sn(o.y + dy), y2 - 1);
  if (hnd.includes('s')) y2 = Math.max(sn(o.y + o.h + dy), y1 + 1);

  if (keepSquare && hnd.length === 2) {
    const side = Math.max(x2 - x1, y2 - y1);
    if (hnd.includes('w')) x1 = x2 - side; else x2 = x1 + side;
    if (hnd.includes('n')) y1 = y2 - side; else y2 = y1 + side;
  }
  el.x = x1;
  el.w = x2 - x1;
  if (el.type !== 'text') { el.y = y1; el.h = y2 - y1; }
}
