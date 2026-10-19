/** Query keys for deployment reads; writes invalidate through these. */
export const DeploymentKeys = {
  all: ["deployments"] as const,
  history: ["deployments", "history"] as const,
  targets: ["deployments", "targets"] as const,
  releases: ["deployments", "releases"] as const,
  stats: (days: number) => ["deployments", "stats", days] as const,
  statsAll: ["deployments", "stats"] as const,
  state: (target: string) => ["deployments", "state", target] as const,
  outcome: (id: string) => ["deployments", "outcome", id] as const,
  branches: (product: string) => ["deployments", "branches", product] as const,
  commits: (product: string, branch: string) =>
    ["deployments", "commits", product, branch] as const,
  commitsAll: ["deployments", "commits"] as const,
  logs: (target: string, service: string, tail: number) =>
    ["deployments", "logs", target, service, tail] as const,
  /** A target's settings plan for one release; a save invalidates every release's. */
  settings: (target: string, release: string) => ["deployments", "settings", target, release] as const,
  settingsOf: (target: string) => ["deployments", "settings", target] as const,
  /** Every history view (pages, calendar, measures, promotions, issues): what a finished rollout changes. */
  historyViews: ["deployments", "history", "view"] as const,
  historyPage: (filter: string, limit: number) => ["deployments", "history", "view", "page", filter, limit] as const,
  activity: (filter: string, tz: string) => ["deployments", "history", "view", "activity", filter, tz] as const,
  rangeStats: (filter: string) => ["deployments", "history", "view", "stats", filter] as const,
  promotions: (filter: string) => ["deployments", "history", "view", "promotions", filter] as const,
  issues: (filter: string) => ["deployments", "history", "view", "issues", filter] as const,
  running: (filter: string) => ["deployments", "history", "running", filter] as const,
  detail: (id: string) => ["deployments", "history", "detail", id] as const,
};
