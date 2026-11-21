import AddIcon from '@mui/icons-material/Add';
import CloseIcon from '@mui/icons-material/Close';
import ContentCopyIcon from '@mui/icons-material/ContentCopy';
import DeleteOutlineIcon from '@mui/icons-material/DeleteOutlined';
import LinkOffIcon from '@mui/icons-material/LinkOff';
import WorkspacesOutlinedIcon from '@mui/icons-material/WorkspacesOutlined';
import Box from '@mui/material/Box';
import Button from '@mui/material/Button';
import IconButton from '@mui/material/IconButton';
import Stack from '@mui/material/Stack';
import Tooltip from '@mui/material/Tooltip';
import Typography from '@mui/material/Typography';
import { useMemo, useRef } from 'react';
import { deleteSel, duplicateSel, moveToLayer, suggestFormat } from '../actions';
import { describe } from '../elements';
import {
  anchorOf, groupSelection, moveItems, removeFromGroup, renameGroup, selectGroup, selectionGroup, ungroupSelection,
} from '../groups';
import { editEl, type Item, openPicker, type Sel, select, setProp, useEditor } from '../store';
import type { PanelElement, Threshold } from '../types';
import {
  BrowseSensorsButton, ColorField, Grid2, Help, NumField, Section, SelectField, SliderField, SuggestField, SwitchField, TextInput,
} from './fields';
import { GradientSection } from './GradientSection';
import { ElementIcon, elementTitle } from './icons';
import { ImageSection } from './ImageSection';
import { LayoutInspector } from './LayoutInspector';

const DURATIONS: [number, string][] = [[30, '30 seconds'], [60, '1 minute'], [120, '2 minutes'], [300, '5 minutes'],
  [600, '10 minutes'], [1800, '30 minutes'], [3600, '1 hour']];

const WEIGHTS: [number, string][] = [[100, 'Thin'], [200, 'Extra light'], [300, 'Light'], [400, 'Regular'], [500, 'Medium'],
  [600, 'Semibold'], [700, 'Bold'], [800, 'Extra bold'], [900, 'Black']];

/** Suggestions for sensor fields: layout aliases first, then raw sensor ids. */
export function useSensorOptions() {
  const aliases = useEditor(s => s.layout?.aliases);
  const catalog = useEditor(s => s.catalog);
  return useMemo(() => {
    const keys = Object.keys(aliases ?? {});
    const ids = [...new Set((catalog ?? []).map(s => s.id))];
    const aliasSet = new Set(keys);
    return { options: [...keys, ...ids], groupBy: (o: string) => (aliasSet.has(o) ? 'Aliases' : 'Sensors') };
  }, [aliases, catalog]);
}

function SensorKeyField({ label, value, onChange }: { label: string; value: string | null | undefined; onChange: (v: string | undefined) => void }) {
  const { options, groupBy } = useSensorOptions();
  return (
    <SuggestField label={label} value={value} options={options} groupBy={groupBy} placeholder="alias or /sensor/id"
      onChange={v => onChange(v.trim() || undefined)}
      endAction={<BrowseSensorsButton onPick={k => onChange(k)} />} />
  );
}

