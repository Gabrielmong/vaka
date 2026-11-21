export type LayerName = 'static' | 'widgets';
export type ScaleMode = 'fit' | 'fill' | 'stretch';
export type Align = 'left' | 'center' | 'right';
export type ElementType = 'rect' | 'ellipse' | 'line' | 'text' | 'bar' | 'ring' | 'graph' | 'image';
export type ImageFit = 'contain' | 'cover' | 'stretch';

/** Angle: 0 = left→right, 90 = top→bottom. Bars follow their fill direction, rings their arc. */
export interface Gradient { kind?: 'linear' | 'radial'; angle?: number; colors: string[]; stops?: number[] | null }

export interface Threshold { gte: number; color: string }

/** `expr` makes it a computed alias (a formula over other keys); sensor/scale/offset are then ignored. */
export interface SensorAlias { sensor: string; name?: string | null; scale?: number; offset?: number; expr?: string | null }

export interface ElementBase {
  type: ElementType;
  id?: string | null;
  /** Editor-only: elements with the same group select and move together (may span both layers). */
  group?: string | null;
  x: number;
  y: number;
  w: number;
  h: number;
  opacity?: number;
  hidden?: boolean;
}

export interface ValueProps {
  value?: string | null;
  min?: number;
  max?: number;
  color?: string;
  thresholds?: Threshold[] | null;
}

export interface RectEl extends ElementBase { type: 'rect'; fill?: string | null; gradient?: Gradient | null; stroke?: string | null; strokeWidth?: number; radius?: number }
export interface EllipseEl extends ElementBase { type: 'ellipse'; fill?: string | null; gradient?: Gradient | null; stroke?: string | null; strokeWidth?: number }
export interface LineEl extends ElementBase { type: 'line'; x2: number; y2: number; color?: string; width?: number; roundCap?: boolean }
export interface TextEl extends ElementBase, ValueProps {
  type: 'text'; text?: string; font?: string; size?: number; weight?: number; align?: Align;
}
export interface BarEl extends ElementBase, ValueProps { type: 'bar'; track?: string | null; radius?: number; vertical?: boolean; gradient?: Gradient | null }
export interface RingEl extends ElementBase, ValueProps {
  type: 'ring'; track?: string | null; thickness?: number; startAngle?: number; sweep?: number; roundCap?: boolean; startTick?: boolean;
  gradient?: Gradient | null;
}
export interface GraphEl extends ElementBase, ValueProps {
  type: 'graph'; duration?: number; autoScale?: boolean; width?: number; fill?: string | null; gradient?: Gradient | null;
  background?: string | null; gridLines?: number; gridColor?: string;
}
export interface ImageEl extends ElementBase { type: 'image'; src: string; fit?: ImageFit; radius?: number }

export type PanelElement = RectEl | EllipseEl | LineEl | TextEl | BarEl | RingEl | GraphEl | ImageEl;

export interface Layout {
  version?: number;
  canvas: { width: number; height: number; background: string };
  scale: ScaleMode;
  aliases: Record<string, SensorAlias>;
  sensorNames: Record<string, string>;
  static: PanelElement[];
  widgets: PanelElement[];
}

/** [x, y, w, h] in canvas units. */
export type Rect = [number, number, number, number];
export interface Bounds { static: (Rect | null)[]; widgets: (Rect | null)[] }

export interface SensorInfo {
  id: string;
  name: string;
  type: string;
  unit: string;
  hardware: string;
  hardwareType: string;
  value: number | null;
}

export interface RenderResult { image: string; width: number; height: number; sensorsReady: boolean; bounds: Bounds }

/** Loose view of an element for generic property editing. */
export type Props = Record<string, unknown>;

export interface LayoutEntry { name: string; width: number; height: number; error: string | null }
export interface LayoutsInfo { layouts: LayoutEntry[]; pages: string[]; pageSeconds: number; showing: string }
export interface ImageInfo { name: string; width: number; height: number; bytes: number; thumb: string }

export interface TemplateInfo { id: string; name: string; description: string; shows: string[] }
export interface HardwareSummary { cpu: string | null; gpu: string | null; board: string | null; fans: number; drives: string[] }
export interface Detection { found: string[]; missing: string[]; summary: HardwareSummary }
