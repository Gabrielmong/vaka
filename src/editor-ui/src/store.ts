import { useSyncExternalStore } from 'react';
import type { Bounds, ImageInfo, LayerName, Layout, LayoutsInfo, PanelElement, Props, Rect, SensorInfo } from './types';

export type Item = { layer: LayerName; index: number };
export type Sel = Item | null;
/** Shared empty selection so selectors stay referentially stable. */
export const NO_ITEMS: Item[] = [];
export type PickFn = (key: string, sensor: SensorInfo | null) => void;
export type ToastKind = 'success' | 'info' | 'warning' | 'error';

export interface DialogRequest {
  kind: 'prompt' | 'confirm';
  title: string;
  message?: string;
  label?: string;
  value?: string;
  confirmText?: string;
  danger?: boolean;
  resolve: (v: string | boolean | null) => void;
}

export interface EditorState {
  layout: Layout | null;
  layoutName: string | null; // file being edited
  layouts: LayoutsInfo | null;
  images: ImageInfo[] | null;
  saved: string;          // JSON of the last loaded/saved layout
  dirty: boolean;
  sel: Sel;               // the one element being edited (null when several are selected)
  multi: Item[];          // two or more elements selected together, e.g. a group (then sel is null)
  hover: Sel;
  marquee: Rect | null;   // rubber-band selection rectangle, canvas units
  undo: string[];
  redo: string[];
  editKey: string | null; // consecutive edits with the same key share one undo step
  editAt: number;
  dragging: boolean;
  frame: string | null;   // data: URL of the last rendered preview
  bounds: Bounds;
  rendered: Layout | null; // layout the bounds were measured on
  renderError: string | null;
  catalog: SensorInfo[] | null;
  fonts: string[];
  zoomMode: 'fit' | number;
  zoom: number;
  grid: number;
  showGrid: boolean;
  toast: { text: string; kind: ToastKind; id: number } | null;
  picker: { onPick: PickFn | null; sensorsOnly: boolean } | null;
  dialog: DialogRequest | null;
  galleryOpen: boolean;
  fatal: string | null;
}

let state: EditorState = {
  layout: null, layoutName: null, layouts: null, images: null, saved: '', dirty: false, sel: null, multi: NO_ITEMS, hover: null, marquee: null, undo: [], redo: [],
  editKey: null, editAt: 0, dragging: false,
  frame: null, bounds: { static: [], widgets: [] }, rendered: null, renderError: null,
  catalog: null, fonts: [],
  zoomMode: 'fit', zoom: 1, grid: 5, showGrid: false,
  toast: null, picker: null, dialog: null, galleryOpen: false, fatal: null,
};

const listeners = new Set<() => void>();

export const getState = () => state;

export function subscribe(fn: () => void) {
  listeners.add(fn);
  return () => { listeners.delete(fn); };
}

export function setState(patch: Partial<EditorState> | ((s: EditorState) => Partial<EditorState>)) {
  const prev = state;
  const next = { ...prev, ...(typeof patch === 'function' ? patch(prev) : patch) };
  if (next.layout !== prev.layout || next.saved !== prev.saved)
    next.dirty = !!next.layout && JSON.stringify(next.layout) !== next.saved;
  state = next;
  listeners.forEach(l => l());
}

/** Selector must return existing references or primitives (no new objects). */
export function useEditor<T>(selector: (s: EditorState) => T): T {
  return useSyncExternalStore(subscribe, () => selector(state));
}

export function normalize(layout: Layout): Layout {
  layout.canvas ??= { width: 800, height: 480, background: '#000000' };
  layout.scale ??= 'fit';
  layout.aliases ??= {};
  layout.sensorNames ??= {};
  layout.static ??= [];
  layout.widgets ??= [];
  return layout;
}

// ── Edits & history ────────────────────────────────────────────────────────────────────

/** Applies a mutation to a copy of the layout and records undo. */
export function edit(fn: (l: Layout) => void, key?: string) {
  const s = state;
  if (!s.layout) return;
  const now = Date.now();
  const coalesce = !!key && key === s.editKey && now - s.editAt < 1500;
  const next = structuredClone(s.layout);
  fn(next);
  setState({
    layout: next,
    undo: coalesce ? s.undo : [...s.undo, JSON.stringify(s.layout)].slice(-200),
    redo: coalesce ? s.redo : [],
    editKey: key ?? null,
    editAt: now,
  });
}

