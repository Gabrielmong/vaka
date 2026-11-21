import Box from '@mui/material/Box';
import Typography from '@mui/material/Typography';
import type { Manifest } from '../manifest';
import { colors } from '../theme';

/** Where the loop is: one segment per simulated workload, the active one filling as its seconds pass. */
export function StateTimeline({ manifest, frame, still }: { manifest: Manifest; frame: number; still: boolean }) {
  const second = frame % manifest.loopSeconds;
  return (
    <Box sx={{ display: 'grid', gridTemplateColumns: `repeat(${manifest.states.length}, 1fr)`, gap: 1 }}>
      {manifest.states.map(s => {
        const active = still ? s.from === 0 : second >= s.from && second < s.to;
        const fill = still ? (active ? 1 : 0) : active ? (second - s.from + 1) / (s.to - s.from) : 0;
        return (
          <Box key={s.name} aria-current={active ? 'step' : undefined}>
            <Box sx={{ height: 3, borderRadius: 2, bgcolor: colors.line, overflow: 'hidden' }}>
              <Box sx={{ height: '100%', width: `${fill * 100}%`, bgcolor: colors.cpu,
                         transition: still || fill === 0 ? 'none' : 'width 1s linear' }} />
            </Box>
            <Typography variant="body2" sx={{ mt: 0.75, color: active ? 'text.primary' : 'text.secondary', fontWeight: active ? 600 : 400 }}>
              {s.name}
            </Typography>
          </Box>
        );
      })}
    </Box>
  );
}
