import { api, errorText } from './api';
import { ADD_KINDS, type AddKind, snap } from './elements';
import { evalFormula, tryParseFormula } from './formula';
import { groupMembers, groupsOf, moveItems, shift, uniqueGroupName } from './groups';
import {
  askConfirm, askText, edit, editEl, getState, type Item, normalize, select, selectedItems, selectItems, setState, toast,
} from './store';
import type { LayerName, Layout, PanelElement, SensorInfo } from './types';

// ── Load / save ────────────────────────────────────────────────────────────────────────

/** Loads a layout file into the editor (the one on the panel when no name is given). */
export async function loadLayout(name?: string) {
  const info = await loadLayouts();
  const target = name ?? getState().layoutName ?? info.showing;
  const layout = normalize(await api.getLayout(target));
  setState({ layout, layoutName: target, saved: JSON.stringify(layout), undo: [], redo: [], sel: null, hover: null, editKey: null });
}

export async function loadLayouts() {
  const info = await api.layouts();
  setState({ layouts: info });
  return info;
}

export const stem = (name: string) => name.replace(/\.json$/i, '');
const isPage = (name: string | null) => !!name && !!getState().layouts?.pages.some(p => p.toLowerCase() === name.toLowerCase());

/** Switches the editor to another layout file, asking first when there are unsaved changes. */
export async function openLayout(name: string) {
  const s = getState();
  if (name === s.layoutName) return;
  if (s.dirty && !await askConfirm('Discard changes?', `Open “${stem(name)}” and lose your unsaved changes to “${stem(s.layoutName ?? '')}”.`,
    { confirmText: 'Discard', danger: true })) return;
  try { await loadLayout(name); }
  catch (e) { toast(`Couldn't open ${stem(name)}: ` + errorText(e), 'error'); }
}

let saving = false;
export async function save() {
  const s = getState();
  if (!s.layout || !s.layoutName || saving) return;
  saving = true;
  const json = JSON.stringify(s.layout);
  try {
    await api.saveLayout(s.layoutName, json);
    setState({ saved: json });
    toast(isPage(s.layoutName) ? 'Saved — the panel is showing it' : 'Saved (not one of the panel pages)');
    void loadLayouts().catch(() => { /* list refresh is best effort */ });
  } catch (e) {
    toast('Save failed: ' + errorText(e), 'error');
  } finally {
    saving = false;
  }
}

export async function revert() {
  if (getState().dirty &&
      !await askConfirm('Discard changes?', 'Reload the saved layout and lose your unsaved changes.', { confirmText: 'Discard', danger: true }))
    return;
  try { await loadLayout(); toast('Reverted to the saved layout', 'info'); }
  catch (e) { toast('Reload failed: ' + errorText(e), 'error'); }
}

function download(blob: Blob, fileName: string) {
  const a = document.createElement('a');
  a.href = URL.createObjectURL(blob);
  a.download = fileName;
  document.body.append(a);
  a.click();
  a.remove();
  setTimeout(() => URL.revokeObjectURL(a.href), 1000);
}

export function exportLayout() {
  const s = getState();
  download(new Blob([JSON.stringify(s.layout, null, 2)], { type: 'application/json' }), s.layoutName ?? 'layout.json');
}

/** Downloads the baked background (canvas color + background elements) as a PNG. */
export async function exportBackground(scale = 1) {
  const s = getState();
  if (!s.layout) return;
  try {
    const blob = await api.background(JSON.stringify(s.layout), scale);
    download(blob, `${stem(s.layoutName ?? 'layout')}-background${scale === 1 ? '' : `@${scale}x`}.png`);
  } catch (e) {
    toast('Export failed: ' + errorText(e), 'error');
  }
}

// ── Layout files & pages ──────────────────────────────────────────────────────────────

