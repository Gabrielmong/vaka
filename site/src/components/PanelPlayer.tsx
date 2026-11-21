import Box from '@mui/material/Box';
import useMediaQuery from '@mui/material/useMediaQuery';
import { useEffect, useRef, useState } from 'react';
import type { Manifest } from '../manifest';
import { FrameCache, frameUrl, nextFrame, preloadOrder, stateAt, stillUrl } from '../player';
import { colors } from '../theme';

const AHEAD = 5;

/**
 * Plays a template's one-frame-per-second recording inside a monitor bezel. Frames are decoded ahead of the
 * playhead; when the next one isn't ready yet the current frame stays up. Reduced motion shows the idle still.
 */
export function PanelPlayer({ manifest, templateId, onFrame }: {
  manifest: Manifest; templateId: string; onFrame?: (frame: number) => void;
}) {
  const reduce = useMediaQuery('(prefers-reduced-motion: reduce)', { noSsr: true });
  const template = manifest.templates.find(t => t.id === templateId) ?? manifest.templates[0];
  const total = template.frames;
  // Remounted per template (key in Hero), so frame 0 and an empty cache are the starting point.
  const [frame, setFrame] = useState(0);
  const [, setLoaded] = useState(0); // re-render when a frame finishes decoding
  const cache = useRef<FrameCache | null>(null);
  cache.current ??= new FrameCache(undefined, () => setLoaded(n => n + 1));

  useEffect(() => {
    onFrame?.(frame);
    if (reduce) return;
    for (const f of [frame, ...preloadOrder(frame, total, AHEAD)]) cache.current!.request(frameUrl(template.id, f));
  }, [frame, template.id, total, reduce, onFrame]);

  useEffect(() => {
    if (reduce) return;
    const timer = window.setInterval(() => {
      setFrame(f => {
        const next = nextFrame(f, total, n => cache.current!.isReady(frameUrl(template.id, n)));
        // Held because the next frame isn't ready: ask again (a failed load restarts; one in flight is left alone).
        if (next === f) cache.current!.request(frameUrl(template.id, (f + 1) % total));
        return next;
      });
    }, 1000);
    return () => window.clearInterval(timer);
  }, [template.id, total, reduce]);

  // Until the current frame has decoded, show the idle still (the gallery loads it first).
  const current = frameUrl(template.id, frame);
  const src = reduce || !cache.current.isReady(current) ? stillUrl(template.id, 'Idle') : current;
  const state = reduce ? 'Idle' : stateAt(manifest, frame);

  return (
    <Box sx={{ position: 'relative' }}>
      {/* The panel's own light on the desk: a blurred copy of the frame behind the bezel. */}
      <Box component="img" src={src} alt="" aria-hidden
        sx={{ position: 'absolute', left: '4%', top: '6%', width: '92%', height: '90%', objectFit: 'cover',
              filter: 'blur(44px) saturate(1.8)', opacity: 0.7, pointerEvents: 'none' }} />
      <Box sx={{ position: 'relative', p: { xs: 1, sm: 1.5 }, pb: { xs: 2.5, sm: 3.5 }, borderRadius: '14px',
                 background: 'linear-gradient(180deg, #232c37 0%, #161c24 100%)',
                 boxShadow: '0 1px 0 rgba(255,255,255,0.08) inset, 0 30px 60px -20px rgba(0,0,0,0.7)' }}>
        <Box component="img" src={src} width={manifest.size.w} height={manifest.size.h}
          alt={`${template.name} layout on a PC that is ${state.toLowerCase()}`}
          sx={{ display: 'block', width: '100%', height: 'auto', borderRadius: '4px', bgcolor: '#000' }} />
        <Box aria-hidden sx={{ position: 'absolute', left: '50%', bottom: { xs: 8, sm: 12 }, width: 6, height: 6, ml: '-3px',
                               borderRadius: '50%', bgcolor: colors.gpu, opacity: 0.7 }} />
      </Box>
      <Box aria-hidden sx={{ position: 'relative', mx: 'auto', width: '16%', height: 18, background: 'linear-gradient(#1b222b, #121820)' }} />
      <Box aria-hidden sx={{ position: 'relative', mx: 'auto', width: '32%', height: 8, borderRadius: '8px 8px 2px 2px', bgcolor: '#1b222b' }} />
    </Box>
  );
}
