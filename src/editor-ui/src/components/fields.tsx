import SearchIcon from '@mui/icons-material/Search';
import Autocomplete from '@mui/material/Autocomplete';
import Box from '@mui/material/Box';
import FormControlLabel from '@mui/material/FormControlLabel';
import IconButton from '@mui/material/IconButton';
import InputAdornment from '@mui/material/InputAdornment';
import MenuItem from '@mui/material/MenuItem';
import Slider from '@mui/material/Slider';
import Stack from '@mui/material/Stack';
import Switch from '@mui/material/Switch';
import TextField from '@mui/material/TextField';
import Tooltip from '@mui/material/Tooltip';
import Typography from '@mui/material/Typography';
import { type ReactNode, type Ref, useEffect, useRef, useState } from 'react';
import { openPicker } from '../store';

/**
 * Local text state for an input bound to a store value: the store updates on every valid
 * keystroke, but what you type isn't overwritten while the field has focus (e.g. "1." or "#ff").
 */
function useDraft(external: string) {
  const [text, setText] = useState(external);
  const focused = useRef(false);
  useEffect(() => { if (!focused.current) setText(external); }, [external]);
  return {
    text,
    setText,
    onFocus: () => { focused.current = true; },
    onBlur: () => { focused.current = false; setText(external); },
  };
}

/** Full precision (alias scales like 0.0009765625 matter), minus float noise like 0.30000000000000004. */
const fmtNum = (v: number | undefined | null) => (v == null ? '' : String(Number(v.toPrecision(10))));

export function Section({ title, action, children }: { title: string; action?: ReactNode; children: ReactNode }) {
  return (
    <Box sx={{ px: 2, py: 1.5, borderBottom: 1, borderColor: 'divider' }}>
      <Stack direction="row" sx={{ alignItems: 'center', justifyContent: 'space-between', mb: 1 }}>
        <Typography variant="overline" color="text.secondary">{title}</Typography>
        {action}
      </Stack>
      <Stack spacing={1.5}>{children}</Stack>
    </Box>
  );
}

export function Grid2({ children, cols = 2 }: { children: ReactNode; cols?: number }) {
  return <Box sx={{ display: 'grid', gridTemplateColumns: `repeat(${cols}, minmax(0, 1fr))`, gap: 1.5 }}>{children}</Box>;
}

export function Help({ children }: { children: ReactNode }) {
  return (
    <Typography variant="caption" color="text.secondary" component="div"
      sx={{ '& code': { fontFamily: 'Consolas, monospace', bgcolor: 'action.hover', px: 0.5, borderRadius: 0.5, color: 'text.primary' } }}>
      {children}
    </Typography>
  );
}

export function NumField({ label, value, onChange, min, max, step, int, title, unit }: {
  label: string; value: number | undefined | null; onChange: (v: number) => void;
  min?: number; max?: number; step?: number; int?: boolean; title?: string; unit?: string;
}) {
  const d = useDraft(fmtNum(value));
  return (
    <TextField
      label={label}
      type="number"
      title={title}
      value={d.text}
      onFocus={d.onFocus}
      onBlur={d.onBlur}
      onChange={e => {
        d.setText(e.target.value);
        let v = parseFloat(e.target.value);
        if (!Number.isFinite(v)) return;
        if (int) v = Math.round(v);
        if (min !== undefined) v = Math.max(min, v);
        if (max !== undefined) v = Math.min(max, v);
        if (v !== value) onChange(v);
      }}
      slotProps={{
        htmlInput: { step: step ?? 'any', min, max },
        input: unit ? { endAdornment: <InputAdornment position="end">{unit}</InputAdornment> } : undefined,
      }}
    />
  );
}

export function TextInput({ label, value, onChange, placeholder, multiline, mono, inputRef, nullable, onCommit }: {
  label?: string; value: string | null | undefined; onChange?: (v: string | undefined) => void;
  placeholder?: string; multiline?: boolean; mono?: boolean; inputRef?: Ref<HTMLInputElement | HTMLTextAreaElement>;
  nullable?: boolean; onCommit?: (v: string) => void;
}) {
  const d = useDraft(value ?? '');
  return (
    <TextField
      label={label}
      placeholder={placeholder}
      multiline={multiline}
      minRows={multiline ? 2 : undefined}
      inputRef={inputRef}
      value={d.text}
      onFocus={d.onFocus}
      onBlur={e => {
        if (onCommit && e.target.value !== (value ?? '')) onCommit(e.target.value);
        d.onBlur();
      }}
      onKeyDown={e => { if (onCommit && e.key === 'Enter' && !multiline) (e.target as HTMLElement).blur(); }}
      onChange={e => {
        d.setText(e.target.value);
        if (onChange) onChange(nullable && !e.target.value.trim() ? undefined : e.target.value);
      }}
      slotProps={{ htmlInput: { spellCheck: false, style: mono ? { fontFamily: 'Consolas, monospace', fontSize: 12 } : undefined } }}
    />
  );
}

const COLOR_RE = /^#?([0-9a-f]{3}|[0-9a-f]{6}|[0-9a-f]{8})$/i;
function hex6(c: string | null | undefined) {
  if (!c) return '#000000';
  let s = c.replace('#', '');
  if (s.length === 3) s = s.split('').map(ch => ch + ch).join('');
  return '#' + s.slice(0, 6).padEnd(6, '0');
}
const alphaOf = (c: string | null | undefined) => {
  const s = (c ?? '').replace('#', '');
  return s.length === 8 ? s.slice(6) : '';
};