export async function newLayout(copyFrom?: string) {
  const s = getState();
  const base = copyFrom ? `${stem(copyFrom)} copy` : '';
  const name = await askText(copyFrom ? `Duplicate “${stem(copyFrom)}”` : 'New layout', {
    label: 'Name', value: base, confirmText: 'Create',
    message: copyFrom
      ? 'Creates a copy you can edit separately.'
      : 'Starts empty with the same canvas size and sensor aliases as the layout on the panel.',
  });
  if (!name?.trim()) return;
  if (copyFrom && copyFrom === s.layoutName && s.dirty)
    toast('The copy is made from the last saved version', 'info');
  try {
    const created = await api.createLayout(name.trim(), copyFrom);
    if (s.dirty && !await askConfirm('Open the new layout?', `You have unsaved changes to “${stem(s.layoutName ?? '')}”. Open “${stem(created.name)}” anyway and lose them?`,
      { confirmText: 'Discard & open', danger: true })) {
      await loadLayouts();
      toast(`Created ${stem(created.name)}`);
      return;
    }
    await loadLayout(created.name);
    toast(`Created ${stem(created.name)} — add it to the panel pages to show it`, 'info');
  } catch (e) {
    toast('Create failed: ' + errorText(e), 'error');
  }
}

/** "Duo", then "Duo 2", "Duo 3"… — the first name no layout file uses yet. */
export function uniqueLayoutName(base: string) {
  const taken = new Set((getState().layouts?.layouts ?? []).map(l => stem(l.name).toLowerCase()));
  if (!taken.has(base.toLowerCase())) return base;
  for (let i = 2; ; i++) if (!taken.has(`${base} ${i}`.toLowerCase())) return `${base} ${i}`;
}

/** Creates a layout from a template, set up for this PC's sensors, and opens it. */
export async function newFromTemplate(id: string, name: string): Promise<boolean> {
  const s = getState();
  try {
    const created = await api.createLayout(name.trim(), undefined, id);
    if (s.dirty && !await askConfirm('Open the new layout?', `You have unsaved changes to “${stem(s.layoutName ?? '')}”. Open “${stem(created.name)}” anyway and lose them?`,
      { confirmText: 'Discard & open', danger: true })) {
      await loadLayouts();
      toast(`Created ${stem(created.name)}`);
      return true;
    }
    await loadLayout(created.name);
    toast(`Created ${stem(created.name)} — add it to the panel pages to show it`, 'info');
    return true;
  } catch (e) {
    toast('Create failed: ' + errorText(e), 'error');
    return false;
  }
}

const FRIENDLY: [RegExp, string][] = [
  [/^cpu\.temp$/, 'CPU temperature'], [/^cpu\.load$/, 'CPU load'], [/^cpu\.clock$/, 'CPU clock'], [/^cpu\.power$/, 'CPU power'],
  [/^cpu\.core\d+$/, 'CPU cores'],
  [/^gpu\.temp$/, 'GPU temperature'], [/^gpu\.hotspot$/, 'GPU hotspot'], [/^gpu\.load$/, 'GPU load'], [/^gpu\.clock$/, 'GPU clock'],
  [/^gpu\.power$/, 'GPU power'], [/^gpu\.fan$/, 'GPU fan'], [/^vram\./, 'VRAM'], [/^ram\./, 'RAM'],
  [/^fan\.\d+$/, 'some case fans'], [/^pump$/, 'pump'], [/^board\.temp$/, 'board temperature'],
  [/^disk\.temp$/, 'disk temperature'], [/^disk\./, 'disk activity'], [/^net\./, 'network'],
];

/** Missing sensor keys in plain words, without repeats: "board temperature, pump". */
export function friendlyMissing(keys: string[]) {
  const words = keys.map(k => FRIENDLY.find(([re]) => re.test(k))?.[1] ?? k);
  return [...new Set(words)].join(', ');
}

/** Re-runs sensor detection for the open layout; hand-picked aliases that still fit are kept. */
export async function redetectSensors() {
  const layout = getState().layout;
  if (!layout) return;
  try {
    const { aliases, detection } = await api.redetect(JSON.stringify(layout));
    edit(l => { l.aliases = aliases; }, 'redetect');
    toast(`Updated ${detection.found.length} sensors · ${detection.missing.length} not found`,
      detection.missing.length ? 'info' : 'success');
  } catch (e) {
    toast('Re-detect failed: ' + errorText(e), 'error');
  }
}

