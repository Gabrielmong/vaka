import ArrowDownwardIcon from '@mui/icons-material/ArrowDownward';
import ArrowUpwardIcon from '@mui/icons-material/ArrowUpward';
import VisibilityIcon from '@mui/icons-material/Visibility';
import VisibilityOffIcon from '@mui/icons-material/VisibilityOff';
import WorkspacesOutlinedIcon from '@mui/icons-material/WorkspacesOutlined';
import LinkOffIcon from '@mui/icons-material/LinkOff';
import Box from '@mui/material/Box';
import IconButton from '@mui/material/IconButton';
import List from '@mui/material/List';
import ListItemButton from '@mui/material/ListItemButton';
import ListItemIcon from '@mui/material/ListItemIcon';
import ListItemText from '@mui/material/ListItemText';
import ListSubheader from '@mui/material/ListSubheader';
import Stack from '@mui/material/Stack';
import Typography from '@mui/material/Typography';
import { memo, useMemo } from 'react';
import { reorder, toggleHidden } from '../actions';
import { describe } from '../elements';
import { groupMembers, groupsOf, selectGroup, selectionGroup, ungroupSelection } from '../groups';
import { getState, isSelected, select, selectedItems, selectItems, setState, useEditor } from '../store';
import type { LayerName, PanelElement } from '../types';
import { ElementIcon } from './icons';

const Row = memo(function Row({ el, layer, index, selected, hovered }: {
  el: PanelElement; layer: LayerName; index: number; selected: boolean; hovered: boolean;
}) {
  const label = describe(el);
  const stop = (fn: () => void) => (e: React.MouseEvent) => { e.stopPropagation(); fn(); };
  const onClick = (e: React.MouseEvent) => {
    if (!(e.ctrlKey || e.metaKey || e.shiftKey)) { select(layer, index); return; }
    // Ctrl/Shift+click adds or removes just this element (the list always works per element).
    const cur = selectedItems(getState());
    const has = cur.some(it => it.layer === layer && it.index === index);
    selectItems(has ? cur.filter(it => !(it.layer === layer && it.index === index)) : [...cur, { layer, index }]);
  };
  return (
    <ListItemButton
      dense
      selected={selected}
      onClick={onClick}
      onMouseEnter={() => setState({ hover: { layer, index } })}
      onMouseLeave={() => setState({ hover: null })}
      sx={{
        py: 0.25, pr: 0.5, mx: 0.5,
        outline: hovered && !selected ? '1px dashed' : 'none', outlineColor: 'primary.dark',
        '& .row-actions': { opacity: 0 }, '&:hover .row-actions, &.Mui-selected .row-actions': { opacity: 1 },
      }}
    >
      <ListItemIcon sx={{ minWidth: 30 }}>
        <ElementIcon el={el} layer={layer} fontSize="small" color={selected ? 'primary' : 'action'} />
      </ListItemIcon>
      <ListItemText
        primary={label}
        secondary={el.group || undefined}
        slotProps={{
          primary: { noWrap: true, title: label, sx: { fontSize: 13, opacity: el.hidden ? 0.4 : 1, textDecoration: el.hidden ? 'line-through' : 'none' } },
          secondary: { noWrap: true, sx: { fontSize: 11, lineHeight: 1.2, color: 'primary.light', opacity: 0.8 } },
        }}
      />
      <Stack direction="row" className="row-actions" sx={{ transition: 'opacity .15s' }}>
        <IconButton title="Bring forward" onClick={stop(() => reorder(layer, index, 1))}><ArrowUpwardIcon sx={{ fontSize: 16 }} /></IconButton>
        <IconButton title="Send backward" onClick={stop(() => reorder(layer, index, -1))}><ArrowDownwardIcon sx={{ fontSize: 16 }} /></IconButton>
      </Stack>
      <IconButton title={el.hidden ? 'Show' : 'Hide'} onClick={stop(() => toggleHidden(layer, index))}>
        {el.hidden ? <VisibilityOffIcon sx={{ fontSize: 16 }} /> : <VisibilityIcon sx={{ fontSize: 16, opacity: 0.6 }} />}
      </IconButton>
    </ListItemButton>
  );
});