/** Edits one element (by selection) — `key` coalesces e.g. typing into one field. */
export function editEl(sel: NonNullable<Sel>, fn: (el: PanelElement & Props, l: Layout) => void, key?: string) {
  edit(l => {
    const el = l[sel.layer][sel.index];
    if (el) fn(el as PanelElement & Props, l);
  }, key);
}

/** Sets one property on an element; typing into the same field is one undo step. */
export function setProp(sel: NonNullable<Sel>, prop: string, value: unknown) {
  editEl(sel, el => {
    if (value === undefined) delete el[prop];
    else el[prop] = value;
  }, `prop:${sel.layer}:${sel.index}:${prop}`);
}

function validSel(layout: Layout | null, sel: Sel): Sel {
  return sel && layout?.[sel.layer][sel.index] ? sel : null;
}

/** Drops selected items that no longer exist (after undo/redo). */
function validSelection(layout: Layout, s: EditorState): Pick<EditorState, 'sel' | 'multi'> {
  if (!s.multi.length) return { sel: validSel(layout, s.sel), multi: NO_ITEMS };
  const kept = s.multi.filter(it => layout[it.layer][it.index]);
  return kept.length > 1 ? { sel: null, multi: kept } : { sel: kept[0] ?? null, multi: NO_ITEMS };
}

export function undo() {
  const s = state;
  if (!s.undo.length || !s.layout) return;
  const layout = normalize(JSON.parse(s.undo[s.undo.length - 1]));
  setState({
    layout, undo: s.undo.slice(0, -1), redo: [...s.redo, JSON.stringify(s.layout)],
    editKey: null, ...validSelection(layout, s), hover: null,
  });
}

export function redo() {
  const s = state;
  if (!s.redo.length || !s.layout) return;
  const layout = normalize(JSON.parse(s.redo[s.redo.length - 1]));
  setState({
    layout, redo: s.redo.slice(0, -1), undo: [...s.undo, JSON.stringify(s.layout)],
    editKey: null, ...validSelection(layout, s), hover: null,
  });
}

export function select(layer: LayerName | null, index = 0) {
  setState({ sel: layer ? { layer, index } : null, multi: NO_ITEMS, editKey: null });
}

export const sameSel = (a: Sel, b: Sel) => !!a && !!b && a.layer === b.layer && a.index === b.index;
const itemKey = (it: Item) => `${it.layer}:${it.index}`;

/** Everything selected: the multi-selection, or the single element, or nothing. */
export const selectedItems = (s: EditorState): Item[] => s.multi.length ? s.multi : s.sel ? [s.sel] : NO_ITEMS;

export const isSelected = (s: EditorState, layer: LayerName, index: number) =>
  s.multi.length ? s.multi.some(it => it.layer === layer && it.index === index) : s.sel?.layer === layer && s.sel.index === index;

/** Selects any number of elements; one becomes the normal single selection. */
export function selectItems(items: Item[]) {
  const seen = new Set<string>();
  const uniq = items.filter(it => !seen.has(itemKey(it)) && !!seen.add(itemKey(it)))
    .sort((a, b) => a.layer.localeCompare(b.layer) || a.index - b.index);
  if (uniq.length <= 1) setState({ sel: uniq[0] ?? null, multi: NO_ITEMS, editKey: null });
  else setState({ sel: null, multi: uniq, editKey: null });
}

// ── Toasts, dialogs, sensor picker ─────────────────────────────────────────────────────

export function toast(text: string, kind: ToastKind = 'success') {
  setState({ toast: { text, kind, id: Date.now() } });
}

export function askText(title: string, opts: { message?: string; label?: string; value?: string; confirmText?: string } = {}) {
  return new Promise<string | null>(resolve =>
    setState({ dialog: { kind: 'prompt', title, ...opts, resolve: v => resolve(typeof v === 'string' ? v : null) } }));
}

export function askConfirm(title: string, message: string, opts: { confirmText?: string; danger?: boolean } = {}) {
  return new Promise<boolean>(resolve =>
    setState({ dialog: { kind: 'confirm', title, message, ...opts, resolve: v => resolve(v === true) } }));
}

export function openPicker(onPick: PickFn | null, sensorsOnly = false) {
  setState({ picker: { onPick, sensorsOnly } });
}
export const closePicker = () => setState({ picker: null });