export async function renameLayout(name: string) {
  const to = await askText(`Rename “${stem(name)}”`, { label: 'Name', value: stem(name), confirmText: 'Rename' });
  if (!to?.trim() || to.trim() === stem(name)) return;
  try {
    const res = await api.renameLayout(name, to.trim());
    if (getState().layoutName === name) setState({ layoutName: res.name });
    await loadLayouts();
  } catch (e) {
    toast('Rename failed: ' + errorText(e), 'error');
  }
}

export async function deleteLayout(name: string) {
  if (!await askConfirm(`Delete “${stem(name)}”?`, 'The file is removed (a copy stays in layouts\\backups).', { confirmText: 'Delete', danger: true }))
    return;
  try {
    await api.deleteLayout(name);
    const info = await loadLayouts();
    if (getState().layoutName === name) {
      setState({ layoutName: null });
      await loadLayout(info.showing);
    }
    toast(`Deleted ${stem(name)}`, 'info');
  } catch (e) {
    toast('Delete failed: ' + errorText(e), 'error');
  }
}

export async function setPages(pages: string[], seconds?: number) {
  const info = getState().layouts;
  if (!info) return;
  try {
    await api.setPages(pages, seconds ?? info.pageSeconds);
    await loadLayouts();
  } catch (e) {
    toast(errorText(e), 'error');
  }
}

export async function showOnPanel(name: string) {
  try { await api.show(name); await loadLayouts(); }
  catch (e) { toast(errorText(e), 'error'); }
}

// ── Images ────────────────────────────────────────────────────────────────────────────

export async function loadImages() {
  try { setState({ images: await api.images() }); }
  catch (e) { toast('Images: ' + errorText(e), 'error'); }
}

export async function uploadImage(file: File): Promise<string | null> {
  try {
    const { name } = await api.uploadImage(file);
    await loadImages();
    return name;
  } catch (e) {
    toast('Upload failed: ' + errorText(e), 'error');
    return null;
  }
}

export async function deleteImage(name: string) {
  const used = getState().layout && [...getState().layout!.static, ...getState().layout!.widgets].some(e => e.type === 'image' && e.src === name);
  if (!await askConfirm(`Delete ${name}?`, used ? 'It is used in this layout and will disappear from it.' : 'Layouts that use it will show nothing in its place.',
    { confirmText: 'Delete', danger: true })) return;
  try { await api.deleteImage(name); await loadImages(); }
  catch (e) { toast('Delete failed: ' + errorText(e), 'error'); }
}

export async function importLayout(file: File) {
  try {
    const obj: unknown = JSON.parse(await file.text());
    if (typeof obj !== 'object' || obj === null || Array.isArray(obj)) throw new Error('Not a layout object');
    edit(l => { Object.assign(l, normalize(obj as Layout)); });
    select(null);
    toast(`Imported ${file.name} — Save to apply it to the panel`, 'info');
  } catch (e) {
    toast('Import failed: ' + errorText(e), 'error');
  }
}

// ── Element operations ────────────────────────────────────────────────────────────────

export function addElement(kind: AddKind) {
  const s = getState();
  if (!s.layout) return;
  const t = ADD_KINDS[kind];
  const c = s.layout.canvas;
  const el = { x: 0, y: 0, w: 0, h: 0, opacity: 1, hidden: false, ...t.make(s.layout) } as PanelElement;
  if (el.type === 'line') {
    el.x = snap(c.width / 2 - 80, s.grid); el.y = snap(c.height / 2, s.grid);
    el.x2 = el.x + 160; el.y2 = el.y;
  } else {
    const w = el.w || 120, h = el.h || (el.type === 'text' ? el.size ?? 30 : 30);
    el.x = snap(c.width / 2 - w / 2, s.grid);
    el.y = snap(c.height / 2 - h / 2, s.grid);
  }
  edit(l => { l[t.layer].push(el); });
  select(t.layer, getState().layout![t.layer].length - 1);
}

