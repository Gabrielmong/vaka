import AddIcon from '@mui/icons-material/Add';
import FileDownloadIcon from '@mui/icons-material/FileDownload';
import FileUploadIcon from '@mui/icons-material/FileUpload';
import GridOnIcon from '@mui/icons-material/GridOn';
import RedoIcon from '@mui/icons-material/Redo';
import RestoreIcon from '@mui/icons-material/Restore';
import SaveIcon from '@mui/icons-material/Save';
import UndoIcon from '@mui/icons-material/Undo';
import WallpaperIcon from '@mui/icons-material/Wallpaper';
import AppBar from '@mui/material/AppBar';
import Box from '@mui/material/Box';
import Button from '@mui/material/Button';
import Chip from '@mui/material/Chip';
import Divider from '@mui/material/Divider';
import IconButton from '@mui/material/IconButton';
import InputAdornment from '@mui/material/InputAdornment';
import ListItemIcon from '@mui/material/ListItemIcon';
import ListItemText from '@mui/material/ListItemText';
import ListSubheader from '@mui/material/ListSubheader';
import Menu from '@mui/material/Menu';
import MenuItem from '@mui/material/MenuItem';
import Stack from '@mui/material/Stack';
import TextField from '@mui/material/TextField';
import ToggleButton from '@mui/material/ToggleButton';
import Toolbar from '@mui/material/Toolbar';
import Tooltip from '@mui/material/Tooltip';
import Typography from '@mui/material/Typography';
import { useRef, useState } from 'react';
import { addElement, exportBackground, exportLayout, importLayout, revert, save } from '../actions';
import { ADD_KINDS, type AddKind } from '../elements';
import { redo, setState, undo, useEditor } from '../store';
import { KIND_ICONS } from './icons';
import { LayoutMenu } from './LayoutMenu';

const ZOOMS: ['fit' | number, string][] = [['fit', 'Fit'], [0.5, '50%'], [0.75, '75%'], [1, '100%'], [1.5, '150%'], [2, '200%']];

function AddMenu() {
  const [anchor, setAnchor] = useState<HTMLElement | null>(null);
  const item = (k: AddKind) => {
    const Icon = KIND_ICONS[k];
    return (
      <MenuItem key={k} onClick={() => { setAnchor(null); addElement(k); }}>
        <ListItemIcon><Icon fontSize="small" /></ListItemIcon>
        <ListItemText primary={ADD_KINDS[k].label} secondary={ADD_KINDS[k].description} />
      </MenuItem>
    );
  };
  return (
    <>
      <Button variant="contained" startIcon={<AddIcon />} onClick={e => setAnchor(e.currentTarget)}>Add</Button>
      <Menu anchorEl={anchor} open={!!anchor} onClose={() => setAnchor(null)}
        slotProps={{ list: { dense: true, sx: { minWidth: 280 } } }}>
        <ListSubheader>Live — redrawn every second</ListSubheader>
        {(['value', 'clock', 'bar', 'ring', 'graph'] as const).map(item)}
        <ListSubheader>Background — drawn once, cached</ListSubheader>
        {(['rect', 'ellipse', 'line', 'label', 'image'] as const).map(item)}
      </Menu>
    </>
  );
}

function Status() {
  const dirty = useEditor(s => s.dirty);
  const renderError = useEditor(s => s.renderError);
  if (renderError)
    return <Tooltip title={renderError}><Chip size="small" color="error" variant="outlined" label="Preview error" /></Tooltip>;
  return dirty
    ? <Chip size="small" color="warning" variant="outlined" label="Unsaved changes" />
    : <Chip size="small" color="success" variant="outlined" label="Saved" />;
}

export function TopBar() {
  const canUndo = useEditor(s => s.undo.length > 0);
  const canRedo = useEditor(s => s.redo.length > 0);
  const dirty = useEditor(s => s.dirty);
  const zoomMode = useEditor(s => s.zoomMode);
  const grid = useEditor(s => s.grid);
  const showGrid = useEditor(s => s.showGrid);
  const fileRef = useRef<HTMLInputElement>(null);

  return (
    <AppBar position="static" color="default" elevation={0} sx={{ borderBottom: 1, borderColor: 'divider', bgcolor: 'background.paper' }}>
      <Toolbar variant="dense" sx={{ gap: 1.5 }}>
        <Stack direction="row" spacing={1} sx={{ alignItems: 'center', mr: 1 }}>
          <Box sx={{ width: 18, height: 18, borderRadius: '50%', border: '3px solid', borderColor: 'primary.main', borderBottomColor: 'transparent', transform: 'rotate(40deg)' }} />
          <Typography variant="subtitle1" sx={{ fontWeight: 600, color: 'text.secondary' }}>Sensor Panel</Typography>
          <Typography color="text.disabled">/</Typography>
          <LayoutMenu />
        </Stack>

        <AddMenu />

        <Divider orientation="vertical" flexItem />
        <Tooltip title="Undo (Ctrl+Z)"><span><IconButton disabled={!canUndo} onClick={undo}><UndoIcon fontSize="small" /></IconButton></span></Tooltip>
        <Tooltip title="Redo (Ctrl+Y)"><span><IconButton disabled={!canRedo} onClick={redo}><RedoIcon fontSize="small" /></IconButton></span></Tooltip>
        <Divider orientation="vertical" flexItem />

        <TextField select size="small" value={String(zoomMode)} sx={{ width: 130 }}
          onChange={e => setState({ zoomMode: e.target.value === 'fit' ? 'fit' : parseFloat(e.target.value) })}
          slotProps={{ input: { startAdornment: <InputAdornment position="start">Zoom</InputAdornment> } }}>
          {ZOOMS.map(([v, l]) => <MenuItem key={String(v)} value={String(v)}>{l}</MenuItem>)}
        </TextField>
        <TextField select size="small" value={grid} sx={{ width: 124 }}
          onChange={e => setState({ grid: Number(e.target.value) })}
          slotProps={{ input: { startAdornment: <InputAdornment position="start">Snap</InputAdornment> } }}>
          {[1, 5, 10, 20].map(g => <MenuItem key={g} value={g}>{g === 1 ? 'Off' : `${g}px`}</MenuItem>)}
        </TextField>
        <Tooltip title="Show grid (hold Alt while dragging to ignore snapping)">
          <ToggleButton value="grid" selected={showGrid} onChange={() => setState({ showGrid: !showGrid })}>
            <GridOnIcon fontSize="small" />
          </ToggleButton>
        </Tooltip>

        <Box sx={{ flex: 1 }} />
        <Status />

        <Tooltip title="Load a layout JSON file"><IconButton onClick={() => fileRef.current?.click()}><FileUploadIcon fontSize="small" /></IconButton></Tooltip>
        <Tooltip title="Download the background (canvas + background elements) as a PNG">
          <IconButton onClick={() => void exportBackground()}><WallpaperIcon fontSize="small" /></IconButton>
        </Tooltip>
        <Tooltip title="Download the layout as JSON"><IconButton onClick={exportLayout}><FileDownloadIcon fontSize="small" /></IconButton></Tooltip>
        <Tooltip title="Discard changes"><span><IconButton disabled={!dirty} onClick={() => void revert()}><RestoreIcon fontSize="small" /></IconButton></span></Tooltip>
        <Button variant="contained" startIcon={<SaveIcon />} disabled={!dirty} onClick={() => void save()}>Save</Button>
        <input ref={fileRef} type="file" accept=".json,application/json" hidden onChange={e => {
          const f = e.target.files?.[0];
          if (f) void importLayout(f);
          e.target.value = '';
        }} />
      </Toolbar>
    </AppBar>
  );
}
