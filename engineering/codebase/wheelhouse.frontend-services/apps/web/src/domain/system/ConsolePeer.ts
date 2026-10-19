import { DeploymentEnvironment } from '@/domain/targets';

/** The other console, as the environment tag switches to it. */
export interface ConsolePeer {
  /** Its origin: `https://host[:port]`, without a path. */
  readonly url: string;
  /** The environment it serves: the one this console does not. */
  readonly environment: DeploymentEnvironment;
}

/** What the API reports about the other console; both fields are null until the host names a peer. */
export interface ConsolePeerReading {
  readonly url: string | null;
  readonly environment: DeploymentEnvironment | null;
}

/** The rules of switching between the dev and prod consoles. */
export const ConsolePeerRules = {
  /**
   * Whether an address is a console's https origin: no other scheme, no credentials, no path, query or hash. The
   * switch only ever opens such an address.
   */
  isOrigin(value: string): boolean {
    try {
      const url = new URL(value);
      return (
        url.protocol === 'https:' &&
        url.username === '' &&
        url.password === '' &&
        url.pathname === '/' &&
        url.search === '' &&
        url.hash === ''
      );
    } catch {
      return false;
    }
  },

  /** The other console from the API's reading, or null without a usable peer: the tag then stays as it is. */
  from(reading: ConsolePeerReading | null | undefined): ConsolePeer | null {
    if (!reading?.url || !reading.environment || !ConsolePeerRules.isOrigin(reading.url)) return null;
    return { url: reading.url.replace(/\/+$/, ''), environment: reading.environment };
  },

  /**
   * The same page on the peer: its origin followed by this page's path, query and hash, as the router names them in
   * `fullPath`. The path is appended to the origin as text, so a path that starts with `//` stays a path on the peer
   * and never names another host.
   */
  hrefOf(peerUrl: string, fullPath: string): string {
    const origin = peerUrl.replace(/\/+$/, '');
    const href = `${origin}${fullPath.startsWith('/') ? '' : '/'}${fullPath}`;
    try {
      return new URL(href).origin === new URL(origin).origin ? href : `${origin}/`;
    } catch {
      return `${origin}/`;
    }
  },
} as const;
