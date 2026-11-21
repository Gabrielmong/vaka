import AddIcon from '@mui/icons-material/Add';
import CloseIcon from '@mui/icons-material/Close';
import SearchIcon from '@mui/icons-material/Search';
import Box from '@mui/material/Box';
import Button from '@mui/material/Button';
import Chip from '@mui/material/Chip';
import IconButton from '@mui/material/IconButton';
import Stack from '@mui/material/Stack';
import Tooltip from '@mui/material/Tooltip';
import Typography from '@mui/material/Typography';
import ArrowDownwardIcon from '@mui/icons-material/ArrowDownward';
import ArrowUpwardIcon from '@mui/icons-material/ArrowUpward';
import MenuItem from '@mui/material/MenuItem';
import TextField from '@mui/material/TextField';
import FunctionsIcon from '@mui/icons-material/Functions';
import RadarIcon from '@mui/icons-material/Radar';
import {
  addFormulaAlias, aliasValue, displayName, fmtValue, isAmbiguous, openLayout, promptAlias, redetectSensors, renameAlias, setPages, showOnPanel, stem, usedKeys,
} from '../actions';
import { tryParseFormula } from '../formula';
import { askConfirm, edit, getState, openPicker, useEditor } from '../store';
import type { ScaleMode } from '../types';
import { ColorField, Grid2, Help, NumField, Section, SelectField, TextInput } from './fields';

function AliasRow({ name, used }: { name: string; used: boolean }) {
  const layout = useEditor(s => s.layout)!;
  const catalog = useEditor(s => s.catalog);
  const a = layout.aliases[name];
  const { sensor, value, unit } = aliasValue(layout, catalog, name);
  const setA = (k: 'sensor' | 'scale' | 'offset' | 'expr', v: string | number) =>
    edit(l => { (l.aliases[name] as unknown as Record<string, unknown>)[k] = v; }, `alias:${name}:${k}`);
  const isFormula = a.expr != null;
  const formulaError = isFormula ? tryParseFormula(a.expr!).error : null;
  const missing = isFormula && catalog
    ? [...(tryParseFormula(a.expr!).formula?.keys ?? [])].filter(k => !layout.aliases[k] && !catalog.some(c => c.id === k))
    : [];

  return (
    <Box sx={{ py: 1.25, borderTop: 1, borderColor: 'divider', '&:first-of-type': { borderTop: 0, pt: 0 } }}>
      <Stack direction="row" spacing={0.5} sx={{ alignItems: 'center' }}>
        <TextInput label="Alias" value={name} onCommit={v => { renameAlias(name, v.trim()); }} />
        {!isFormula && <Tooltip title="Point at another sensor">
          <IconButton onClick={() => openPicker((_, s) => {
            if (!s) return;
            edit(l => {
              const al = l.aliases[name];
              al.sensor = s.id;
              if (isAmbiguous(getState().catalog, s.id)) al.name = s.name; else delete al.name;
            });
          }, true)}><SearchIcon fontSize="small" /></IconButton>
        </Tooltip>}
        <Tooltip title="Delete alias">
          <IconButton onClick={async () => {
            if (used && !await askConfirm('Delete alias?', `“${name}” is used by elements on the panel; they will show “--”.`, { confirmText: 'Delete', danger: true }))
              return;
            edit(l => { delete l.aliases[name]; });
          }}><CloseIcon fontSize="small" /></IconButton>
        </Tooltip>
      </Stack>
      {isFormula ? (
        <Box sx={{ mt: 1 }}>
          <TextInput label="Formula" mono multiline value={a.expr} onChange={v => setA('expr', v ?? '')} />
        </Box>
      ) : (
        <Box sx={{ display: 'grid', gridTemplateColumns: 'minmax(0,1fr) 88px 72px', gap: 1, mt: 1 }}>
          <TextInput label="Sensor id" mono value={a.sensor} onChange={v => v && setA('sensor', v)} />
          <NumField label="Scale" value={a.scale ?? 1} title="value × scale + offset" onChange={v => setA('scale', v)} />
          <NumField label="Offset" value={a.offset ?? 0} title="value × scale + offset" onChange={v => setA('offset', v)} />
        </Box>
      )}
      <Stack direction="row" spacing={1} sx={{ mt: 0.75, alignItems: 'center' }}>
        {isFormula ? (
          <Typography variant="caption" color={formulaError || missing.length ? 'error' : 'text.secondary'} sx={{ flex: 1 }}>
            {formulaError ?? (missing.length ? `Unknown: ${missing.join(', ')}` : 'Computed from other keys')}
          </Typography>
        ) : (
          <Typography variant="caption" color="text.secondary" noWrap sx={{ flex: 1 }}
            title={sensor ? `${displayName(layout, sensor)} · ${sensor.hardware}` : undefined}>
            {sensor ? `${displayName(layout, sensor)} · ${sensor.hardware}`
              : catalog ? 'Sensor not found on this PC' : 'Loading sensors…'}
          </Typography>
        )}
        {(sensor || (isFormula && !formulaError)) && <Chip size="small" label={fmtValue(value, unit)} />}
        {!used && <Chip size="small" variant="outlined" label="unused" />}
      </Stack>
    </Box>
  );
}

