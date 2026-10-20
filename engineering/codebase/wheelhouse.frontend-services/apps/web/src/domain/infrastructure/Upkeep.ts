import { VpsProvider, type Server } from '@/domain/servers';
import { InfraJobRules, InfraJobStatus, type InfraJobStatus as JobStatus, type JobStep } from './InfraJob';
import { NetworkAddress } from './NetworkAddress';

/** Where an upkeep found the server's public address. */
export const AddressSource = {
  /** Already recorded on the server. */
  Server: 'server',
  /** The address the server was enrolled on. */
  Enrollment: 'enrollment',
  /** The host's own public source address. */
  Host: 'host',
} as const;
export type AddressSource = (typeof AddressSource)[keyof typeof AddressSource];

/** What an upkeep learned; nothing secret. */
export interface UpkeepFacts {
  kitDigest?: string | undefined;
  publicAddress?: string | null | undefined;
  addressSource?: AddressSource | null | undefined;
}

/** One run that brought an enrolled host to the console's kit: the kit, its firewall and the platform. */
export interface Upkeep {
  id: string;
  kind: 'upkeep';
  serverId: string;
  status: JobStatus;
  reason: string | null;
  actor: string;
  createdAt: string;
  startedAt: string | null;
  completedAt: string | null;
  facts: UpkeepFacts;
  steps: JobStep[];
}

/** A server's upkeep runs, newest first, beside the digest of the kit this console ships. */
export interface UpkeepListing {
  serverId: string;
  /** The console's own kit; null when it cannot read it. */
  kit: string | null;
  jobs: Upkeep[];
}

const SourceWords: Readonly<Record<AddressSource, string>> = {
  server: 'recorded',
  enrollment: 'from enrollment',
  host: 'reported by the host',
};

/** Pure rules over a server's upkeep. */
export const UpkeepRules = {
  /** Whether a server shows its kit section: an enrolled host, reached on its tailnet address; never the local rig. */
  isVisible: (server: Pick<Server, 'host' | 'provider'>): boolean =>
    server.provider !== VpsProvider.Local && NetworkAddress.isTailnet(server.host),

  /** The newest run, or null before the first. */
  latest: (listing: UpkeepListing | null | undefined): Upkeep | null => listing?.jobs[0] ?? null,

  /** The kit the newest run that succeeded left on the host; null before one succeeded. */
  hostKit: (listing: UpkeepListing | null | undefined): string | null =>
    listing?.jobs.find((job) => job.status === InfraJobStatus.Succeeded)?.facts.kitDigest ?? null,

  /** Whether the host runs this console's kit: known once a run succeeded. */
  isCurrent: (listing: UpkeepListing | null | undefined): boolean => {
    const kit = UpkeepRules.hostKit(listing);
    return kit !== null && kit === listing?.kit;
  },

  /** Whether a run may start: none runs now. */
  canStart: (listing: UpkeepListing | null | undefined): boolean =>
    !InfraJobRules.isActive(UpkeepRules.latest(listing)?.status),

  /** Where the public address came from, in words. */
  sourceWord: (source: AddressSource | null | undefined): string | null => (source ? SourceWords[source] : null),
} as const;
