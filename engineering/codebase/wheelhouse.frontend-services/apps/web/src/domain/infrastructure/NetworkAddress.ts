/** Pure rules over IP addresses. */
export const NetworkAddress = {
  /** A dotted IPv4 address's four octets, or null for anything else: no leading zeros, each 0–255. */
  ipv4: (text: string): [number, number, number, number] | null => {
    const parts = text.trim().split('.');
    if (parts.length !== 4) return null;
    const octets = parts.map((part) => (/^(0|[1-9][0-9]{0,2})$/.test(part) ? Number(part) : NaN));
    return octets.every((octet) => octet >= 0 && octet <= 255)
      ? (octets as [number, number, number, number])
      : null;
  },

  /** Whether the text is an IPv6 address, bracketless, as a URL host accepts it. */
  isIpv6: (text: string): boolean => {
    const value = text.trim();
    if (!value.includes(':') || !/^[0-9A-Fa-f:.]+$/.test(value)) return false;
    try {
      return new URL(`http://[${value}]/`).hostname.length > 2;
    } catch {
      return false;
    }
  },

  /** Whether the text is an IPv4 or IPv6 address. */
  isIp: (text: string): boolean => NetworkAddress.ipv4(text) !== null || NetworkAddress.isIpv6(text),

  /** Whether the address is a tailnet one: IPv4 in Tailscale's 100.64.0.0/10. */
  isTailnet: (text: string | null | undefined): boolean => {
    const octets = text ? NetworkAddress.ipv4(text) : null;
    return octets !== null && octets[0] === 100 && octets[1] >= 64 && octets[1] <= 127;
  },

  /** Whether the text is a public address enrollment accepts: an IP address outside the tailnet range. */
  isPublic: (text: string): boolean => NetworkAddress.isIp(text) && !NetworkAddress.isTailnet(text),
} as const;
