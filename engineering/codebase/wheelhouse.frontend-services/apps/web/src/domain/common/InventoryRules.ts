/** The shapes inventory values take — the host validates the same ones and names the field it refuses. */
export const InventoryRules = {
  /** A product, server, environment, vault, service or site name: a lowercase letter, then lowercase letters, digits
   * or dashes, 48 at most. */
  slug: /^[a-z][a-z0-9-]{0,47}$/,
  /** A GitHub repository as `owner/name`. */
  repository: /^[A-Za-z0-9][A-Za-z0-9-]{0,38}\/[A-Za-z0-9._-]{1,100}$/,
  /** Whether text is an absolute https address of 2048 characters at most, with a host and no user information, as a
   * server's console address must be. */
  isHttpsAddress: (value: string): boolean => {
    if (value.length > 2048) return false;
    try {
      const url = new URL(value);
      return (
        url.protocol === "https:" &&
        url.hostname !== "" &&
        !url.username &&
        !url.password
      );
    } catch {
      return false;
    }
  },
} as const;
