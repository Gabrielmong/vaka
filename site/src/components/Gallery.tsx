import Box from '@mui/material/Box';
import ButtonBase from '@mui/material/ButtonBase';
import Typography from '@mui/material/Typography';
import useMediaQuery from '@mui/material/useMediaQuery';
import { useEffect, useState } from 'react';
import type { Manifest } from '../manifest';
import { stillUrl } from '../player';
import { colors } from '../theme';
import { Section } from './Section';

const STEP_S = 3;

/** All templates; each card crossfades its three states, staggered so the grid doesn't change in unison. */
export function Gallery({ manifest, playing, onPlay }: { manifest: Manifest; playing: string | null; onPlay: (id: string) => void }) {
  const reduce = useMediaQuery('(prefers-reduced-motion: reduce)', { noSsr: true });
  const [tick, setTick] = useState(0);
  useEffect(() => {
    if (reduce) return;
    const t = window.setInterval(() => setTick(n => n + 1), 1000);
    return () => window.clearInterval(t);
  }, [reduce]);

  const states = manifest.states.map(s => s.name);
  return (
    <Section id="templates" title={`${manifest.templates.length} layouts to start from`}
      lead="Each one is matched to your hardware when you pick it, and every part can be moved, restyled or removed in the editor. Choose one to play it above.">
      <Box sx={{ display: 'grid', gap: { xs: 4, md: 5 }, gridTemplateColumns: 'repeat(auto-fill, minmax(min(100%, 300px), 1fr))' }}>
        {manifest.templates.map((t, i) => {
          const shown = reduce ? 0 : Math.floor((tick + i * 1.3) / STEP_S) % states.length;
          const active = t.id === playing;
          return (
            <ButtonBase key={t.id} onClick={() => onPlay(t.id)} aria-pressed={active}
              sx={{ display: 'block', textAlign: 'left', borderRadius: '8px',
                    '&:hover .shot': { outlineColor: active ? colors.cpu : colors.muted },
                    '&.Mui-focusVisible': { outline: `2px solid ${colors.cpu}`, outlineOffset: 6 } }}>
              <Box className="shot" sx={{ position: 'relative', aspectRatio: `${manifest.size.w} / ${manifest.size.h}`, borderRadius: '6px',
                                          overflow: 'hidden', bgcolor: '#000', outline: `2px solid ${active ? colors.cpu : 'transparent'}`,
                                          outlineOffset: 3, transition: 'outline-color 150ms' }}>
                {states.map((s, k) => (
                  <Box key={s} component="img" src={stillUrl(t.id, s)} alt={k === 0 ? `${t.name} layout` : ''}
                    loading={k === 0 ? 'eager' : 'lazy'} aria-hidden={k !== 0 || undefined}
                    width={manifest.size.w} height={manifest.size.h}
                    sx={{ position: 'absolute', inset: 0, width: '100%', height: '100%', objectFit: 'cover',
                          opacity: k === shown ? 1 : 0, transition: 'opacity 900ms ease' }} />
                ))}
              </Box>
              <Box sx={{ pt: 1.75 }}>
                <Typography variant="h3" component="h3" sx={{ mb: 0.5 }}>
                  {t.name}
                  {active && <Typography component="span" variant="body2" color="primary" sx={{ ml: 1 }}>playing above</Typography>}
                </Typography>
                <Typography variant="body2" color="text.secondary">{t.description}</Typography>
              </Box>
            </ButtonBase>
          );
        })}
      </Box>
    </Section>
  );
}
