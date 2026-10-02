/** The shapes inventory values take — the host validates the same ones and names the field it refuses. */
export const InventoryRules = {
  /** A product, server, environment, vault, service or site name: a lowercase letter, then lowercase letters, digits
   * or dashes, 48 at most. */
  slug: /^[a-z][a-z0-9-]{0,47}$/,
  /** A GitHub repository as `owner/name`. */
  repository: /^[A-Za-z0-9][A-Za-z0-9-]{0,38}\/[A-Za-z0-9._-]{1,100}$/,
} as const;
