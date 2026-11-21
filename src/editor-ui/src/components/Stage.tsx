import Box from '@mui/material/Box';
import Typography from '@mui/material/Typography';
import { useEffect, useRef, useState } from 'react';
import { boundsOf, type HandleName, handlesFor, hitTest, inside, resize, snap } from '../elements';
import { anchorOf, clickUnit, itemsInRect, selectionGroup, shift, unionBounds } from '../groups';
import {
  edit, editEl, getState, isSelected, type Item, sameSel, select, selectedItems, selectItems, setState, useEditor,
} from '../store';
import type { PanelElement, Rect } from '../types';

interface Drag {
  mode: 'move' | 'resize' | 'marquee' | 'pending'; // pending: Ctrl/Shift press - a click toggles, a drag draws a box
  handle?: HandleName;
  start: { x: number; y: number };
  items: { it: Item; orig: PanelElement }[];
  anchor: { x: number; y: number };
  hit?: Item | null;
  base?: Item[]; // selection the marquee / toggle starts from
  key: string;
  moved: boolean;
}
let dragSeq = 0;

type Variant = 'hover' | 'sel' | 'member' | 'group' | 'marquee';

function Box2({ b, zoom, variant }: { b: Rect; zoom: number; variant: Variant }) {
  const solid = variant === 'sel' || variant === 'member';
  return (
    <Box sx={{
      position: 'absolute', pointerEvents: 'none',
      outline: solid ? '1px solid' : '1px dashed',
      outlineColor: variant === 'member' ? 'rgba(90,169,255,.45)' : variant === 'hover' ? 'primary.dark' : 'primary.main',
      outlineOffset: variant === 'group' ? 3 : 0,
      boxShadow: variant === 'sel' ? '0 0 0 1px rgba(0,0,0,.6)' : 'none',
      bgcolor: variant === 'marquee' ? 'rgba(90,169,255,.08)' : 'transparent',
    }} style={{
      left: b[0] * zoom, top: b[1] * zoom, width: Math.max(1, b[2] * zoom), height: Math.max(1, b[3] * zoom),
    }} />
  );
}

function Overlay() {
  const layout = useEditor(s => s.layout);
  const sel = useEditor(s => s.sel);
  const multi = useEditor(s => s.multi);
  const hover = useEditor(s => s.hover);
  const marquee = useEditor(s => s.marquee);
  const zoom = useEditor(s => s.zoom);
  useEditor(s => s.bounds); // re-render when the server's measurements arrive
  const s = getState();
  if (!layout) return null;
  // Hovering a grouped element previews what a click selects: the whole group.
  const hoverUnit = hover && !isSelected(s, hover.layer, hover.index) ? clickUnit(s, hover) : null;
  const hb = hoverUnit ? unionBounds(s, hoverUnit) : null;
  const sb = sel ? boundsOf(s, sel.layer, sel.index) : null;
  const ub = multi.length ? unionBounds(s, multi) : null;
  return (
    <>
      {hb && <Box2 b={hb} zoom={zoom} variant="hover" />}
      {multi.map(it => {
        const b = boundsOf(s, it.layer, it.index);
        return b && <Box2 key={`${it.layer}:${it.index}`} b={b} zoom={zoom} variant="member" />;
      })}
      {ub && <Box2 b={ub} zoom={zoom} variant={selectionGroup(layout, multi) ? 'group' : 'sel'} />}
      {sb && <Box2 b={sb} zoom={zoom} variant="sel" />}
      {marquee && <Box2 b={marquee} zoom={zoom} variant="marquee" />}
      {sel && handlesFor(s, sel).map(h => (
        <Box key={h.name} data-handle={h.name} sx={{
          position: 'absolute', width: 9, height: 9, ml: '-5px', mt: '-5px',
          bgcolor: '#fff', border: '1px solid', borderColor: 'primary.dark',
          borderRadius: h.name.startsWith('p') ? '50%' : '2px',
        }} style={{ left: h.x * zoom, top: h.y * zoom, cursor: h.cursor }} />
      ))}
    </>
  );
}

