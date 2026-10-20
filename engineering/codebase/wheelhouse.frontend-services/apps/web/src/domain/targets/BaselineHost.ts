import { DeploymentEnvironment } from './Target';

/** The service that answers any `<label>.<a-b-c-d>.sslip.io` with `a.b.c.d`. */
export const BASELINE_DOMAIN = 'sslip.io';

/** A dotted IPv4 address. @internal */
const IPV4 = /^(25[0-5]|2[0-4]\d|1\d\d|[1-9]?\d)(\.(25[0-5]|2[0-4]\d|1\d\d|[1-9]?\d)){3}$/;

/** A host on some address's baseline domain, whatever its product or site. @internal */
const BASELINE = /^[a-z0-9-]+\.\d{1,3}(-\d{1,3}){3}\.sslip\.io$/;

/**
 * A public site's host before its product has a domain (decision 68), on its server's public address through sslip.io:
 * `<product>-dev.<ip>.sslip.io` in dev and `<product>.<ip>.sslip.io` in prod, the address's dots as dashes; a site other
 * than the product's front door adds `-<site>` after the product. A bound domain replaces it.
 */
export const BaselineHost = {
  /** The baseline host, or null without an IPv4 address or when its first label would pass 63 characters. */
  of: (product: string, site: string, environment: string, address: string | null | undefined, primary: boolean): string | null => {
    if (!address || !IPV4.test(address)) return null;
    const label = product + (primary ? '' : `-${site}`) + (environment === DeploymentEnvironment.Prod ? '' : `-${environment}`);
    return label.length <= 63 ? `${label}.${address.replaceAll('.', '-')}.${BASELINE_DOMAIN}` : null;
  },

  /** Whether a host is some baseline host: one Wheelhouse derived, which follows the environment and server. */
  isBaseline: (host: string): boolean => BASELINE.test(host.trim().toLowerCase()),
} as const;