export function deleteSel() {
  const items = selectedItems(getState());
  if (!items.length) return;
  // Highest index first so earlier splices don't shift later ones.
  const order = [...items].sort((a, b) => b.index - a.index);
  edit(l => { for (const it of order) l[it.layer].splice(it.index, 1); });
  select(null);
}

export function duplicateSel() {
  const s = getState();
  const { sel, layout } = s;
  if (s.multi.length && layout) { duplicateMany(layout, s.multi); return; }
  if (!sel || !layout) return;
  edit(l => {
    const copy = structuredClone(l[sel.layer][sel.index]);
    copy.x += 10; copy.y += 10;
    if (copy.type === 'line') { copy.x2 += 10; copy.y2 += 10; }
    if (copy.id) copy.id += '-copy';
    l[sel.layer].splice(sel.index + 1, 0, copy);
  });
  select(sel.layer, sel.index + 1);
}

/**
 * Copies several elements (appended on top of their layers, offset 10 px) and selects the copies.
 * Groups that were copied whole become new groups; partly copied groups are dropped from the copies.
 */
function duplicateMany(layout: Layout, items: Item[]) {
  const renamed = new Map<string, string | null>();
  const taken = new Set(groupsOf(layout).map(g => g.name));
  for (const it of items) {
    const g = layout[it.layer][it.index]?.group;
    if (!g || renamed.has(g)) continue;
    const whole = groupMembers(layout, g).every(m => items.some(x => x.layer === m.layer && x.index === m.index));
    let name: string | null = null;
    if (whole) {
      name = uniqueGroupName(layout, `${g} copy`);
      for (let n = 2; taken.has(name); n++) name = `${g} copy ${n}`;
      taken.add(name);
    }
    renamed.set(g, name);
  }
  const copies: Item[] = [];
  const counts = { static: layout.static.length, widgets: layout.widgets.length };
  edit(l => {
    for (const it of items) {
      const copy = structuredClone(l[it.layer][it.index]);
      shift(copy, 10, 10);
      if (copy.id) copy.id += '-copy';
      if (copy.group) {
        const g = renamed.get(copy.group);
        if (g) copy.group = g; else delete copy.group;
      }
      l[it.layer].push(copy);
      copies.push({ layer: it.layer, index: counts[it.layer]++ });
    }
  });
  selectItems(copies);
}

export function reorder(layer: LayerName, index: number, dir: 1 | -1) {
  const s = getState();
  const list = s.layout?.[layer];
  const j = index + dir;
  if (!list || j < 0 || j >= list.length) return;
  edit(l => { const a = l[layer]; [a[index], a[j]] = [a[j], a[index]]; });
  // Keep the same elements selected after the swap.
  const remap = (it: Item): Item => it.layer !== layer ? it
    : it.index === index ? { layer, index: j } : it.index === j ? { layer, index } : it;
  if (s.multi.length) selectItems(s.multi.map(remap));
  else if (s.sel) select(s.sel.layer, remap(s.sel).index);
}

export function moveToLayer(target: LayerName) {
  const { sel } = getState();
  if (!sel || sel.layer === target) return;
  edit(l => { const [el] = l[sel.layer].splice(sel.index, 1); l[target].push(el); });
  select(target, getState().layout![target].length - 1);
}

export function toggleHidden(layer: LayerName, index: number) {
  editEl({ layer, index }, el => { el.hidden = !el.hidden; });
}

export function nudge(dx: number, dy: number) {
  const items = selectedItems(getState());
  moveItems(items, dx, dy, `nudge:${items.map(it => `${it.layer}${it.index}`).join(',')}`);
}

// ── Sensors & aliases ─────────────────────────────────────────────────────────────────

export async function loadCatalog() {
  const r = await api.sensors();
  if (r.ready) setState({ catalog: r.sensors });
  return r.ready;
}

export function findSensor(catalog: SensorInfo[] | null, id: string, name?: string | null) {
  if (!catalog) return null;
  const matches = catalog.filter(s => s.id === id);
  if (!matches.length) return null;
  return (name && matches.find(s => s.name.toLowerCase() === name.toLowerCase())) || matches[0];
}

