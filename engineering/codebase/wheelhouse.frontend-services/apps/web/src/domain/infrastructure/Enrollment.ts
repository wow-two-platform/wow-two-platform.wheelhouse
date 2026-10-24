import { VpsProvider, type Server, type ServerIngress } from '@/domain/servers';
import { InfraJobStatus, JobStepStatus, type InfraJobStatus as JobStatus, type JobStep, type MintedKey } from './InfraJob';
import { NetworkAddress } from './NetworkAddress';

/** What an enrollment was asked to do; the same fields start or rerun it. */
export interface StartEnrollmentRequest {
  /** The server's public IP address, as the provider shows it. */
  publicAddress: string;
  sshPort: number;
  /** The account the image delivers: root on a root-only image, else its administrator such as ubuntu. */
  loginUser: string;
  /** The administrator account; the sign-in account itself unless signing in as root. */
  admin: string;
  /** The administrator's SSH public key, needed only when the administrator does not exist yet. */
  adminKey: string | null;
  tailnetHostname: string;
  /** The host key fingerprint the provider's console shows, `SHA256:…`, to confirm the first contact. */
  expectedHostKey: string | null;
}

/** An enrollment's recorded request. */
export interface EnrollmentRequest extends StartEnrollmentRequest {
  serverId: string;
}

/** What an enrollment learned; nothing secret. */
export interface EnrollmentFacts {
  hostKey?: string | undefined;
  hostKeyFingerprint?: string | undefined;
  /** Who confirmed the host key: the owner's typed fingerprint, or the tailnet path presenting the same key. */
  hostKeyConfirmedBy?: 'owner' | 'tailnet' | undefined;
  kitDigest?: string | undefined;
  tailnetAddress?: string | undefined;
  /** The public address the server was enrolled on; its public sites' baseline hosts name it. */
  publicAddress?: string | undefined;
  nodeId?: string | undefined;
  nodeName?: string | undefined;
  publicSshClosed?: boolean | undefined;
}

/** The one-time enrollment key's public half, shown while the host still needs it. */
export interface OneTimeKey {
  publicKey: string;
  fingerprint: string;
}

/** The one-time key created for a server. */
export interface EnrollmentKey extends OneTimeKey {
  serverId: string;
}

/** A server's enrollment: Wheelhouse signs in once with its one-time key, sets the host up and moves it onto the
 * tailnet. */
export interface Enrollment {
  kind: 'enrollment';
  serverId: string;
  status: JobStatus;
  id?: string | undefined;
  reason?: string | null | undefined;
  actor?: string | undefined;
  attempt?: number | undefined;
  createdAt?: string | undefined;
  startedAt?: string | null | undefined;
  completedAt?: string | null | undefined;
  request?: EnrollmentRequest | undefined;
  facts: EnrollmentFacts;
  mints: MintedKey[];
  steps: JobStep[];
  oneTimeKey?: OneTimeKey | undefined;
}

/** The account Wheelhouse deploys as on every products host. */
export const DEPLOY_ACCOUNT = 'wheelhouse-deploy';

/**
 * How a products host's kit publishes sites: public ones on websecure (port 443) with Let's Encrypt HTTP-01
 * certificates, private ones on the private entry point behind their site nodes, both probed on the host itself.
 * Enrollment and kit refresh record it on the server, with its public address.
 */
export const ProductsHostIngress: Readonly<ServerIngress> = {
  scheme: 'https',
  port: null,
  entryPoints: ['websecure'],
  privateEntryPoints: ['private'],
  certResolver: 'letsencrypt',
  pattern: null,
  probe: 'https://127.0.0.1',
  privateProbe: 'http://127.0.0.1:18080',
};

/** The shapes the host accepts for an enrollment's fields. */
export const EnrollmentPatterns = {
  loginUser: /^[a-z_][a-z0-9_-]{0,31}$/,
  admin: /^[a-z][a-z0-9-]{0,30}$/,
  hostname: /^[a-z][a-z0-9-]{0,61}[a-z0-9]$/,
  fingerprint: /^SHA256:[A-Za-z0-9+/]{43}=?$/,
  publicKey: /^(ssh-ed25519|ssh-rsa|ecdsa-sha2-nistp(256|384|521)) [A-Za-z0-9+/]+=*( [^\s]{1,100})?$/,
  /** The administrator key's longest accepted length. */
  publicKeyLength: 1000,
} as const;

/** Administrator names the host refuses. */
export const RESERVED_ADMINS: readonly string[] = ['root', DEPLOY_ACCOUNT];

/** Pure rules over a server's enrollment. */
export const EnrollmentRules = {
  /** The account a provider's image delivers: root on Contabo's root-only images, ubuntu on OVHcloud's, else root. */
  loginUserFor: (provider: VpsProvider): string => (provider === VpsProvider.Ovhcloud ? 'ubuntu' : 'root'),

  /** The administrator a sign-in account sets up: ubuntu when signing in as root, else the account itself. */
  adminFor: (loginUser: string): string => (loginUser === 'root' ? 'ubuntu' : loginUser),

  /** The tailnet hostname a server suggests: its slug, without a trailing dash. */
  hostnameFor: (server: Pick<Server, 'slug'>): string => server.slug.replace(/-+$/, ''),

  /** The public address a server suggests: its host, unless that is already a tailnet address. */
  publicAddressOf: (server: Pick<Server, 'host'>): string => (NetworkAddress.isTailnet(server.host) ? '' : server.host),

  /**
   * Whether a server shows its Enrollment section: once an enrollment exists, or while it is still reached on a public
   * address and is neither the local rig nor the control host, which the consoles run on.
   */
  isVisible: (
    server: Pick<Server, 'host' | 'provider' | 'controlHost'>,
    enrollment: Pick<Enrollment, 'status'> | null | undefined,
  ): boolean =>
    (enrollment != null && enrollment.status !== InfraJobStatus.NotStarted) ||
    (server.provider !== VpsProvider.Local && !server.controlHost && !NetworkAddress.isTailnet(server.host)),

  /** Whether the owner can start the enrollment, or run it again after it stopped short. */
  canStart: (enrollment: Pick<Enrollment, 'status'> | null | undefined): boolean =>
    !enrollment ||
    enrollment.status === InfraJobStatus.NotStarted ||
    enrollment.status === InfraJobStatus.Failed ||
    enrollment.status === InfraJobStatus.Interrupted,

  /** Whether starting again continues an earlier attempt. */
  isRerun: (enrollment: Pick<Enrollment, 'status'> | null | undefined): boolean =>
    enrollment?.status === InfraJobStatus.Failed || enrollment?.status === InfraJobStatus.Interrupted,

  /** Whether the first public contact succeeded, which fixes the public address for a rerun. */
  contactSucceeded: (enrollment: Pick<Enrollment, 'steps'> | null | undefined): boolean =>
    enrollment?.steps.some((step) => step.key === 'contact' && step.status === JobStepStatus.Succeeded) ?? false,

  /** Who confirmed the host key, in words. */
  confirmedBy: (facts: EnrollmentFacts): string | null =>
    facts.hostKeyConfirmedBy === 'owner'
      ? 'confirmed by you'
      : facts.hostKeyConfirmedBy === 'tailnet'
        ? 'proven over the tailnet'
        : null,

  /** The one command that adds the one-time key to the sign-in account on a running server. */
  authorizeCommand: (publicKey: string, loginUser: string, address: string): string =>
    `echo '${publicKey}' | ssh ${loginUser}@${address || '<ip>'} 'cat >> ~/.ssh/authorized_keys'`,
} as const;
