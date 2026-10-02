/** Who hosts a server. */
export const VpsProvider = {
  Hetzner: 'hetzner',
  Local: 'local',
} as const;
export type VpsProvider = (typeof VpsProvider)[keyof typeof VpsProvider];

/** Each provider's display name. */
export const VpsProviderLabels: Readonly<Record<VpsProvider, string>> = {
  [VpsProvider.Hetzner]: 'Hetzner',
  [VpsProvider.Local]: 'Local',
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
}

/** A server's editable definition; the slug is fixed once the server exists. */
export type SaveServerRequest = Omit<Server, 'slug'> & { slug?: string };

export type { ContainerVitals, DiskUsage, ServerVitals, HostVitals, TargetVitals } from './models/ServerVitals';
export { TrendRanges, hostTrend, type TrendPoint, type TrendRange, type VitalsSample } from './models/VitalsSample';
export { ServerExtensions, RESTART_WARNING, USAGE_THRESHOLDS, type ContainerTone } from './ServerExtensions';
