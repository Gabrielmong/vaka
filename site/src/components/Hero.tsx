import DownloadIcon from '@mui/icons-material/Download';
import GitHubIcon from '@mui/icons-material/GitHub';
import Box from '@mui/material/Box';
import Button from '@mui/material/Button';
import Link from '@mui/material/Link';
import Stack from '@mui/material/Stack';
import Typography from '@mui/material/Typography';
import useMediaQuery from '@mui/material/useMediaQuery';
import { useEffect, useState } from 'react';
import { initialDownloadUrl, licenseUrl, repoUrl, resolveDownload } from '../links';
import type { Manifest } from '../manifest';
import { colors } from '../theme';
import { PanelPlayer } from './PanelPlayer';
import { StateTimeline } from './StateTimeline';

export function Hero({ manifest, failed, templateId }: { manifest: Manifest | null; failed: boolean; templateId: string | null }) {
  const [download, setDownload] = useState(initialDownloadUrl);
  const [frame, setFrame] = useState(0);
  const reduce = useMediaQuery('(prefers-reduced-motion: reduce)', { noSsr: true });
  useEffect(() => { void resolveDownload().then(setDownload); }, []);

  const name = manifest?.templates.find(t => t.id === templateId)?.name;

  return (
    <Box component="header" sx={{ maxWidth: 1180, mx: 'auto', px: { xs: 2, sm: 4 }, pt: { xs: 3, md: 4 }, pb: { xs: 6, md: 9 } }}>
      <Stack direction="row" sx={{ alignItems: 'center', justifyContent: 'space-between', mb: { xs: 5, md: 8 } }}>
        <Stack direction="row" spacing={1.25} sx={{ alignItems: 'center' }}>
          <Box component="img" src="./favicon.svg" alt="" sx={{ width: 40, height: 40, display: 'block' }} />
          <Typography component="span" variant="h3">Vaka</Typography>
        </Stack>
        <Link href={repoUrl} underline="hover" color="text.secondary" sx={{ display: 'inline-flex', alignItems: 'center', gap: 0.75 }}>
          <GitHubIcon fontSize="small" />Source
        </Link>
      </Stack>

      <Box sx={{ display: 'grid', gap: { xs: 6, md: 8 }, alignItems: 'center', gridTemplateColumns: { xs: '1fr', md: '5fr 7fr' } }}>
        <Box>
          <Typography variant="h1" component="h1" sx={{ mb: 3 }}>
            Your PC’s vitals on the little screen beside your monitor
          </Typography>
          <Typography color="text.secondary" sx={{ fontSize: '1.1rem', maxWidth: '44ch', mb: 4 }}>
            Temperatures, loads, clocks, fans, drives and FPS on a small display that stays out of your way.
            It finds your hardware on its own and comes with twelve layouts you can change in the browser.
          </Typography>
          <Stack direction={{ xs: 'column', sm: 'row' }} spacing={1.5} sx={{ mb: 2 }}>
            <Button variant="contained" size="large" href={download} startIcon={<DownloadIcon />}>Download for Windows</Button>
            <Button variant="outlined" size="large" color="inherit" href={repoUrl} startIcon={<GitHubIcon />}
              sx={{ borderColor: colors.line }}>View on GitHub</Button>
          </Stack>
          <Typography variant="body2" color="text.secondary">
            Windows 10 and 11, 64-bit. Free and open source (<Link href={licenseUrl} color="inherit">MIT</Link>).
          </Typography>
        </Box>

        <Box>
          {manifest && templateId ? (
            <>
              <PanelPlayer key={templateId} manifest={manifest} templateId={templateId} onFrame={setFrame} />
              <Box sx={{ mt: 3, mx: { md: 4 } }}>
                <Typography variant="body2" color="text.secondary" sx={{ mb: 1.25 }}>
                  {name} on simulated sensors{reduce ? '' : ', cycling through three workloads'}
                </Typography>
                <StateTimeline manifest={manifest} frame={frame} still={reduce} />
              </Box>
            </>
          ) : (
            <Box sx={{ aspectRatio: '5 / 3', borderRadius: '14px', border: `1px dashed ${colors.line}`, display: 'grid', placeItems: 'center', p: 3 }}>
              <Typography color="text.secondary" sx={{ textAlign: 'center' }}>
                {failed ? 'Panel preview not built. Run npm run frames in site/ to render it.' : 'Loading the panel…'}
              </Typography>
            </Box>
          )}
        </Box>
      </Box>
    </Box>
  );
}
