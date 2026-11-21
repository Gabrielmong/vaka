export const repo = __REPO__;
export const repoUrl = `https://github.com/${repo}`;
export const releasesUrl = `${repoUrl}/releases`;
export const latestSetupUrl = `${repoUrl}/releases/latest/download/VakaSetup.exe`;
export const licenseUrl = `${repoUrl}/blob/HEAD/LICENSE`;
export const noticesUrl = `${repoUrl}/blob/HEAD/THIRD-PARTY-NOTICES.md`;

/** What the Download button links to before the API answers: never a link that might 404. */
export const initialDownloadUrl = releasesUrl;

type Fetch = (input: string, init?: { signal?: AbortSignal }) => Promise<Response>;

/**
 * Where the Download button should go: the latest release's setup when one exists, otherwise the
 * Releases page (no release yet, rate limited, offline, too slow …). Asks the GitHub API once.
 */
export async function resolveDownload(fetchFn: Fetch = (u, init) => fetch(u, init), timeoutMs = 8000): Promise<string> {
  try {
    const r = await fetchFn(`https://api.github.com/repos/${repo}/releases/latest`, { signal: AbortSignal.timeout(timeoutMs) });
    if (!r.ok) return releasesUrl;
    const body = (await r.json()) as { assets?: { name?: string }[] };
    return body.assets?.some(a => a.name === 'VakaSetup.exe') ? latestSetupUrl : releasesUrl;
  } catch {
    return releasesUrl;
  }
}