function Thresholds({ sel, el }: { sel: NonNullable<Sel>; el: PanelElement & { thresholds?: Threshold[] | null; max?: number } }) {
  const list = el.thresholds ?? [];
  const setT = (i: number, patch: Partial<Threshold>) =>
    editEl(sel, e => { const t = (e.thresholds as Threshold[])[i]; Object.assign(t, patch); }, `thr:${sel.layer}:${sel.index}:${i}:${Object.keys(patch)[0]}`);
  return (
    <Section title="Color thresholds" action={
      <Button startIcon={<AddIcon />} onClick={() => editEl(sel, e => {
        const arr = ((e.thresholds as Threshold[] | null | undefined) ?? []);
        const max = (e.max as number | undefined) ?? 100;
        arr.push({ gte: Math.round(max * (arr.length ? 0.9 : 0.8)), color: arr.length ? '#ff4a4a' : '#ffb040' });
        e.thresholds = arr;
      })}>Add</Button>
    }>
      {!list.length && <Help>No thresholds — the base color is always used.</Help>}
      {list.map((t, i) => (
        <Stack key={i} direction="row" spacing={1} sx={{ alignItems: 'center' }}>
          <Box sx={{ width: 96, flex: 'none' }}><NumField label="At or above" value={t.gte} onChange={v => setT(i, { gte: v })} /></Box>
          <ColorField label="Color" value={t.color} onChange={v => v && setT(i, { color: v })} />
          <IconButton title="Remove" onClick={() => editEl(sel, e => {
            const arr = e.thresholds as Threshold[];
            arr.splice(i, 1);
            if (!arr.length) delete e.thresholds;
          })}><CloseIcon fontSize="small" /></IconButton>
        </Stack>
      ))}
    </Section>
  );
}

function TextSection({ sel, el, isStatic }: { sel: NonNullable<Sel>; el: Extract<PanelElement, { type: 'text' }>; isStatic: boolean }) {
  const ref = useRef<HTMLTextAreaElement>(null);
  const fonts = useEditor(s => s.fonts);
  const set = (k: string, v: unknown) => setProp(sel, k, v);
  const insert = () => openPicker((k, s) => {
    const tok = `{${k}:${suggestFormat(s)}}`;
    const ta = ref.current;
    const text = el.text ?? '';
    const start = ta?.selectionStart ?? text.length, end = ta?.selectionEnd ?? start;
    editEl(sel, e => {
      e.text = text.slice(0, start) + tok + text.slice(end);
      if (!e.value) e.value = k;
    });
  });
  return (
    <Section title="Text">
      <TextInput label="Text" multiline mono value={el.text} onChange={v => set('text', v ?? '')}
        inputRef={ref as React.Ref<HTMLInputElement | HTMLTextAreaElement>} />
      {isStatic
        ? <Help>Background labels are drawn as-is. Untick “Bake into background” to use <code>{'{sensor}'}</code> placeholders.</Help>
        : <>
            <Button variant="outlined" startIcon={<AddIcon />} onClick={insert} sx={{ alignSelf: 'flex-start' }}>Insert sensor value</Button>
            <Help>
              Placeholders: <code>{'{cpu.temp:0}'}</code> <code>{'{time:HH:mm}'}</code> <code>{'{date:MM/dd/yy}'}</code>.
              Format <code>0</code> = whole number, <code>0.0</code> = one decimal.
            </Help>
          </>}
      <SuggestField label="Font" value={el.font} options={fonts} onChange={v => set('font', v)} />
      <Grid2>
        <NumField label="Size" value={el.size} min={1} onChange={v => set('size', v)} unit="px" />
        <SelectField label="Weight" value={el.weight ?? 400} options={WEIGHTS} onChange={v => set('weight', v)} />
        <SelectField label="Align" value={el.align ?? 'left'} options={[['left', 'Left'], ['center', 'Center'], ['right', 'Right']]}
          onChange={v => set('align', v)} />
        <ColorField label="Color" value={el.color} onChange={v => v && set('color', v)} />
      </Grid2>
    </Section>
  );
}

