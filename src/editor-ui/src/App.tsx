import Alert from '@mui/material/Alert';
import AlertTitle from '@mui/material/AlertTitle';
import Box from '@mui/material/Box';
import CircularProgress from '@mui/material/CircularProgress';
import Paper from '@mui/material/Paper';
import { useEffect } from 'react';
import { api, errorText, hasToken } from './api';
import { deleteSel, duplicateSel, loadCatalog, loadImages, loadLayout, nudge, save } from './actions';
import { PromptDialog, Toasts } from './components/Dialogs';
import { Inspector } from './components/Inspector';
import { Layers } from './components/Layers';
import { SensorDialog } from './components/SensorDialog';
import { Stage } from './components/Stage';
import { TemplateGallery } from './components/TemplateGallery';
import { TopBar } from './components/TopBar';
import { groupSelection, selectAll, selectGroup, ungroupSelection } from './groups';
import { startPreview } from './preview';
import { getState, openPicker, redo, select, selectedItems, setState, undo, useEditor } from './store';

let started = false;

async function init() {
  if (started) return;
  started = true;
  if (!hasToken()) {
    setState({ fatal: 'Open the editor from the Vaka tray icon (right-click → Open editor).' });
    return;
  }
  // Debug / screenshot helpers: #layout=GPU.json&sel=widgets:3, #group=CPU card, #sensors
  const hash = new URLSearchParams(location.hash.slice(1));
  try {
    await loadLayout(hash.get('layout') ?? undefined);
  } catch (e) {
    setState({ fatal: errorText(e) });
    return;
  }
  startPreview();

  const m = /^(static|widgets):(\d+)$/.exec(hash.get('sel') ?? '');
  if (m && getState().layout?.[m[1] as 'static' | 'widgets'][+m[2]]) select(m[1] as 'static' | 'widgets', +m[2]);
  if (hash.get('group')) selectGroup(hash.get('group')!);
  if (hash.has('sensors')) openPicker(null);
  if (hash.has('gallery')) setState({ galleryOpen: true });

  void loadImages();
  api.fonts().then(fonts => setState({ fonts })).catch(() => {});
  loadCatalog().catch(() => {}); // also wakes up full sensor scanning on the server
}

function onKeyDown(e: KeyboardEvent) {
  const mod = e.ctrlKey || e.metaKey;
  const k = e.key.toLowerCase();
  if (mod && k === 's') { e.preventDefault(); void save(); return; }
  const s = getState();
  if (s.picker || s.dialog) return;
  const t = e.target as HTMLElement;
  if (t.isContentEditable || ['INPUT', 'TEXTAREA', 'SELECT'].includes(t.tagName)) return;

  if (mod && k === 'z' && !e.shiftKey) { e.preventDefault(); undo(); }
  else if (mod && (k === 'y' || (k === 'z' && e.shiftKey))) { e.preventDefault(); redo(); }
  else if (mod && k === 'a') { e.preventDefault(); selectAll(); }
  else if (!selectedItems(s).length) return;
  else if (mod && k === 'g') { e.preventDefault(); if (e.shiftKey) ungroupSelection(); else groupSelection(); }
  else if (e.key === 'Delete' || e.key === 'Backspace') { e.preventDefault(); deleteSel(); }
  else if (mod && k === 'd') { e.preventDefault(); duplicateSel(); }
  else if (e.key === 'Escape') {
    // Escape steps out of a group to the whole group, then clears the selection.
    const g = s.sel && s.layout?.[s.sel.layer][s.sel.index]?.group;
    if (g) selectGroup(g); else select(null);
  }
  else if (e.key.startsWith('Arrow')) {
    e.preventDefault();
    const step = e.shiftKey ? 10 : 1;
    const d: Record<string, [number, number]> = { ArrowLeft: [-step, 0], ArrowRight: [step, 0], ArrowUp: [0, -step], ArrowDown: [0, step] };
    if (d[e.key]) nudge(...d[e.key]);
  }
}

export function App() {
  const fatal = useEditor(s => s.fatal);
  const ready = useEditor(s => !!s.layout);

  useEffect(() => {
    void init();
    const beforeUnload = (e: BeforeUnloadEvent) => { if (getState().dirty) e.preventDefault(); };
    window.addEventListener('keydown', onKeyDown);
    window.addEventListener('beforeunload', beforeUnload);
    return () => {
      window.removeEventListener('keydown', onKeyDown);
      window.removeEventListener('beforeunload', beforeUnload);
    };
  }, []);

  if (fatal)
    return (
      <Box sx={{ height: '100vh', display: 'grid', placeItems: 'center', p: 3 }}>
        <Alert severity="error" variant="outlined" sx={{ maxWidth: 520 }}>
          <AlertTitle>Can't open the editor</AlertTitle>
          {fatal}
        </Alert>
      </Box>
    );

  return (
    <Box sx={{ height: '100vh', display: 'flex', flexDirection: 'column', overflow: 'hidden' }}>
      <TopBar />
      {ready ? (
        <Box sx={{ flex: 1, minHeight: 0, display: 'grid', gridTemplateColumns: '260px minmax(0, 1fr) 340px' }}>
          <Paper square elevation={0} sx={{ borderRight: 1, borderColor: 'divider', display: 'flex', flexDirection: 'column', minHeight: 0 }}>
            <Layers />
          </Paper>
          <Stage />
          <Paper square elevation={0} sx={{ borderLeft: 1, borderColor: 'divider', display: 'flex', flexDirection: 'column', minHeight: 0 }}>
            <Inspector />
          </Paper>
        </Box>
      ) : (
        <Box sx={{ flex: 1, display: 'grid', placeItems: 'center' }}><CircularProgress /></Box>
      )}
      <SensorDialog />
      <TemplateGallery />
      <PromptDialog />
      <Toasts />
    </Box>
  );
}
