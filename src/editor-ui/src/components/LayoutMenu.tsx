import AddIcon from '@mui/icons-material/Add';
import DashboardCustomizeIcon from '@mui/icons-material/DashboardCustomizeOutlined';
import ArrowDropDownIcon from '@mui/icons-material/ArrowDropDown';
import ContentCopyIcon from '@mui/icons-material/ContentCopy';
import DeleteOutlineIcon from '@mui/icons-material/DeleteOutlined';
import DriveFileRenameOutlineIcon from '@mui/icons-material/DriveFileRenameOutline';
import Button from '@mui/material/Button';
import Chip from '@mui/material/Chip';
import Divider from '@mui/material/Divider';
import ListItemIcon from '@mui/material/ListItemIcon';
import ListItemText from '@mui/material/ListItemText';
import ListSubheader from '@mui/material/ListSubheader';
import Menu from '@mui/material/Menu';
import MenuItem from '@mui/material/MenuItem';
import { useState } from 'react';
import { deleteLayout, loadLayouts, newLayout, openLayout, renameLayout, stem } from '../actions';
import { setState, useEditor } from '../store';

const same = (a: string, b: string) => a.toLowerCase() === b.toLowerCase();

/** Shows which layout file is being edited and switches / creates / renames / deletes layouts. */
export function LayoutMenu() {
  const [anchor, setAnchor] = useState<HTMLElement | null>(null);
  const name = useEditor(s => s.layoutName);
  const info = useEditor(s => s.layouts);
  const close = () => setAnchor(null);
  const run = (fn: () => void) => () => { close(); fn(); };
  if (!name) return null;

  return (
    <>
      <Button color="inherit" endIcon={<ArrowDropDownIcon />} title="Switch layout"
        onClick={e => { setAnchor(e.currentTarget); void loadLayouts().catch(() => {}); }}
        sx={{ fontWeight: 600, maxWidth: 260, '& .MuiButton-endIcon': { ml: 0.25 } }}>
        <span style={{ overflow: 'hidden', textOverflow: 'ellipsis', whiteSpace: 'nowrap' }}>{stem(name)}</span>
      </Button>
      <Menu anchorEl={anchor} open={!!anchor} onClose={close} slotProps={{ list: { dense: true, sx: { minWidth: 300 } } }}>
        <ListSubheader>Layouts</ListSubheader>
        {info?.layouts.map(l => {
          const page = info.pages.findIndex(p => same(p, l.name));
          return (
            <MenuItem key={l.name} selected={same(l.name, name)} onClick={run(() => void openLayout(l.name))}>
              <ListItemText primary={stem(l.name)}
                secondary={l.error ? `Can't load: ${l.error}` : `${l.width}×${l.height}`}
                slotProps={{ secondary: { color: l.error ? 'error' : undefined, noWrap: true } }} />
              {same(l.name, info.showing)
                ? <Chip size="small" color="success" label="on panel" sx={{ ml: 1 }} />
                : page >= 0 && <Chip size="small" variant="outlined" label={`page ${page + 1}`} sx={{ ml: 1 }} />}
            </MenuItem>
          );
        })}
        <Divider />
        <MenuItem onClick={run(() => setState({ galleryOpen: true }))}>
          <ListItemIcon><DashboardCustomizeIcon fontSize="small" /></ListItemIcon>New from template…
        </MenuItem>
        <MenuItem onClick={run(() => void newLayout())}>
          <ListItemIcon><AddIcon fontSize="small" /></ListItemIcon>New layout…
        </MenuItem>
        <MenuItem onClick={run(() => void newLayout(name))}>
          <ListItemIcon><ContentCopyIcon fontSize="small" /></ListItemIcon>Duplicate “{stem(name)}”…
        </MenuItem>
        <MenuItem onClick={run(() => void renameLayout(name))}>
          <ListItemIcon><DriveFileRenameOutlineIcon fontSize="small" /></ListItemIcon>Rename…
        </MenuItem>
        <MenuItem onClick={run(() => void deleteLayout(name))} disabled={(info?.layouts.length ?? 0) <= 1}>
          <ListItemIcon><DeleteOutlineIcon fontSize="small" color="error" /></ListItemIcon>Delete…
        </MenuItem>
      </Menu>
    </>
  );
}
