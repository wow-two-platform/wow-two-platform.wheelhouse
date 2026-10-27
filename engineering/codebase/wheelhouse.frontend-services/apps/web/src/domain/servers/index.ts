import { InventoryRules } from '@/domain/common';

/** Who hosts a server. */
export const VpsProvider = {
  Hetzner: 'hetzner',
  Local: 'local',
  /** Refers to an OVHcloud virtual server. */
  Ovhcloud: 'ovhcloud',
  /** Refers to a Contabo virtual server. */
  Contabo: 'contabo',
} as const;
export type VpsProvider = (typeof VpsProvider)[keyof typeof VpsProvider];

/** Each provider's display name. */
export const VpsProviderLabels: Readonly<Record<VpsProvider, string>> = {
  [VpsProvider.Hetzner]: 'Hetzner',
  [VpsProvider.Local]: 'Local',
  [VpsProvider.Ovhcloud]: 'OVHcloud',
  [VpsProvider.Contabo]: 'Contabo',
};

/**
 * Each provider's console sign-in page, which a server's card opens unless the server names its own address (an
 * account's regional manager, say, which stays on the row and never in the code); null for a provider without one.
 */
export const VpsProviderConsoles: Readonly<Record<VpsProvider, string | null>> = {
  [VpsProvider.Hetzner]: 'https://console.hetzner.cloud/',
  [VpsProvider.Local]: null,
  [VpsProvider.Ovhcloud]: 'https://www.ovh.com/manager/',
  [VpsProvider.Contabo]: 'https://my.contabo.com/',
};

/** How a server's ingress publishes sites. */
export interface ServerIngress {
  scheme: 'http' | 'https';
  port: number | null;
  entryPoints: string[];
  privateEntryPoints: string[];
  certResolver: string | null;
  /** The host pattern for sites a target leaves unnamed, such as `{site}-{product}.{environment}.preview.example`. */
  pattern: string | null;
  probe: string | null;
  privateProbe: string | null;
  /**
   * The server's public IPv4 address, where its public entry points answer: a public site with no host gets its
   * baseline host there. Enrollment and kit refresh record it; an edit never changes it.
   */
  publicAddress?: string | null | undefined;
}

/** A host Wheelhouse deploys to; its SSH identity and pinned host key stay files on the control host. */
export interface Server {
  slug: string;
  name: string;
  provider: VpsProvider;
  host: string;
  region: string;
  sshUser: string;
  sshPort: number;
  ingress: ServerIngress;
  /** The https address of the provider console the server's card opens in place of its provider's sign-in page; a
   * host before the field, or a server without one, sends none. */
  consoleUrl?: string | null | undefined;
  /**
   * Whether the host reads the server as the control host, the one the consoles run on and reach at a private address:
   * Wheelhouse's own environments run only there, and it is never enrolled. Never sent on a save.
   */
  controlHost?: boolean | undefined;
}

/** The provider console a server's card opens, and the link's words. */
export interface ServerConsole {
  href: string;
  label: string;
}

/** The console a server's card opens: its own address, else its provider's sign-in page; null for neither, or for an
 * address that is not https. */
export function serverConsoleOf(server: Pick<Server, 'provider' | 'consoleUrl'>): ServerConsole | null {
  const href = server.consoleUrl || VpsProviderConsoles[server.provider];
  return href && InventoryRules.isHttpsAddress(href)
    ? { href, label: `Open ${VpsProviderLabels[server.provider]}` }
    : null;
}

/** A server's editable definition; the slug is fixed once the server exists. */
export type SaveServerRequest = Omit<Server, 'slug' | 'controlHost'> & { slug?: string };

export type {
  ContainerVitals,
  DiskUsage,
  ServerHostVitals,
  ServerVitals,
  HostVitals,
  TargetVitals,
} from './models/ServerVitals';
export { TrendRanges, hostTrend, type TrendPoint, type TrendRange, type VitalsSample } from './models/VitalsSample';
export { ServerExtensions, RESTART_WARNING, USAGE_THRESHOLDS, type ContainerTone } from './ServerExtensions';
