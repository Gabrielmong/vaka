import AddIcon from '@mui/icons-material/Add';
import CloseIcon from '@mui/icons-material/Close';
import Box from '@mui/material/Box';
import Button from '@mui/material/Button';
import IconButton from '@mui/material/IconButton';
import Stack from '@mui/material/Stack';
import { editEl, type Sel } from '../store';
import type { Gradient } from '../types';
import { ColorField, Grid2, Help, NumField, Section, SelectField, SwitchField } from './fields';

/** CSS preview of a gradient (left→right) for the swatch strip. */
function css(g: Gradient) {
  const n = g.colors.length;
  const stops = g.colors.map((c, i) => `${c} ${Math.round((g.stops?.[i] ?? (n > 1 ? i / (n - 1) : 0)) * 100)}%`);
  return `linear-gradient(90deg, ${stops.join(', ')})`;
}

const PRESETS: [string, string[]][] = [
  ['Heat', ['#3ddc84', '#ffd23f', '#ff4a4a']],
  ['Cool', ['#5aa9ff', '#a66bff']],
  ['Fade', ['#ffffff', '#ffffff00']],
];

/**
 * Gradient editor. `mode`: 'shape' (kind + angle), 'fill' (bars: along the fill), 'arc' (rings: along the arc).
 */
export function GradientSection({ sel, gradient, baseColor, mode, help }: {
  sel: NonNullable<Sel>; gradient: Gradient | null | undefined; baseColor: string | null | undefined;
  mode: 'shape' | 'fill' | 'arc'; help?: string;
}) {
  const key = (k: string) => `grad:${sel.layer}:${sel.index}:${k}`;
  const set = (fn: (g: Gradient) => void, k: string) =>
    editEl(sel, e => { if (e.gradient) fn(e.gradient as Gradient); }, key(k));
  const g = gradient;
  const n = g?.colors.length ?? 0;
  const pos = (i: number) => g?.stops?.[i] ?? (n > 1 ? i / (n - 1) : 0);

  return (
    <Section title="Gradient">
      <SwitchField label="Use a gradient" checked={!!g} onChange={on => editEl(sel, e => {
        if (on) e.gradient = { kind: 'linear', angle: mode === 'shape' ? 90 : 0, colors: [baseColor || '#5aa9ff', '#000000'] } satisfies Gradient;
        else delete e.gradient;
      })} />
      {g && (
        <>
          <Box sx={{ height: 14, borderRadius: 1, border: 1, borderColor: 'divider', backgroundImage: css(g) }} />
          <Stack direction="row" spacing={0.5}>
            {PRESETS.map(([name, colors]) => (
              <Button key={name} size="small" onClick={() => editEl(sel, e => {
                const gr = e.gradient as Gradient;
                gr.colors = [...colors];
                delete gr.stops;
              })}>{name}</Button>
            ))}
          </Stack>
          {mode === 'shape' && (
            <Grid2>
              <SelectField label="Type" value={g.kind ?? 'linear'} options={[['linear', 'Linear'], ['radial', 'Radial']]}
                onChange={v => set(gr => { gr.kind = v; }, 'kind')} />
              {(g.kind ?? 'linear') === 'linear' &&
                <NumField label="Angle" value={g.angle ?? 90} unit="°" title="0 = left → right, 90 = top → bottom"
                  onChange={v => set(gr => { gr.angle = v; }, 'angle')} />}
            </Grid2>
          )}
          {g.colors.map((c, i) => (
            <Stack key={i} direction="row" spacing={1} sx={{ alignItems: 'center' }}>
              <ColorField label={`Color ${i + 1}`} value={c} onChange={v => v && set(gr => { gr.colors[i] = v; }, `c${i}`)} />
              <Box sx={{ width: 84, flex: 'none' }}>
                <NumField label="At" unit="%" min={0} max={100} value={Math.round(pos(i) * 1000) / 10}
                  onChange={v => set(gr => {
                    gr.stops = gr.colors.map((_, j) => (j === i ? Math.min(1, Math.max(0, v / 100)) : pos(j)));
                  }, `s${i}`)} />
              </Box>
              <IconButton title="Remove color" disabled={n <= 2} onClick={() => editEl(sel, e => {
                const gr = e.gradient as Gradient;
                gr.colors.splice(i, 1);
                delete gr.stops;
              })}><CloseIcon fontSize="small" /></IconButton>
            </Stack>
          ))}
          <Button startIcon={<AddIcon />} sx={{ alignSelf: 'flex-start' }} onClick={() => editEl(sel, e => {
            const gr = e.gradient as Gradient;
            gr.colors.push(gr.colors[gr.colors.length - 1] ?? '#ffffff');
            delete gr.stops;
          })}>Add color</Button>
          <Help>{help ?? (mode === 'fill'
            ? 'Spans the whole bar: the fill reveals more of it as the value rises. A matching threshold color still wins.'
            : mode === 'arc'
              ? 'Runs along the arc from start to end. A matching threshold color still wins.'
              : 'Replaces the fill color.')}</Help>
        </>
      )}
    </Section>
  );
}
