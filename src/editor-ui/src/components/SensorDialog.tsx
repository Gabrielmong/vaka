import CloseIcon from '@mui/icons-material/Close';
import SearchIcon from '@mui/icons-material/Search';
import Box from '@mui/material/Box';
import Button from '@mui/material/Button';
import CircularProgress from '@mui/material/CircularProgress';
import Dialog from '@mui/material/Dialog';
import DialogContent from '@mui/material/DialogContent';
import DialogTitle from '@mui/material/DialogTitle';
import FormControlLabel from '@mui/material/FormControlLabel';
import IconButton from '@mui/material/IconButton';
import InputAdornment from '@mui/material/InputAdornment';
import Stack from '@mui/material/Stack';
import Switch from '@mui/material/Switch';
import TextField from '@mui/material/TextField';
import Typography from '@mui/material/Typography';
import { memo, useDeferredValue, useEffect, useMemo, useState } from 'react';
import { errorText } from '../api';
import { aliasValue, displayName, fmtValue, loadCatalog, promptAlias, renameSensor } from '../actions';
import { closePicker, getState, toast, useEditor } from '../store';
import type { SensorInfo } from '../types';

const COLS = 'minmax(180px, 1.5fr) 90px 100px minmax(120px, 1fr) 210px';

function pick(key: string, s: SensorInfo | null) {
  const cb = getState().picker?.onPick;
  closePicker();
  cb?.(key, s);
}

function HwHead({ title, subtitle }: { title: string; subtitle: string }) {
  return (
    <Box sx={{ position: 'sticky', top: 0, zIndex: 1, bgcolor: 'background.paper', px: 3, py: 0.75, borderBottom: 1, borderColor: 'divider' }}>
      <Typography variant="subtitle2" component="span">{title}</Typography>
      <Typography variant="caption" color="text.secondary" sx={{ ml: 1 }}>{subtitle}</Typography>
    </Box>
  );
}

const SensorRow = memo(function SensorRow({ s, renamed, canPick }: { s: SensorInfo; renamed?: string; canPick: boolean }) {
  return (
    <Box sx={{ display: 'grid', gridTemplateColumns: COLS, gap: 1.5, alignItems: 'center', px: 3, py: 0.5, '&:hover': { bgcolor: 'action.hover' } }}>
      <Typography variant="body2" noWrap title={s.name}>
        {renamed || s.name}
        {renamed && <Typography component="span" variant="caption" color="text.secondary" sx={{ ml: 1 }}>{s.name}</Typography>}
      </Typography>
      <Typography variant="caption" color="text.secondary">{s.type}</Typography>
      <Typography variant="body2" sx={{ textAlign: 'right', fontVariantNumeric: 'tabular-nums' }}>{fmtValue(s.value, s.unit)}</Typography>
      <Typography variant="caption" color="text.disabled" noWrap title={s.id} sx={{ fontFamily: 'Consolas, monospace' }}>{s.id}</Typography>
      <Stack direction="row" spacing={0.5}>
        {canPick && <Button variant="contained" onClick={() => pick(s.id, s)}>Use</Button>}
        {s.id.startsWith('/') && (
          <Button title="Create a friendly key for this sensor" onClick={async () => {
            const name = await promptAlias(s);
            if (name && getState().picker?.onPick) pick(name, s);
          }}>Alias…</Button>
        )}
        <Button title="Display name shown in the editor" onClick={() => void renameSensor(s)}>Rename…</Button>
      </Stack>
    </Box>
  );
});

