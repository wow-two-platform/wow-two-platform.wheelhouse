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
  /**
   * Whether the host reads the server as the control host, the one the consoles run on and reach at a private address:
   * Wheelhouse's own environments run only there, and it is never enrolled. Never sent on a save.
   */
  controlHost?: boolean | undefined;
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
