/** Query keys for audit reads; every action invalidates them. */
export const AuditKeys = {
  all: ["audit"] as const,
  list: (pageSize: number, product: string | null = null) => ["audit", "list", product ?? "*", pageSize] as const,
  verification: ["audit", "verification"] as const,
};