function ElementInspector({ sel }: { sel: NonNullable<Sel> }) {
  const el = useEditor(s => s.layout?.[sel.layer][sel.index]);
  if (!el) return null;
  const isStatic = sel.layer === 'static';
  const set = (k: string, v: unknown) => setProp(sel, k, v);

  const geo: [string, number | undefined][] =
    el.type === 'line' ? [['x', el.x], ['y', el.y], ['x2', el.x2], ['y2', el.y2]]
      : el.type === 'text' ? [['x', el.x], ['y', el.y], ['w', el.w]]
        : [['x', el.x], ['y', el.y], ['w', el.w], ['h', el.h]];

  return (
    <>
      <Stack direction="row" spacing={1} sx={{ px: 2, py: 1.5, alignItems: 'center', borderBottom: 1, borderColor: 'divider' }}>
        <ElementIcon el={el} layer={sel.layer} color="primary" />
        <Typography variant="subtitle1" sx={{ fontWeight: 600, flex: 1 }}>{elementTitle(el, sel.layer)}</Typography>
        <Tooltip title="Duplicate (Ctrl+D)"><IconButton onClick={duplicateSel}><ContentCopyIcon fontSize="small" /></IconButton></Tooltip>
        <Tooltip title="Delete (Del)"><IconButton onClick={deleteSel} color="error"><DeleteOutlineIcon fontSize="small" /></IconButton></Tooltip>
      </Stack>

      {el.group && (
        <Section title="Group">
          <Help>Part of <b>{el.group}</b>. Clicking it on the canvas selects the whole group; double-click edits one element.</Help>
          <Stack direction="row" spacing={1} sx={{ mt: 1 }}>
            <Button size="small" variant="outlined" startIcon={<WorkspacesOutlinedIcon />} onClick={() => selectGroup(el.group!)}>Select group</Button>
            <Button size="small" startIcon={<LinkOffIcon />} onClick={() => removeFromGroup(sel)}>Remove from group</Button>
          </Stack>
        </Section>
      )}

      <Section title="Layer">
        <SwitchField label="Bake into background" checked={isStatic} onChange={v => moveToLayer(v ? 'static' : 'widgets')} />
        <Help>{isStatic
          ? 'Drawn once into the cached background image — cheapest. Sensor values and the clock do not update here.'
          : 'Live: redrawn every second with current sensor values.'}</Help>
      </Section>

      <Section title="Position & size">
        <Grid2>
          {geo.map(([k, v]) => <NumField key={k} label={k.toUpperCase()} value={v} step={1} onChange={n => set(k, n)} />)}
        </Grid2>
        {el.type === 'text' && <Help>W = 0: auto width, anchored at X. W &gt; 0: text is aligned inside the box.</Help>}
      </Section>

      {(el.type === 'rect' || el.type === 'ellipse') && (
        <Section title="Appearance">
          <ColorField label="Fill" nullable value={el.fill} onChange={v => set('fill', v)} />
          <ColorField label="Stroke" nullable value={el.stroke} onChange={v => set('stroke', v)} />
          <Grid2>
            <NumField label="Stroke width" value={el.strokeWidth} min={0} onChange={v => set('strokeWidth', v)} />
            {el.type === 'rect' && <NumField label="Corner radius" value={el.radius} min={0} onChange={v => set('radius', v)} />}
          </Grid2>
        </Section>
      )}
      {(el.type === 'rect' || el.type === 'ellipse') && <GradientSection sel={sel} gradient={el.gradient} baseColor={el.fill} mode="shape" />}

      {el.type === 'image' && <ImageSection sel={sel} el={el} />}

      {el.type === 'graph' && (
        <>
          <Section title="Sensor">
            <SensorKeyField label="Sensor" value={el.value} onChange={v => set('value', v)} />
            <SelectField label="Time span" value={el.duration ?? 60}
              options={DURATIONS.some(([d]) => d === (el.duration ?? 60)) ? DURATIONS : [...DURATIONS, [el.duration ?? 60, `${el.duration} s`]]}
              onChange={v => set('duration', v)} />
            <SwitchField label="Auto-scale to the visible data" checked={el.autoScale} onChange={v => set('autoScale', v)} />
            {!el.autoScale && (
              <Grid2>
                <NumField label="Min" value={el.min} onChange={v => set('min', v)} />
                <NumField label="Max" value={el.max} onChange={v => set('max', v)} />
              </Grid2>
            )}
            <Help>The panel keeps history for every graphed sensor, including on pages that aren’t showing.</Help>
          </Section>
          <Section title="Appearance">
            <Grid2>
              <ColorField label="Line" value={el.color} onChange={v => v && set('color', v)} />
              <NumField label="Line width" value={el.width} min={0} step={0.5} onChange={v => set('width', v)} />
              <ColorField label="Area fill" nullable value={el.fill} onChange={v => set('fill', v)} />
              <ColorField label="Background" nullable value={el.background} onChange={v => set('background', v)} />
              <NumField label="Grid lines" value={el.gridLines} int min={0} max={20} onChange={v => set('gridLines', v)} />
              <ColorField label="Grid color" value={el.gridColor} onChange={v => v && set('gridColor', v)} />
            </Grid2>
          </Section>
          <GradientSection sel={sel} gradient={el.gradient} baseColor={el.fill ?? el.color} mode="shape"
            help="Fills the area under the line (replaces the area fill). Angle 90 = top → bottom." />
          <Thresholds sel={sel} el={el} />
        </>
      )}

      {el.type === 'line' && (
        <Section title="Appearance">
          <Grid2>
            <ColorField label="Color" value={el.color} onChange={v => v && set('color', v)} />
            <NumField label="Width" value={el.width} min={0} onChange={v => set('width', v)} />
          </Grid2>
          <SwitchField label="Round ends" checked={el.roundCap} onChange={v => set('roundCap', v)} />
        </Section>
      )}

      {el.type === 'text' && (
        <>
          <TextSection sel={sel} el={el} isStatic={isStatic} />
          {!isStatic && (
            <Section title="Color sensor">
              <SensorKeyField label="Sensor" value={el.value} onChange={v => set('value', v)} />
              <Help>The sensor whose value picks the threshold color below.</Help>
            </Section>
          )}
          {!isStatic && <Thresholds sel={sel} el={el} />}
        </>
      )}

      {(el.type === 'bar' || el.type === 'ring') && (
        <>
          <Section title="Sensor">
            <SensorKeyField label="Sensor" value={el.value} onChange={v => set('value', v)} />
            <Grid2>
              <NumField label="Min" value={el.min} onChange={v => set('min', v)} />
              <NumField label="Max" value={el.max} onChange={v => set('max', v)} />
            </Grid2>
          </Section>
          <Section title="Appearance">
            <Grid2>
              <ColorField label="Color" value={el.color} onChange={v => v && set('color', v)} />
              <ColorField label="Track" nullable value={el.track} onChange={v => set('track', v)} />
            </Grid2>
            {el.type === 'bar' ? (
              <>
                <NumField label="Corner radius" value={el.radius} min={0} onChange={v => set('radius', v)} />
                <SwitchField label="Vertical (fills upward)" checked={el.vertical} onChange={v => set('vertical', v)} />
              </>
            ) : (
              <>
                <Grid2 cols={3}>
                  <NumField label="Thickness" value={el.thickness} min={1} onChange={v => set('thickness', v)} />
                  <NumField label="Start" value={el.startAngle} unit="°" title="0 = 3 o'clock, clockwise" onChange={v => set('startAngle', v)} />
                  <NumField label="Sweep" value={el.sweep} unit="°" title="360 = full circle, 270 = speedometer" onChange={v => set('sweep', v)} />
                </Grid2>
                <SwitchField label="Round ends" checked={el.roundCap} onChange={v => set('roundCap', v)} />
                <SwitchField label="Start tick mark" checked={el.startTick} onChange={v => set('startTick', v)} />
              </>
            )}
          </Section>
          <GradientSection sel={sel} gradient={el.gradient} baseColor={el.color} mode={el.type === 'bar' ? 'fill' : 'arc'} />
          <Thresholds sel={sel} el={el} />
        </>
      )}

      <Section title="Misc">
        <SliderField label="Opacity" value={el.opacity ?? 1} onChange={v => set('opacity', v)} format={v => `${Math.round(v * 100)}%`} />
        <TextInput label="ID (optional)" value={el.id} nullable onChange={v => set('id', v)} />
        <SwitchField label="Hidden" checked={el.hidden} onChange={v => set('hidden', v)} />
      </Section>
    </>
  );
}