export const isAmbiguous = (catalog: SensorInfo[] | null, id: string) =>
  (catalog ?? []).filter(s => s.id === id).length > 1;

export const displayName = (layout: Layout, s: SensorInfo) => layout.sensorNames[s.id] || s.name;

/** Resolves an alias to its sensor and scaled value. */
/** Current value of any layout key from the sensor catalog (aliases, formulas, raw ids, built-ins). */
export function keyValue(layout: Layout, catalog: SensorInfo[] | null, key: string, depth = 0): number | null {
  const a = layout.aliases[key];
  if (!a) return findSensor(catalog, key)?.value ?? null;
  if (a.expr) {
    const { formula } = tryParseFormula(a.expr);
    return formula && depth < 8 ? evalFormula(formula, k => keyValue(layout, catalog, k, depth + 1)) : null;
  }
  const s = findSensor(catalog, a.sensor, a.name);
  return s?.value == null ? null : s.value * (a.scale ?? 1) + (a.offset ?? 0);
}

export function aliasValue(layout: Layout, catalog: SensorInfo[] | null, key: string) {
  const a = layout.aliases[key];
  if (!a) return { sensor: null, value: null, unit: '' };
  if (a.expr) return { sensor: null, value: keyValue(layout, catalog, key), unit: '' };
  const sensor = findSensor(catalog, a.sensor, a.name);
  const value = sensor?.value == null ? null : sensor.value * (a.scale ?? 1) + (a.offset ?? 0);
  return { sensor, value, unit: (a.scale ?? 1) === 1 && (a.offset ?? 0) === 0 ? sensor?.unit ?? '' : '' };
}

export function fmtValue(v: number | null | undefined, unit?: string) {
  if (v == null) return '—';
  if (unit === 'B/s') {
    const units = ['B/s', 'KB/s', 'MB/s', 'GB/s'];
    let i = 0;
    while (Math.abs(v) >= 1024 && i < units.length - 1) { v /= 1024; i++; }
    return `${v.toFixed(i ? 1 : 0)} ${units[i]}`;
  }
  const a = Math.abs(v);
  const txt = a >= 100 ? v.toFixed(0) : a >= 10 ? v.toFixed(1) : v.toFixed(2);
  return unit ? `${txt} ${unit}` : txt;
}

export function suggestFormat(s: SensorInfo | null) {
  const unit = s?.unit ?? '';
  if (['°C', '%', 'RPM', 'MHz', 'W', 'Hz', 'FPS'].includes(unit)) return '0';
  if (unit === 'ms') return '0.0';
  if (unit === 'GB' || unit === 'V') return '0.0';
  return '0.##';
}

function suggestAlias(layout: Layout, s: SensorInfo) {
  const prefix: Record<string, string> = {
    Cpu: 'cpu', GpuNvidia: 'gpu', GpuAmd: 'gpu', GpuIntel: 'gpu', Memory: 'ram', Motherboard: 'mb', SuperIO: 'mb',
    Storage: 'disk', Network: 'net', Drive: 'drive', EmbeddedController: 'ec',
  };
  const slug = displayName(layout, s).toLowerCase().replace(/[^a-z0-9]+/g, '-').replace(/^-|-$/g, '');
  return `${prefix[s.hardwareType] ?? 'sensor'}.${slug || 'value'}`;
}

export const KEY_RE = /^[A-Za-z][\w.-]*$/;
const KEY_HELP = 'Start with a letter; use letters, digits, ".", "-" or "_".';

