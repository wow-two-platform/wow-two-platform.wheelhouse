/** Query keys for the tailnet, enrollments and placements. */
export const InfrastructureKeys = {
  tailnet: ["tailnet", "status"] as const,
  policy: ["tailnet", "policy"] as const,
  enrollment: (server: string) => ["enrollments", server] as const,
  placements: (product: string) => ["placements", product] as const,
  upkeep: (server: string) => ["upkeep", server] as const,
};
