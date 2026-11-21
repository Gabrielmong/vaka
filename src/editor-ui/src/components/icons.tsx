import CircleOutlined from '@mui/icons-material/CircleOutlined';
import CropSquare from '@mui/icons-material/CropSquare';
import DonutLarge from '@mui/icons-material/DonutLarge';
import HorizontalRule from '@mui/icons-material/HorizontalRule';
import ImageOutlined from '@mui/icons-material/ImageOutlined';
import LinearScale from '@mui/icons-material/LinearScale';
import Schedule from '@mui/icons-material/Schedule';
import ShowChart from '@mui/icons-material/ShowChart';
import Sensors from '@mui/icons-material/Sensors';
import TextFields from '@mui/icons-material/TextFields';
import type { SvgIconProps } from '@mui/material/SvgIcon';
import type { AddKind } from '../elements';
import type { LayerName, PanelElement } from '../types';

export const KIND_ICONS: Record<AddKind, (p: SvgIconProps) => React.ReactNode> = {
  rect: p => <CropSquare {...p} />,
  ellipse: p => <CircleOutlined {...p} />,
  line: p => <HorizontalRule {...p} />,
  label: p => <TextFields {...p} />,
  value: p => <Sensors {...p} />,
  clock: p => <Schedule {...p} />,
  bar: p => <LinearScale {...p} />,
  ring: p => <DonutLarge {...p} />,
  graph: p => <ShowChart {...p} />,
  image: p => <ImageOutlined {...p} />,
};

export function ElementIcon({ el, layer, ...p }: { el: PanelElement; layer: LayerName } & SvgIconProps) {
  switch (el.type) {
    case 'rect': return KIND_ICONS.rect(p);
    case 'ellipse': return KIND_ICONS.ellipse(p);
    case 'line': return KIND_ICONS.line(p);
    case 'bar': return KIND_ICONS.bar(p);
    case 'ring': return KIND_ICONS.ring(p);
    case 'graph': return KIND_ICONS.graph(p);
    case 'image': return KIND_ICONS.image(p);
    case 'text':
      if (layer === 'static' || !el.text?.includes('{')) return KIND_ICONS.label(p);
      return /\{(time|date)\b/.test(el.text) ? KIND_ICONS.clock(p) : KIND_ICONS.value(p);
  }
}

export function elementTitle(el: PanelElement, layer: LayerName) {
  if (el.type === 'text') return layer === 'static' ? 'Label' : 'Text';
  return { rect: 'Rectangle', ellipse: 'Ellipse', line: 'Line', bar: 'Bar', ring: 'Ring gauge', graph: 'Graph', image: 'Image' }[el.type];
}