function Group({ layer, title, subtitle }: { layer: LayerName; title: string; subtitle: string }) {
  const items = useEditor(s => s.layout?.[layer]);
  useEditor(s => s.sel);
  useEditor(s => s.multi);
  const hover = useEditor(s => s.hover);
  const st = getState();
  if (!items) return null;
  return (
    <List dense disablePadding
      subheader={
        <ListSubheader sx={{ lineHeight: 1, py: 1.25, bgcolor: 'background.paper', display: 'flex', justifyContent: 'space-between', alignItems: 'baseline' }}>
          <Typography variant="overline" sx={{ lineHeight: 1 }}>{title}</Typography>
          <Typography variant="caption" color="text.disabled">{subtitle}</Typography>
        </ListSubheader>
      }>
      {!items.length && <Typography variant="body2" color="text.disabled" sx={{ px: 2.5, py: 0.5, fontStyle: 'italic' }}>Empty</Typography>}
      {/* Top-most (drawn last) first */}
      {items.map((_, k) => {
        const i = items.length - 1 - k;
        return (
          <Row key={i} el={items[i]} layer={layer} index={i}
            selected={isSelected(st, layer, i)}
            hovered={hover?.layer === layer && hover.index === i} />
        );
      })}
    </List>
  );
}

/** Groups (cards etc.): click to select the whole group, which then moves as one. */
function Groups() {
  const layout = useEditor(s => s.layout);
  const multi = useEditor(s => s.multi);
  const groups = useMemo(() => (layout ? groupsOf(layout) : []), [layout]);
  if (!layout || !groups.length) return null;
  const current = selectionGroup(layout, multi);
  return (
    <List dense disablePadding
      subheader={
        <ListSubheader sx={{ lineHeight: 1, py: 1.25, bgcolor: 'background.paper', display: 'flex', justifyContent: 'space-between', alignItems: 'baseline' }}>
          <Typography variant="overline" sx={{ lineHeight: 1 }}>Groups</Typography>
          <Typography variant="caption" color="text.disabled">select &amp; move together</Typography>
        </ListSubheader>
      }>
      {groups.map(g => (
        <ListItemButton key={g.name} dense selected={current === g.name} onClick={() => selectGroup(g.name)}
          onMouseEnter={() => setState({ hover: g.items[0] })}
          onMouseLeave={() => setState({ hover: null })}
          sx={{ py: 0.25, pr: 0.5, mx: 0.5, '& .row-actions': { opacity: 0 }, '&:hover .row-actions, &.Mui-selected .row-actions': { opacity: 1 } }}>
          <ListItemIcon sx={{ minWidth: 30 }}>
            <WorkspacesOutlinedIcon fontSize="small" color={current === g.name ? 'primary' : 'action'} />
          </ListItemIcon>
          <ListItemText primary={g.name} slotProps={{ primary: { noWrap: true, title: g.name, sx: { fontSize: 13 } } }} />
          <Typography variant="caption" color="text.disabled" sx={{ mr: 0.5 }}>{g.items.length}</Typography>
          <IconButton className="row-actions" title="Ungroup" onClick={e => {
            e.stopPropagation();
            selectItems(groupMembers(layout, g.name));
            ungroupSelection();
          }}><LinkOffIcon sx={{ fontSize: 16 }} /></IconButton>
        </ListItemButton>
      ))}
    </List>
  );
}

export function Layers() {
  return (
    <Box sx={{ overflow: 'auto', pb: 2 }}>
      <Groups />
      <Group layer="widgets" title="Live widgets" subtitle="redrawn every second" />
      <Group layer="static" title="Background" subtitle="drawn once, cached" />
    </Box>
  );
}
