import { boundsOf, inside } from './elements';
import { edit, type EditorState, getState, type Item, selectedItems, selectItems, toast } from './store';
import type { LayerName, Layout, PanelElement, Rect } from './types';

/*
 * Groups are a tag: every element whose `group` matches belongs to it, in either layer. That lets a
 * card's background rectangle (static) and its live widgets form one unit. Clicking any member on
 * the canvas selects the whole group; double-click (or a click in the layer list) edits one element.
 */

const LAYERS: LayerName[] = ['static', 'widgets'];

export function allItems(layout: Layout): Item[] {
  return LAYERS.flatMap(layer => layout[layer].map((_, index) => ({ layer, index })));
}

const elAt = (layout: Layout, it: Item) => layout[it.layer][it.index] as PanelElement | undefined;

export function groupMembers(layout: Layout, name: string): Item[] {
  return allItems(layout).filter(it => elAt(layout, it)?.group === name);
}

export interface GroupInfo { name: string; items: Item[] }

/** Groups in the order they first appear (background first, then widgets). */
export function groupsOf(layout: Layout): GroupInfo[] {
  const map = new Map<string, Item[]>();
  for (const it of allItems(layout)) {
    const g = elAt(layout, it)?.group;
    if (!g) continue;
    if (!map.has(g)) map.set(g, []);
    map.get(g)!.push(it);
  }
  return [...map].map(([name, items]) => ({ name, items }));
}

/** The group name if `items` is exactly one complete group, else null. */
export function selectionGroup(layout: Layout, items: Item[]): string | null {
  if (!items.length) return null;
  const name = elAt(layout, items[0])?.group;
  if (!name || !items.every(it => elAt(layout, it)?.group === name)) return null;
  return groupMembers(layout, name).length === items.length ? name : null;
}

/**
 * What a plain click on `hit` selects: its whole group, unless the user is already working
 * inside that group (an individual member is selected), in which case just the element.
 */
export function clickUnit(s: EditorState, hit: Item): Item[] {
  const layout = s.layout!;
  const name = elAt(layout, hit)?.group;
  if (!name) return [hit];
  const insideGroup = !!s.sel && elAt(layout, s.sel)?.group === name;
  return insideGroup ? [hit] : groupMembers(layout, name);
}

/** Bounding box around several elements (canvas units). */
export function unionBounds(s: EditorState, items: Item[]): Rect | null {
  let x1 = Infinity, y1 = Infinity, x2 = -Infinity, y2 = -Infinity;
  for (const it of items) {
    const b = boundsOf(s, it.layer, it.index);
    if (!b) continue;
    x1 = Math.min(x1, b[0]); y1 = Math.min(y1, b[1]);
    x2 = Math.max(x2, b[0] + b[2]); y2 = Math.max(y2, b[1] + b[3]);
  }
  return x1 === Infinity ? null : [x1, y1, x2 - x1, y2 - y1];
}

/** Visible elements entirely inside `r`, expanded to their whole groups. */
export function itemsInRect(s: EditorState, r: Rect): Item[] {
  const layout = s.layout!;
  const hits = allItems(layout).filter(it => {
    if (elAt(layout, it)?.hidden) return false;
    const b = boundsOf(s, it.layer, it.index);
    return !!b && inside({ x: b[0], y: b[1] }, r) && inside({ x: b[0] + b[2], y: b[1] + b[3] }, r);
  });
  return expandGroups(layout, hits);
}

export function expandGroups(layout: Layout, items: Item[]): Item[] {
  const out = [...items];
  const names = new Set(items.map(it => elAt(layout, it)?.group).filter((g): g is string => !!g));
  for (const n of names) out.push(...groupMembers(layout, n));
  return out;
}

/** Top-left corner of the elements' own coordinates (what snapping and the X/Y fields use). */
export function anchorOf(layout: Layout, items: Item[]) {
  let x = Infinity, y = Infinity;
  for (const it of items) {
    const el = elAt(layout, it);
    if (!el) continue;
    x = Math.min(x, el.x, el.type === 'line' ? el.x2 : Infinity);
    y = Math.min(y, el.y, el.type === 'line' ? el.y2 : Infinity);
  }
  return { x: x === Infinity ? 0 : x, y: y === Infinity ? 0 : y };
}

export function shift(el: PanelElement, dx: number, dy: number) {
  el.x += dx; el.y += dy;
  if (el.type === 'line') { el.x2 += dx; el.y2 += dy; }
}

/** Moves several elements by the same offset as one undo step (`key` coalesces drags / nudges). */
export function moveItems(items: Item[], dx: number, dy: number, key?: string) {
  if (!items.length || (!dx && !dy)) return;
  edit(l => { for (const it of items) { const el = elAt(l, it); if (el) shift(el, dx, dy); } }, key);
}

export function uniqueGroupName(layout: Layout, base: string) {
  const taken = new Set(groupsOf(layout).map(g => g.name.toLowerCase()));
  if (!taken.has(base.toLowerCase())) return base;
  for (let n = 2; ; n++) if (!taken.has(`${base} ${n}`.toLowerCase())) return `${base} ${n}`;
}

/** Name for a new group: the id of its largest named element (e.g. "cpu-card"), else "Group N". */
function suggestName(layout: Layout, items: Item[]) {
  const named = items.map(it => elAt(layout, it)!).filter(el => el?.id)
    .sort((a, b) => (b.w || 0) * (b.h || 0) - (a.w || 0) * (a.h || 0));
  const id = named[0]?.id;
  if (id) return uniqueGroupName(layout, /card$/i.test(id) ? id.replace(/[-_ ]?card$/i, '') + ' card' : `${id} group`);
  let n = groupsOf(layout).length + 1;
  while (groupsOf(layout).some(g => g.name.toLowerCase() === `group ${n}`)) n++;
  return `Group ${n}`;
}

// ── Actions ─────────────────────────────────────────────────────────────────────────────

export function groupSelection() {
  const s = getState();
  const items = selectedItems(s);
  if (!s.layout || items.length < 2) {
    toast('Select two or more elements to group (Ctrl+click or drag a box).', 'info');
    return;
  }
  const name = suggestName(s.layout, items);
  edit(l => { for (const it of items) { const el = elAt(l, it); if (el) el.group = name; } });
  selectItems(items);
  toast(`Grouped ${items.length} elements as “${name}”`);
}

export function ungroupSelection() {
  const s = getState();
  const items = selectedItems(s);
  if (!s.layout || !items.some(it => elAt(s.layout!, it)?.group)) return;
  edit(l => { for (const it of items) { const el = elAt(l, it); if (el) delete el.group; } });
  selectItems(items);
}

export function selectGroup(name: string) {
  const s = getState();
  if (s.layout) selectItems(groupMembers(s.layout, name));
}

export function renameGroup(from: string, to: string) {
  const s = getState();
  const name = to.trim();
  if (!s.layout || !name || name === from) return;
  if (groupsOf(s.layout).some(g => g.name.toLowerCase() === name.toLowerCase() && g.name !== from)) {
    toast(`There is already a group called “${name}”.`, 'warning');
    return;
  }
  edit(l => { for (const it of groupMembers(l, from)) elAt(l, it)!.group = name; });
}

export function removeFromGroup(it: Item) {
  edit(l => { const el = elAt(l, it); if (el) delete el.group; });
}

/** Selects everything visible (Ctrl+A). */
export function selectAll() {
  const s = getState();
  if (s.layout) selectItems(allItems(s.layout).filter(it => !elAt(s.layout!, it)?.hidden));
}