/** Asks for an alias name and creates it. Returns the name, or null if cancelled. */
export async function promptAlias(s: SensorInfo): Promise<string | null> {
  const layout = getState().layout;
  if (!layout) return null;
  for (;;) {
    const name = (await askText('Create alias', {
      message: `A short key for “${displayName(layout, s)}” (${s.hardware}) that widgets can use, e.g. cpu.temp.`,
      label: 'Alias name', value: suggestAlias(layout, s), confirmText: 'Create',
    }))?.trim();
    if (!name) return null;
    if (!KEY_RE.test(name)) { toast(KEY_HELP, 'warning'); continue; }
    const existing = getState().layout!.aliases[name];
    if (existing && existing.sensor !== s.id &&
        !await askConfirm('Replace alias?', `“${name}” already points to ${existing.sensor}.`, { confirmText: 'Replace' }))
      continue;
    const ambiguous = isAmbiguous(getState().catalog, s.id);
    edit(l => { l.aliases[name] = ambiguous ? { sensor: s.id, name: s.name, scale: 1, offset: 0 } : { sensor: s.id, scale: 1, offset: 0 }; });
    return name;
  }
}

export async function renameSensor(s: SensorInfo) {
  const layout = getState().layout;
  if (!layout) return;
  const v = await askText('Rename sensor', {
    message: `Display name for “${s.name}” (${s.hardware}). Leave empty to reset.`,
    label: 'Display name', value: layout.sensorNames[s.id] || s.name, confirmText: 'Save',
  });
  if (v === null) return;
  edit(l => {
    if (!v.trim() || v.trim() === s.name) delete l.sensorNames[s.id];
    else l.sensorNames[s.id] = v.trim();
  });
}

export function usedKeys(layout: Layout) {
  const keys = new Set<string>();
  for (const el of layout.widgets) {
    if ('value' in el && el.value) keys.add(el.value);
    if (el.type === 'text' && el.text)
      for (const m of el.text.matchAll(/\{([^{}:]+)(?::[^{}]*)?\}/g)) keys.add(m[1].trim());
  }
  // Keys a used formula alias reads count as used too.
  const todo = [...keys];
  while (todo.length) {
    const expr = layout.aliases[todo.pop()!]?.expr;
    if (!expr) continue;
    for (const k of tryParseFormula(expr).formula?.keys ?? []) if (!keys.has(k)) { keys.add(k); todo.push(k); }
  }
  return keys;
}

/** Adds a computed alias (formula), asking for its name and formula. */
export async function addFormulaAlias() {
  const layout = getState().layout;
  if (!layout) return;
  const name = (await askText('New formula alias', {
    message: 'A key whose value is calculated from other keys, e.g. total power.',
    label: 'Alias', value: 'system.power', confirmText: 'Next',
  }))?.trim();
  if (!name) return;
  if (!KEY_RE.test(name)) { toast(KEY_HELP, 'warning'); return; }
  if (layout.aliases[name]) { toast(`“${name}” already exists.`, 'warning'); return; }
  const expr = (await askText(`Formula for ${name}`, {
    message: 'Use keys and + - * / ( ). [key] for keys with dashes or raw sensor ids; min(), max(), abs(); a ?? b falls back to b.',
    label: 'Formula', value: 'cpu.power + gpu.power', confirmText: 'Add',
  }))?.trim();
  if (!expr) return;
  const { error } = tryParseFormula(expr);
  if (error) { toast(error, 'error'); return; }
  edit(l => { l.aliases[name] = { sensor: '', expr }; });
}

/** Renames an alias and updates every reference to it. Returns false if rejected. */
export function renameAlias(oldKey: string, newKey: string) {
  const layout = getState().layout;
  if (!layout || !newKey || newKey === oldKey) return false;
  if (!KEY_RE.test(newKey)) { toast(KEY_HELP, 'warning'); return false; }
  if (newKey in layout.aliases) { toast(`“${newKey}” already exists`, 'warning'); return false; }
  const re = new RegExp(`\\{\\s*${oldKey.replace(/[.*+?^${}()|[\]\\]/g, '\\$&')}\\s*(?=[:}])`, 'g');
  edit(l => {
    l.aliases = Object.fromEntries(Object.entries(l.aliases).map(([k, v]) => [k === oldKey ? newKey : k, v]));
    for (const el of [...l.static, ...l.widgets] as (PanelElement & { value?: string | null })[]) {
      if (el.value === oldKey) el.value = newKey;
      if (el.type === 'text' && el.text) el.text = el.text.replace(re, '{' + newKey);
    }
  });
  return true;
}
