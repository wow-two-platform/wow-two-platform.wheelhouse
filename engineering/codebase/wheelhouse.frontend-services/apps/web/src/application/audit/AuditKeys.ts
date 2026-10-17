/** Query keys for audit reads; every operator action invalidates them. */
export const AuditKeys = {
  all: ["audit"] as const,
  list: (limit: number, before: number | null, product: string | null = null) =>
    ["audit", "list", product ?? "*", limit, before] as const,
  verification: ["audit", "verification"] as const,
};