/** Hex color with a native picker swatch; keeps the alpha byte of #rrggbbaa when picking. */
export function ColorField({ label, value, onChange, nullable }: {
  label: string; value: string | null | undefined; onChange: (v: string | null) => void; nullable?: boolean;
}) {
  const d = useDraft(value ?? '');
  return (
    <TextField
      label={label}
      value={d.text}
      placeholder={nullable ? 'none' : '#ffffff'}
      onFocus={d.onFocus}
      onBlur={d.onBlur}
      onChange={e => {
        const v = e.target.value.trim();
        d.setText(e.target.value);
        if (!v && nullable) onChange(null);
        else if (COLOR_RE.test(v)) onChange(v.startsWith('#') ? v : '#' + v);
      }}
      slotProps={{
        htmlInput: { spellCheck: false, style: { fontFamily: 'Consolas, monospace', fontSize: 12 } },
        input: {
          startAdornment: (
            <InputAdornment position="start">
              <Tooltip title="Pick a color">
                <Box component="label" sx={{
                  position: 'relative', width: 22, height: 22, borderRadius: 1, cursor: 'pointer', overflow: 'hidden',
                  border: 1, borderColor: 'divider',
                  backgroundImage: 'repeating-conic-gradient(#444 0% 25%, #222 0% 50%)', backgroundSize: '8px 8px',
                }}>
                  <Box component="span" sx={{ position: 'absolute', inset: 0 }} style={{ background: value ?? 'transparent' }} />
                  <input
                    type="color"
                    value={hex6(value)}
                    onChange={e => { const v = e.target.value + alphaOf(value); d.setText(v); onChange(v); }}
                    style={{ position: 'absolute', inset: 0, opacity: 0, cursor: 'pointer', width: '100%', height: '100%' }}
                  />
                </Box>
              </Tooltip>
            </InputAdornment>
          ),
        },
      }}
    />
  );
}

export function SelectField<T extends string | number>({ label, value, options, onChange }: {
  label: string; value: T | undefined; options: [T, string][]; onChange: (v: T) => void;
}) {
  return (
    <TextField select label={label} value={value ?? ''} onChange={e => {
      const raw = e.target.value;
      const opt = options.find(([v]) => String(v) === String(raw));
      if (opt) onChange(opt[0]);
    }}>
      {options.map(([v, l]) => <MenuItem key={String(v)} value={v}>{l}</MenuItem>)}
    </TextField>
  );
}

export function SwitchField({ label, checked, onChange }: { label: string; checked: boolean | undefined; onChange: (v: boolean) => void }) {
  return (
    <FormControlLabel
      sx={{ ml: 0, gap: 1 }}
      control={<Switch checked={!!checked} onChange={e => onChange(e.target.checked)} />}
      label={<Typography variant="body2">{label}</Typography>}
    />
  );
}

export function SliderField({ label, value, onChange, min = 0, max = 1, step = 0.05, format }: {
  label: string; value: number; onChange: (v: number) => void; min?: number; max?: number; step?: number;
  format?: (v: number) => string;
}) {
  return (
    <Box>
      <Stack direction="row" sx={{ justifyContent: 'space-between' }}>
        <Typography variant="caption" color="text.secondary">{label}</Typography>
        <Typography variant="caption">{format ? format(value) : value}</Typography>
      </Stack>
      <Slider size="small" value={value} min={min} max={max} step={step}
        onChange={(_, v) => onChange(Array.isArray(v) ? v[0] : v)} />
    </Box>
  );
}

/** Free text with suggestions (fonts, sensor keys). */
export function SuggestField({ label, value, options, onChange, groupBy, renderLabel, placeholder, mono, endAction }: {
  label: string; value: string | null | undefined; options: string[]; onChange: (v: string) => void;
  groupBy?: (o: string) => string; renderLabel?: (o: string) => ReactNode; placeholder?: string; mono?: boolean;
  endAction?: ReactNode;
}) {
  return (
    <Stack direction="row" spacing={0.5} sx={{ alignItems: 'center' }}>
      <Autocomplete
        freeSolo
        fullWidth
        size="small"
        options={options}
        groupBy={groupBy}
        value={value ?? ''}
        inputValue={value ?? ''}
        onInputChange={(_, v) => { if (v !== (value ?? '')) onChange(v); }}
        renderOption={renderLabel ? (props, o) => {
          const { key, ...rest } = props as typeof props & { key: string };
          return <li key={key} {...rest}>{renderLabel(o)}</li>;
        } : undefined}
        renderInput={params => (
          <TextField {...params} label={label} placeholder={placeholder}
            slotProps={{
              ...params.slotProps,
              htmlInput: { ...params.slotProps.htmlInput, spellCheck: false, style: mono ? { fontFamily: 'Consolas, monospace', fontSize: 12 } : undefined },
            }} />
        )}
      />
      {endAction}
    </Stack>
  );
}

export function BrowseSensorsButton({ onPick, sensorsOnly }: { onPick: (key: string) => void; sensorsOnly?: boolean }) {
  return (
    <Tooltip title="Browse sensors">
      <IconButton onClick={() => openPicker(k => onPick(k), sensorsOnly)}><SearchIcon fontSize="small" /></IconButton>
    </Tooltip>
  );
}