function SensorNames() {
  const names = useEditor(s => s.layout!.sensorNames);
  const entries = Object.entries(names);
  return (
    <Section title="Sensor names" action={<Button startIcon={<SearchIcon />} onClick={() => openPicker(null, true)}>Browse</Button>}>
      {!entries.length && <Help>Rename sensors from the sensor browser, e.g. “Fan #6” → “AIO pump”.</Help>}
      {entries.map(([id, name]) => (
        <Stack key={id} direction="row" spacing={0.5} sx={{ alignItems: 'center' }}>
          <Box sx={{ flex: 1, minWidth: 0 }}>
            <TextInput label="Display name" value={name} onCommit={v => edit(l => {
              if (v.trim()) l.sensorNames[id] = v.trim(); else delete l.sensorNames[id];
            })} />
            <Typography variant="caption" color="text.disabled" sx={{ fontFamily: 'Consolas, monospace' }}>{id}</Typography>
          </Box>
          <IconButton title="Remove" onClick={() => edit(l => { delete l.sensorNames[id]; })}><CloseIcon fontSize="small" /></IconButton>
        </Stack>
      ))}
    </Section>
  );
}

const same = (a: string, b: string) => a.toLowerCase() === b.toLowerCase();

/** The layouts the panel cycles through (global, not part of this layout file). */
function Pages() {
  const info = useEditor(s => s.layouts);
  const current = useEditor(s => s.layoutName);
  if (!info || !current) return null;
  const { pages, pageSeconds: seconds, showing } = info;
  const move = (i: number, d: number) => {
    const next = [...pages];
    [next[i], next[i + d]] = [next[i + d], next[i]];
    void setPages(next);
  };
  const others = info.layouts.filter(l => !pages.some(p => same(p, l.name)));
  const currentIsPage = pages.some(p => same(p, current));

  return (
    <Section title="Panel pages">
      <Help>The panel shows these layouts in turn. Pages are a panel setting, shared by all layouts.</Help>
      <Box>
        {pages.map((p, i) => (
          <Stack key={p} direction="row" spacing={0.5} sx={{ alignItems: 'center', py: 0.25 }}>
            <Typography variant="caption" color="text.disabled" sx={{ width: 16, textAlign: 'right' }}>{i + 1}</Typography>
            <Button size="small" color={same(p, current) ? 'primary' : 'inherit'} onClick={() => void openLayout(p)}
              title="Edit this layout" sx={{ flex: 1, justifyContent: 'flex-start', minWidth: 0, fontWeight: same(p, current) ? 600 : 400 }}>
              <span style={{ overflow: 'hidden', textOverflow: 'ellipsis', whiteSpace: 'nowrap' }}>{stem(p)}</span>
            </Button>
            {same(p, showing)
              ? <Chip size="small" color="success" label="on panel" />
              : <Button size="small" onClick={() => void showOnPanel(p)} title="Show this page on the panel now">Show</Button>}
            <IconButton size="small" disabled={i === 0} onClick={() => move(i, -1)} title="Earlier"><ArrowUpwardIcon sx={{ fontSize: 16 }} /></IconButton>
            <IconButton size="small" disabled={i === pages.length - 1} onClick={() => move(i, 1)} title="Later"><ArrowDownwardIcon sx={{ fontSize: 16 }} /></IconButton>
            <IconButton size="small" disabled={pages.length <= 1} onClick={() => void setPages(pages.filter(x => x !== p))}
              title="Remove from the rotation (the layout file is kept)"><CloseIcon sx={{ fontSize: 16 }} /></IconButton>
          </Stack>
        ))}
      </Box>
      {!currentIsPage && (
        <Button variant="outlined" startIcon={<AddIcon />} onClick={() => void setPages([...pages, current])} sx={{ alignSelf: 'flex-start' }}>
          Add “{stem(current)}” to the pages
        </Button>
      )}
      <Box sx={{ display: 'grid', gridTemplateColumns: 'minmax(0,1fr) 110px', gap: 1 }}>
        <TextField select label="Add page" value="" disabled={!others.length}
          onChange={e => void setPages([...pages, e.target.value])}>
          {others.map(l => <MenuItem key={l.name} value={l.name}>{stem(l.name)}</MenuItem>)}
        </TextField>
        <NumField label="Each page" value={seconds} int min={3} max={3600} unit="s"
          onChange={v => { if (v >= 3) void setPages(pages, v); }} />
      </Box>
      {pages.length < 2 && <Help>Add a second page to start rotating. Use the layout menu at the top to create one.</Help>}
    </Section>
  );
}

export function LayoutInspector() {
  const layout = useEditor(s => s.layout)!;
  const c = layout.canvas;
  const used = usedKeys(layout);
  const setCanvas = (k: 'width' | 'height' | 'background', v: number | string) =>
    edit(l => { (l.canvas as Record<string, unknown>)[k] = v; }, `canvas:${k}`);

  return (
    <>
      <Box sx={{ px: 2, py: 1.5, borderBottom: 1, borderColor: 'divider' }}>
        <Typography variant="subtitle1" sx={{ fontWeight: 600 }}>Layout</Typography>
        <Typography variant="caption" color="text.secondary">Nothing selected — click an element to edit it.</Typography>
      </Box>

      <Pages />

      <Section title="Canvas">
        <Grid2>
          <NumField label="Width" value={c.width} int min={1} unit="px" onChange={v => setCanvas('width', v)} />
          <NumField label="Height" value={c.height} int min={1} unit="px" onChange={v => setCanvas('height', v)} />
        </Grid2>
        <ColorField label="Background" value={c.background} onChange={v => v && setCanvas('background', v)} />
        <SelectField<ScaleMode> label="On other screen sizes" value={layout.scale}
          options={[['fit', 'Fit (letterbox)'], ['fill', 'Fill (crop)'], ['stretch', 'Stretch']]}
          onChange={v => edit(l => { l.scale = v; })} />
        <Help>Design size of the layout. The panel scales it to whatever display it runs on.</Help>
      </Section>

      <Section title="Sensor aliases" action={
        <Stack direction="row">
          <Tooltip title="Re-detect sensors: point the standard keys (cpu.temp, gpu.load, fan.1…) at this PC's sensors. Hand-picked ones that still fit are kept.">
            <IconButton aria-label="Re-detect sensors" onClick={() => void redetectSensors()}><RadarIcon fontSize="small" /></IconButton>
          </Tooltip>
          <Button startIcon={<FunctionsIcon />} title="Calculated from other keys, e.g. total power" onClick={() => void addFormulaAlias()}>Formula</Button>
          <Button startIcon={<AddIcon />} onClick={() => openPicker((k, s) => { if (s && k === s.id) void promptAlias(s); /* else: created via "Alias…" already */ }, true)}>Add</Button>
        </Stack>
      }>
        <Help>
          Friendly keys like <code>cpu.temp</code> that widgets use. Moving to new hardware only means re-pointing
          them here. Value = raw × scale + offset. <b>Formula</b> aliases calculate a value from other keys,
          e.g. <code>cpu.power + gpu.power + 50</code>.
        </Help>
        <Box>
          {Object.keys(layout.aliases).map(k => <AliasRow key={k} name={k} used={used.has(k)} />)}
        </Box>
      </Section>

      <SensorNames />
    </>
  );
}
