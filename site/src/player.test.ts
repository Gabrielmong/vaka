import { expect, test } from 'vitest';
import type { Manifest } from './manifest';
import { FrameCache, frameUrl, nextFrame, preloadOrder, stateAt, stillUrl } from './player';

const m: Manifest = {
  loopSeconds: 30, size: { w: 800, h: 480 }, hero: 'duo',
  states: [{ name: 'Idle', from: 0, to: 10 }, { name: 'Gaming', from: 10, to: 20 }, { name: 'Rendering', from: 20, to: 30 }],
  templates: [],
};

test('stateAt follows the script', () => {
  expect([0, 9, 10, 25, 29].map(f => stateAt(m, f))).toEqual(['Idle', 'Idle', 'Gaming', 'Rendering', 'Rendering']);
});
test('frameUrl pads and is relative', () => expect(frameUrl('duo', 7)).toBe('./panel/duo/07.webp'));
test('stillUrl is relative and lower-case', () => expect(stillUrl('duo', 'Gaming')).toBe('./panel/duo/gaming.webp'));
test('preloadOrder wraps', () => expect(preloadOrder(28, 30, 4)).toEqual([29, 0, 1, 2]));
test('nextFrame holds until the next frame is ready', () => {
  expect(nextFrame(29, 30, () => false)).toBe(29);
  expect(nextFrame(29, 30, f => f === 0)).toBe(0);
  expect(nextFrame(4, 30, () => true)).toBe(5);
});

test('FrameCache retries a frame whose load failed', async () => {
  let fail = true;
  const loads: string[] = [];
  const cache = new FrameCache(src => { loads.push(src); return fail ? Promise.reject(new Error('net')) : Promise.resolve(); });
  cache.request('a');
  cache.request('a');                       // in flight: not requested twice
  await Promise.resolve(); await Promise.resolve();
  expect(cache.isReady('a')).toBe(false);
  fail = false;
  cache.request('a');                       // failed before: tried again
  await Promise.resolve(); await Promise.resolve();
  expect(cache.isReady('a')).toBe(true);
  expect(loads).toEqual(['a', 'a']);
});
test('FrameCache tells its owner when a frame becomes ready', async () => {
  const ready: string[] = [];
  const cache = new FrameCache(() => Promise.resolve(), src => ready.push(src));
  cache.request('b');
  await Promise.resolve(); await Promise.resolve();
  expect(ready).toEqual(['b']);
});