export function SensorDialog() {
  const picker = useEditor(s => s.picker);
  const layout = useEditor(s => s.layout);
  const catalog = useEditor(s => s.catalog);
  const [query, setQuery] = useState('');
  const [hideEmpty, setHideEmpty] = useState(false);
  const q = useDeferredValue(query.trim().toLowerCase());
  const open = !!picker;

  // Poll live values while open (also wakes full sensor scanning on the server).
  useEffect(() => {
    if (!open) return;
    setQuery('');
    let alive = true;
    const tick = () => loadCatalog().catch(e => { if (alive) toast('Sensors: ' + errorText(e), 'error'); });
    void tick();
    const t = setInterval(tick, 2000);
    return () => { alive = false; clearInterval(t); };
  }, [open]);

  const groups = useMemo(() => {
    const map = new Map<string, { type: string; items: SensorInfo[] }>();
    if (!catalog || !layout) return map;
    const match = (...f: (string | undefined)[]) => !q || f.some(x => x?.toLowerCase().includes(q));
    for (const s of catalog) {
      if (!match(s.name, layout.sensorNames[s.id], s.hardware, s.type, s.id)) continue;
      if (hideEmpty && (s.value == null || s.value === 0)) continue;
      if (!map.has(s.hardware)) map.set(s.hardware, { type: s.hardwareType, items: [] });
      map.get(s.hardware)!.items.push(s);
    }
    return map;
  }, [catalog, layout, q, hideEmpty]);

  if (!layout) return null;
  const canPick = !!picker?.onPick;
  const aliasKeys = picker?.sensorsOnly ? [] : Object.keys(layout.aliases).filter(k => !q || k.toLowerCase().includes(q) || layout.aliases[k].sensor.toLowerCase().includes(q));
  const empty = !aliasKeys.length && ![...groups.values()].some(g => g.items.length);

  return (
    <Dialog open={open} onClose={closePicker} maxWidth="lg" fullWidth
      slotProps={{ paper: { sx: { height: 'min(760px, 90vh)' } } }}>
      <DialogTitle sx={{ display: 'flex', gap: 2, alignItems: 'center', pb: 1.5 }}>
        <Typography variant="h6" sx={{ flex: 'none' }}>{canPick ? 'Choose a sensor' : 'Sensors'}</Typography>
        <TextField autoFocus placeholder="Search name, hardware, type or id…" value={query} onChange={e => setQuery(e.target.value)}
          slotProps={{ input: { startAdornment: <InputAdornment position="start"><SearchIcon fontSize="small" /></InputAdornment> } }} />
        <FormControlLabel sx={{ flex: 'none', mr: 0 }} label={<Typography variant="body2">Hide empty</Typography>}
          control={<Switch checked={hideEmpty} onChange={e => setHideEmpty(e.target.checked)} />} />
        <IconButton onClick={closePicker} title="Close"><CloseIcon /></IconButton>
      </DialogTitle>
      <DialogContent dividers sx={{ p: 0 }}>
        {aliasKeys.length > 0 && (
          <>
            <HwHead title="Aliases" subtitle="defined in this layout" />
            {aliasKeys.map(k => {
              const { sensor, value, unit } = aliasValue(layout, catalog, k);
              return (
                <Box key={k} sx={{ display: 'grid', gridTemplateColumns: COLS, gap: 1.5, alignItems: 'center', px: 3, py: 0.5, '&:hover': { bgcolor: 'action.hover' } }}>
                  <Typography variant="body2" noWrap>
                    {k}
                    {sensor && <Typography component="span" variant="caption" color="text.secondary" sx={{ ml: 1 }}>{displayName(layout, sensor)}</Typography>}
                  </Typography>
                  <Typography variant="caption" color="text.secondary">alias</Typography>
                  <Typography variant="body2" sx={{ textAlign: 'right', fontVariantNumeric: 'tabular-nums' }}>{fmtValue(value, unit)}</Typography>
                  <Typography variant="caption" color="text.disabled" noWrap sx={{ fontFamily: 'Consolas, monospace' }}>{layout.aliases[k].expr ? `= ${layout.aliases[k].expr}` : layout.aliases[k].sensor}</Typography>
                  <Box>{canPick && <Button variant="contained" onClick={() => pick(k, sensor ? { ...sensor, value } : null)}>Use</Button>}</Box>
                </Box>
              );
            })}
          </>
        )}
        {!catalog && (
          <Stack spacing={2} sx={{ alignItems: 'center', py: 6 }}>
            <CircularProgress size={28} />
            <Typography variant="body2" color="text.secondary">Reading all sensors — the first scan takes a few seconds…</Typography>
          </Stack>
        )}
        {[...groups].map(([hw, g]) => (
          <Box key={hw}>
            <HwHead title={hw} subtitle={g.type} />
            {g.items.map((s, i) => <SensorRow key={s.id + '|' + i} s={s} renamed={layout.sensorNames[s.id]} canPick={canPick} />)}
          </Box>
        ))}
        {catalog && empty && <Typography color="text.secondary" sx={{ textAlign: 'center', py: 6 }}>No sensors match.</Typography>}
      </DialogContent>
    </Dialog>
  );
}
