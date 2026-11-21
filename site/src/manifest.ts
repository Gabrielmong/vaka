export type Manifest = {
  loopSeconds: number;
  size: { w: number; h: number };
  hero: string;
  states: { name: string; from: number; to: number }[];
  templates: { id: string; name: string; description: string; shows: string[]; frames: number }[];
};

/** The frame index written by `Vaka --frames` (absent until the frames are rendered). */
export async function loadManifest(): Promise<Manifest> {
  const r = await fetch('./panel/manifest.json');
  if (!r.ok) throw new Error(`manifest: HTTP ${r.status}`);
  return (await r.json()) as Manifest;
}
