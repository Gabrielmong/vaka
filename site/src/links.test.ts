import { describe, expect, test, vi } from 'vitest';
import { initialDownloadUrl, latestSetupUrl, releasesUrl, resolveDownload } from './links';

const respond = (ok: boolean, body: unknown = {}) =>
  vi.fn(async () => ({ ok, status: ok ? 200 : 404, json: async () => body }) as unknown as Response);

describe('resolveDownload', () => {
  test('release with the setup asset → latest download', async () => {
    const f = respond(true, { assets: [{ name: 'VakaSetup.exe' }] });
    expect(await resolveDownload(f)).toBe(latestSetupUrl);
  });
  test('404 (no release yet) → releases page', async () => {
    expect(await resolveDownload(respond(false))).toBe(releasesUrl);
  });
  test('release without the asset → releases page', async () => {
    expect(await resolveDownload(respond(true, { assets: [{ name: 'notes.txt' }] }))).toBe(releasesUrl);
  });
  test('network error or rate limit → releases page', async () => {
    expect(await resolveDownload(vi.fn(async () => { throw new TypeError('offline'); }))).toBe(releasesUrl);
    expect(await resolveDownload(respond(true, { message: 'API rate limit exceeded' }))).toBe(releasesUrl);
  });
  test('calls the API exactly once', async () => {
    const f = respond(true, { assets: [{ name: 'VakaSetup.exe' }] });
    await resolveDownload(f);
    expect(f).toHaveBeenCalledTimes(1);
    expect(String((f.mock.calls[0] as unknown[])[0])).toMatch(/^https:\/\/api\.github\.com\/repos\/[^/]+\/[^/]+\/releases\/latest$/);
  });
  test('download URL follows releases/latest', () => {
    expect(latestSetupUrl).toMatch(/^https:\/\/github\.com\/[^/]+\/[^/]+\/releases\/latest\/download\/VakaSetup\.exe$/);
  });
  test('button starts on the releases page until a release is confirmed', () => {
    expect(initialDownloadUrl).toBe(releasesUrl);
  });
  test('a hanging API call gives up and falls back', async () => {
    const hang = vi.fn((_u: string, init?: { signal?: AbortSignal }) => new Promise<Response>((_, reject) =>
      init?.signal?.addEventListener('abort', () => reject(new DOMException('timeout', 'TimeoutError')))));
    expect(await resolveDownload(hang, 20)).toBe(releasesUrl);
  });
});
