import Box from '@mui/material/Box';
import { useEffect, useRef, useState } from 'react';
import { Features } from './components/Features';
import { Gallery } from './components/Gallery';
import { Hero } from './components/Hero';
import { InstallSteps } from './components/InstallSteps';
import { SiteFooter } from './components/SiteFooter';
import { loadManifest, type Manifest } from './manifest';

export function App() {
  const [manifest, setManifest] = useState<Manifest | null>(null);
  const [failed, setFailed] = useState(false);
  const [templateId, setTemplateId] = useState<string | null>(null);
  const heroRef = useRef<HTMLDivElement>(null);

  useEffect(() => {
    loadManifest().then(m => { setManifest(m); setTemplateId(id => id ?? m.hero); }, () => setFailed(true));
  }, []);

  const play = (id: string) => {
    setTemplateId(id);
    const reduce = window.matchMedia('(prefers-reduced-motion: reduce)').matches;
    heroRef.current?.scrollIntoView({ behavior: reduce ? 'auto' : 'smooth', block: 'start' });
  };

  return (
    <Box component="main" sx={{ overflowX: 'clip' }}>
      <Box ref={heroRef}><Hero manifest={manifest} failed={failed} templateId={templateId} /></Box>
      {manifest && <Gallery manifest={manifest} playing={templateId} onPlay={play} />}
      <Features />
      <InstallSteps />
      <SiteFooter />
    </Box>
  );
}
