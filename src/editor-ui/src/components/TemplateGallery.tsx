import CloseIcon from '@mui/icons-material/Close';
import Alert from '@mui/material/Alert';
import Box from '@mui/material/Box';
import Card from '@mui/material/Card';
import CardActionArea from '@mui/material/CardActionArea';
import CardContent from '@mui/material/CardContent';
import Chip from '@mui/material/Chip';
import CircularProgress from '@mui/material/CircularProgress';
import Dialog from '@mui/material/Dialog';
import DialogContent from '@mui/material/DialogContent';
import DialogTitle from '@mui/material/DialogTitle';
import IconButton from '@mui/material/IconButton';
import Skeleton from '@mui/material/Skeleton';
import Stack from '@mui/material/Stack';
import Typography from '@mui/material/Typography';
import { useEffect, useState } from 'react';
import { friendlyMissing, newFromTemplate, uniqueLayoutName } from '../actions';
import { api, errorStatus, errorText } from '../api';
import { askText, setState, useEditor } from '../store';
import type { Detection, Layout, TemplateInfo } from '../types';

const PREVIEW_SCALE = 0.4;
const REFRESH_MS = 5000;
const RETRY_MS = 2000;

/** Picks a starting layout from the built-in templates, each previewed with this PC's live sensors. */
export function TemplateGallery() {
  const open = useEditor(s => s.galleryOpen);
  const [templates, setTemplates] = useState<TemplateInfo[] | null>(null);
  const [layouts, setLayouts] = useState<Record<string, Layout>>({});
  const [images, setImages] = useState<Record<string, string>>({});
  const [detection, setDetection] = useState<Detection | null>(null);
  const [scanning, setScanning] = useState(false);
  const [error, setError] = useState<string | null>(null);
  const [busy, setBusy] = useState(false);

  // Build every template for this PC once per opening (retrying while sensors load), then keep previews live.
  useEffect(() => {
    if (!open) return;
    let alive = true;
    let timer: number | undefined;
    const built: Record<string, Layout> = {};
    setError(null);

    const renderAll = async (list: TemplateInfo[]) => {
      void api.leaseFps().catch(() => {}); // keep FPS measured while the previews are on screen
      for (const t of list) {
        if (!alive) return;
        const l = built[t.id];
        if (!l) continue;
        try {
          const r = await api.render(JSON.stringify(l), PREVIEW_SCALE);
          if (alive) setImages(m => ({ ...m, [t.id]: 'data:image/png;base64,' + r.image }));
        } catch { /* keep the last frame */ }
      }
      if (alive) timer = window.setTimeout(() => void renderAll(list), REFRESH_MS);
    };

    const buildAll = async () => {
      try {
        const list = (await api.templates()).templates;
        if (!alive) return;
        setTemplates(list);
        for (const t of list) {
          const r = await api.buildTemplate(t.id);
          if (!alive) return;
          built[t.id] = r.layout;
          setDetection(d => d ?? r.detection);
        }
        setScanning(false);
        setLayouts({ ...built });
        void renderAll(list);
      } catch (e) {
        if (!alive) return;
        if (errorStatus(e) === 503) { setScanning(true); timer = window.setTimeout(() => void buildAll(), RETRY_MS); }
        else setError(errorText(e));
      }
    };

    setDetection(null);
    void buildAll();
    return () => { alive = false; window.clearTimeout(timer); };
  }, [open]);

  const close = () => setState({ galleryOpen: false });

  const pick = async (t: TemplateInfo) => {
    const name = await askText(`New layout from “${t.name}”`, {
      label: 'Name', value: uniqueLayoutName(t.name), confirmText: 'Create',
      message: 'Sensors are matched to this PC. Anything it doesn\'t have shows “--” until you pick a sensor for it.',
    });
    if (!name?.trim()) return;
    setBusy(true);
    try { if (await newFromTemplate(t.id, name)) close(); }
    finally { setBusy(false); }
  };

  return (
    <Dialog open={open} onClose={close} maxWidth="lg" fullWidth scroll="paper">
      <DialogTitle sx={{ pr: 6 }}>
        New from template
        <IconButton aria-label="Close" onClick={close} sx={{ position: 'absolute', right: 8, top: 8 }}><CloseIcon /></IconButton>
      </DialogTitle>
      <DialogContent dividers>
        <Stack spacing={1} sx={{ mb: 2 }}>
          {detection ? <DetectionSummary d={detection} /> : scanning
            ? <Stack direction="row" spacing={1} sx={{ alignItems: 'center' }}><CircularProgress size={16} /><Typography>Scanning sensors…</Typography></Stack>
            : !error && <Typography color="text.secondary">Looking at this PC…</Typography>}
          {error && <Alert severity="error">{error}</Alert>}
        </Stack>
        <Box sx={{ display: 'grid', gap: 2, gridTemplateColumns: 'repeat(auto-fill, minmax(300px, 1fr))' }}>
          {(templates ?? []).map(t => (
            <Card key={t.id} variant="outlined">
              <CardActionArea disabled={busy || !layouts[t.id]} onClick={() => void pick(t)} sx={{ height: '100%', alignItems: 'stretch' }}>
                {images[t.id]
                  ? <Box component="img" src={images[t.id]} alt={`${t.name} preview`}
                      sx={{ display: 'block', width: '100%', aspectRatio: '5 / 3', objectFit: 'contain', bgcolor: '#000' }} />
                  : <Skeleton variant="rectangular" sx={{ width: '100%', height: 'auto', aspectRatio: '5 / 3' }} />}
                <CardContent>
                  <Typography variant="subtitle1" sx={{ fontWeight: 600 }}>{t.name}</Typography>
                  <Typography variant="body2" color="text.secondary" sx={{ mb: 1 }}>{t.description}</Typography>
                  <Stack direction="row" sx={{ flexWrap: 'wrap', gap: 0.5 }}>
                    {t.shows.map(s => <Chip key={s} size="small" variant="outlined" label={s} />)}
                  </Stack>
                </CardContent>
              </CardActionArea>
            </Card>
          ))}
        </Box>
      </DialogContent>
    </Dialog>
  );
}

function DetectionSummary({ d }: { d: Detection }) {
  const s = d.summary;
  const found = [s.cpu ?? 'no CPU sensors', s.gpu ?? 'no GPU', `${s.fans} fan${s.fans === 1 ? '' : 's'}`,
    `${s.drives.length} drive${s.drives.length === 1 ? '' : 's'}`];
  return (
    <>
      <Typography><b>Found:</b> {found.join(' · ')}</Typography>
      {d.missing.length > 0 && (
        <Typography variant="body2" color="text.secondary">
          Not found: {friendlyMissing(d.missing)} — those spots will show “--”.
        </Typography>
      )}
    </>
  );
}
