import type { Manifest } from './manifest';

export function stateAt(m: Manifest, frame: number): string {
  const second = frame % m.loopSeconds;
  return (m.states.find(s => second >= s.from && second < s.to) ?? m.states[0]).name;
}

export const frameUrl = (id: string, frame: number) => `./panel/${id}/${String(frame).padStart(2, '0')}.webp`;

export const stillUrl = (id: string, state: string) => `./panel/${id}/${state.toLowerCase()}.webp`;

/** The next `ahead` frames after `current`, wrapping at the end of the loop. */
export const preloadOrder = (current: number, total: number, ahead: number) =>
  Array.from({ length: Math.min(ahead, total - 1) }, (_, i) => (current + 1 + i) % total);

/** Advance only when the next frame has decoded, so a slow connection holds a frame instead of flashing blank. */
export function nextFrame(current: number, total: number, isReady: (frame: number) => boolean): number {
  const next = (current + 1) % total;
  return isReady(next) ? next : current;
}

/**
 * Decodes frames ahead of the playhead. A request for a frame that is loading is ignored; one whose load failed
 * starts again, so a dropped request on a flaky connection is retried on the next tick instead of freezing the loop.
 */
export class FrameCache {
  private readonly state = new Map<string, 'loading' | 'ready'>();

  constructor(
    private readonly load: (src: string) => Promise<void> = src => { const img = new Image(); img.src = src; return img.decode(); },
    private readonly onReady?: (src: string) => void,
  ) {}

  request(src: string) {
    if (this.state.has(src)) return;
    this.state.set(src, 'loading');
    this.load(src).then(() => { this.state.set(src, 'ready'); this.onReady?.(src); }, () => this.state.delete(src));
  }

  isReady(src: string) { return this.state.get(src) === 'ready'; }
}