/** Several elements at once: a group, or an ad-hoc multi-selection that can become one. */
function MultiInspector({ items }: { items: Item[] }) {
  const layout = useEditor(s => s.layout)!;
  const group = selectionGroup(layout, items);
  const anyGrouped = items.some(it => layout[it.layer][it.index]?.group);
  const anchor = anchorOf(layout, items);
  return (
    <>
      <Stack direction="row" spacing={1} sx={{ px: 2, py: 1.5, alignItems: 'center', borderBottom: 1, borderColor: 'divider' }}>
        <WorkspacesOutlinedIcon color="primary" />
        <Typography variant="subtitle1" noWrap sx={{ fontWeight: 600, flex: 1 }}>{group ?? `${items.length} elements`}</Typography>
        <Tooltip title="Duplicate (Ctrl+D)"><IconButton onClick={duplicateSel}><ContentCopyIcon fontSize="small" /></IconButton></Tooltip>
        <Tooltip title="Delete all (Del)"><IconButton onClick={deleteSel} color="error"><DeleteOutlineIcon fontSize="small" /></IconButton></Tooltip>
      </Stack>

      <Section title="Group">
        {group ? (
          <>
            <TextInput key={group} label="Group name" value={group} onCommit={v => renameGroup(group, v)} />
            <Help>Click any member on the canvas to select the group; double-click to edit one element on its own.</Help>
            <Button sx={{ mt: 1 }} size="small" startIcon={<LinkOffIcon />} onClick={ungroupSelection}>Ungroup (Ctrl+Shift+G)</Button>
          </>
        ) : (
          <>
            <Help>{items.length} elements selected. Group them to select and move them together — e.g. a card and everything on it.</Help>
            <Stack direction="row" spacing={1} sx={{ mt: 1 }}>
              <Button size="small" variant="contained" startIcon={<WorkspacesOutlinedIcon />} onClick={groupSelection}>Group (Ctrl+G)</Button>
              {anyGrouped && <Button size="small" startIcon={<LinkOffIcon />} onClick={ungroupSelection}>Ungroup</Button>}
            </Stack>
          </>
        )}
      </Section>

      <Section title="Position">
        <Grid2>
          <NumField label="X" value={anchor.x} step={1} onChange={v => moveItems(items, v - anchor.x, 0, 'multi:x')} />
          <NumField label="Y" value={anchor.y} step={1} onChange={v => moveItems(items, 0, v - anchor.y, 'multi:y')} />
        </Grid2>
        <Help>Top-left of the selection. Changing it moves every element together.</Help>
      </Section>

      <Section title={`Elements (${items.length})`}>
        <Stack spacing={0.25}>
          {items.map(it => {
            const el = layout[it.layer][it.index];
            if (!el) return null;
            return (
              <Button key={`${it.layer}:${it.index}`} size="small" color="inherit" onClick={() => select(it.layer, it.index)}
                startIcon={<ElementIcon el={el} layer={it.layer} fontSize="small" color="action" />}
                sx={{ justifyContent: 'flex-start', textTransform: 'none', fontWeight: 400 }}>
                <span style={{ overflow: 'hidden', textOverflow: 'ellipsis', whiteSpace: 'nowrap' }}>{describe(el)}</span>
              </Button>
            );
          })}
        </Stack>
      </Section>
    </>
  );
}

export function Inspector() {
  const sel = useEditor(s => s.sel);
  const multi = useEditor(s => s.multi);
  const hasLayout = useEditor(s => !!s.layout);
  if (!hasLayout) return null;
  return (
    <Box sx={{ overflowY: 'auto', overflowX: 'hidden', pb: 4 }}>
      {multi.length ? <MultiInspector items={multi} />
        : sel ? <ElementInspector key={`${sel.layer}:${sel.index}`} sel={sel} /> : <LayoutInspector />}
    </Box>
  );
}