export function Stage() {
  const wrapRef = useRef<HTMLDivElement>(null);
  const ovRef = useRef<HTMLDivElement>(null);
  const drag = useRef<Drag | null>(null);
  const [cursor, setCursor] = useState('default');
  const frame = useEditor(s => s.frame);
  const cw = useEditor(s => s.layout?.canvas.width ?? 800);
  const ch = useEditor(s => s.layout?.canvas.height ?? 480);
  const zoom = useEditor(s => s.zoom);
  const zoomMode = useEditor(s => s.zoomMode);
  const grid = useEditor(s => s.grid);
  const showGrid = useEditor(s => s.showGrid);
  const dragging = useEditor(s => s.dragging);

  // Fit-to-window zoom.
  useEffect(() => {
    const wrap = wrapRef.current;
    if (!wrap) return;
    const update = () => {
      const z = zoomMode === 'fit'
        ? Math.max(0.1, Math.min((wrap.clientWidth - 48) / cw, (wrap.clientHeight - 80) / ch))
        : zoomMode;
      if (z !== getState().zoom) setState({ zoom: z });
    };
    update();
    const ro = new ResizeObserver(update);
    ro.observe(wrap);
    return () => ro.disconnect();
  }, [zoomMode, cw, ch]);

  const toCanvas = (e: React.PointerEvent) => {
    const r = ovRef.current!.getBoundingClientRect();
    const z = getState().zoom;
    return { x: (e.clientX - r.left) / z, y: (e.clientY - r.top) / z };
  };

  const beginDrag = (mode: Drag['mode'], p: { x: number; y: number }, items: Item[], extra: Partial<Drag> = {}) => {
    const layout = getState().layout!;
    drag.current = {
      mode, start: p, key: `drag:${++dragSeq}`, moved: false,
      items: items.map(it => ({ it, orig: structuredClone(layout[it.layer][it.index]) })),
      anchor: anchorOf(layout, items),
      ...extra,
    };
  };

  const onPointerDown = (e: React.PointerEvent) => {
    const s = getState();
    if (e.button !== 0 || !s.layout) return;
    const p = toCanvas(e);
    ovRef.current!.setPointerCapture(e.pointerId);
    e.preventDefault();
    const handle = (e.target as HTMLElement).dataset.handle as HandleName | undefined;
    if (handle && s.sel) { beginDrag('resize', p, [s.sel], { handle }); return; }

    const hit = hitTest(s, p);
    if (e.ctrlKey || e.metaKey || e.shiftKey) {
      beginDrag('pending', p, [], { hit, base: selectedItems(s) });
      return;
    }
    // Keep the current selection if the press is on one of its elements (lets you drag a card
    // even when a widget sits on top of it); otherwise select what's under the pointer.
    let items = selectedItems(s);
    if (!items.some(it => inside(p, boundsOf(s, it.layer, it.index)))) {
      if (!hit) { select(null); beginDrag('marquee', p, [], { base: [] }); return; }
      items = clickUnit(s, hit);
      selectItems(items);
    }
    beginDrag('move', p, items);
  };

  const onDoubleClick = (e: React.MouseEvent) => {
    // Double-click goes inside a group to edit just the element under the pointer.
    const s = getState();
    const r = ovRef.current!.getBoundingClientRect();
    const hit = hitTest(s, { x: (e.clientX - r.left) / s.zoom, y: (e.clientY - r.top) / s.zoom });
    if (hit && !sameSel(hit, s.sel)) select(hit.layer, hit.index);
  };

  const onPointerMove = (e: React.PointerEvent) => {
    const s = getState();
    if (!s.layout) return;
    const p = toCanvas(e);
    const d = drag.current;
    if (!d) {
      const hv = hitTest(s, p);
      if (!sameSel(hv, s.hover) && !(hv === null && s.hover === null)) setState({ hover: hv });
      const onHandle = !!(e.target as HTMLElement).dataset.handle;
      setCursor(onHandle ? 'default' : hv ? 'move' : 'default');
      return;
    }
    const dx = p.x - d.start.x, dy = p.y - d.start.y;
    if (!d.moved) {
      if (Math.abs(dx * s.zoom) < 3 && Math.abs(dy * s.zoom) < 3) return;
      d.moved = true;
      if (d.mode === 'pending') d.mode = 'marquee';
      setState({ dragging: d.mode !== 'marquee', hover: null });
    }
    if (d.mode === 'marquee') {
      const r: Rect = [Math.min(p.x, d.start.x), Math.min(p.y, d.start.y), Math.abs(dx), Math.abs(dy)];
      setState({ marquee: r });
      selectItems([...(d.base ?? []), ...itemsInRect(s, r)]);
      return;
    }
    const sn = (v: number) => snap(v, s.grid, e.altKey);
    if (d.mode === 'resize' && d.handle) {
      const { it, orig } = d.items[0];
      editEl(it, el => resize(el, orig, d.handle!, dx, dy, sn, e.shiftKey), d.key);
      return;
    }
    // Move: snap the selection's top-left corner, then shift every element by the same amount
    // so their positions relative to each other never change.
    const ox = sn(d.anchor.x + dx) - d.anchor.x, oy = sn(d.anchor.y + dy) - d.anchor.y;
    edit(l => {
      for (const { it, orig } of d.items) {
        const el = structuredClone(orig);
        shift(el, ox, oy);
        l[it.layer][it.index] = el;
      }
    }, d.key);
  };

  const onPointerUp = (e: React.PointerEvent) => {
    const d = drag.current;
    if (!d) return;
    try { ovRef.current!.releasePointerCapture(e.pointerId); } catch { /* already released */ }
    drag.current = null;
    if (d.mode === 'pending' && d.hit) {
      // Ctrl/Shift+click: add or remove the element (its whole group, unless you're inside it).
      const unit = clickUnit(getState(), d.hit);
      const base = d.base ?? [];
      const same = (a: Item, b: Item) => a.layer === b.layer && a.index === b.index;
      const all = unit.every(u => base.some(b => same(b, u)));
      selectItems(all ? base.filter(b => !unit.some(u => same(u, b))) : [...base, ...unit]);
    }
    setState({ dragging: false, editKey: null, marquee: null });
  };

  let g = Math.max(1, grid);
  while (g * zoom < 6) g *= 2;

  return (
    <Box ref={wrapRef} sx={{
      position: 'relative', overflow: 'auto', minWidth: 0, minHeight: 0,
      display: 'flex', flexDirection: 'column', alignItems: 'center', justifyContent: 'safe center', p: 3,
      background: 'radial-gradient(circle at 50% 40%, #1a1b21 0%, #0d0e11 70%)',
    }}>
      <Box sx={{ position: 'relative', flex: 'none', bgcolor: '#000', boxShadow: '0 0 0 1px #333, 0 12px 40px rgba(0,0,0,.6)' }}
        style={{ width: cw * zoom, height: ch * zoom }}>
        {frame && <img src={frame} alt="Panel preview" draggable={false}
          style={{ display: 'block', width: '100%', height: '100%', userSelect: 'none' }} />}
        <Box
          ref={ovRef}
          onPointerDown={onPointerDown}
          onPointerMove={onPointerMove}
          onPointerUp={onPointerUp}
          onPointerCancel={onPointerUp}
          onDoubleClick={onDoubleClick}
          onPointerLeave={() => { if (!drag.current && getState().hover) setState({ hover: null }); }}
          sx={{ position: 'absolute', inset: 0, touchAction: 'none' }}
          style={{
            cursor: dragging ? 'grabbing' : cursor,
            backgroundImage: showGrid
              ? 'linear-gradient(to right, rgba(90,169,255,.12) 1px, transparent 1px), linear-gradient(to bottom, rgba(90,169,255,.12) 1px, transparent 1px)'
              : undefined,
            backgroundSize: showGrid ? `${g * zoom}px ${g * zoom}px` : undefined,
          }}
        >
          <Overlay />
        </Box>
      </Box>
      <Typography variant="caption" color="text.secondary" sx={{ mt: 1.5, textAlign: 'center' }}>
        Click selects (a whole group) · double-click edits inside a group · Ctrl/Shift+click or drag a box to multi-select ·
        Ctrl+G group · handles resize (Shift keeps square) · Alt ignores snap · arrows nudge (Shift ×10) · Del delete · Ctrl+D duplicate
      </Typography>
    </Box>
  );
}
