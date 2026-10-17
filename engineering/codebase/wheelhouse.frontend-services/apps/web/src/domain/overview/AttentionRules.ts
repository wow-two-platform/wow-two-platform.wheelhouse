import { Measures } from '@/domain/common';
import { TargetCondition, type DeploymentStats } from '@/domain/deployments';
import { ServerExtensions, RESTART_WARNING, USAGE_THRESHOLDS, type ServerVitals } from '@/domain/servers';
import { VaultStatus, type VaultHygiene, type VaultSummary } from '@/domain/secrets';

/** How urgent an attention item is. */
export type AttentionTone = 'danger' | 'warning';

/** One thing an operator should look at, with the page that resolves it. */
export interface AttentionItem {
  id: string;
  tone: AttentionTone;
  title: string;
  detail: string;
  href: string;
}

const [WARN_PERCENT, CRITICAL_PERCENT] = USAGE_THRESHOLDS;
const LOAD_WARNING_PERCENT = 150;
const LISTED = 3;

function listed(values: string[]) {
  return values.slice(0, LISTED).join(', ') + (values.length > LISTED ? ` and ${values.length - LISTED} more` : '');
}

/** Pure rules that turn server, deployment and vault readings into attention items. */
export const AttentionRules = {
  /** Unreachable or locked targets, failing containers, and hosts short on disk, memory or CPU. */
  fromVitals: (vitals: ServerVitals): AttentionItem[] => {
    const items: AttentionItem[] = [];
    for (const target of vitals.targets) {
      const id = target.targetId;
      if (!target.ok) {
        items.push({ id: `unreachable:${id}`, tone: 'danger', title: `${id} could not be read`,
          detail: target.reason ?? 'Wheelhouse could not reach the target.', href: '/servers' });
        continue;
      }
      if (target.condition === TargetCondition.NeedsReconciliation)
        items.push({ id: `reconcile:${id}`, tone: 'danger', title: `${id} needs reconciliation`,
          detail: 'A rollout stopped after changing containers; new deployments wait until it is reconciled.', href: '/deployments' });
      if (target.release && target.containers?.length === 0)
        items.push({ id: `empty:${id}`, tone: 'danger', title: `${id} runs no containers`,
          detail: `It last verified ${target.release}, but none of its containers exist now.`, href: '/servers' });
      for (const container of target.containers ?? []) {
        if (!ServerExtensions.isHealthy(container))
          items.push({ id: `container:${id}:${container.service}`, tone: 'danger',
            title: `${container.service} on ${id} is ${ServerExtensions.containerLabel(container)}`,
            detail: `${container.state === 'running' ? 'Running but failing its health check' : 'Not serving traffic'}; `
              + `${container.restarts} restart${container.restarts === 1 ? '' : 's'}.`, href: '/servers' });
        else if (container.restarts >= RESTART_WARNING)
          items.push({ id: `restarts:${id}:${container.service}`, tone: 'warning',
            title: `${container.service} on ${id} restarted ${container.restarts} times`,
            detail: 'It is running now; repeated restarts usually mean a crash loop or memory pressure.', href: '/servers' });
      }
      for (const problem of target.problems ?? [])
        items.push({ id: `problem:${id}:${problem}`, tone: 'warning', title: `${id}: ${problem}`,
          detail: 'Part of the reading failed; the rest is shown.', href: '/servers' });
    }
    for (const { serverId: server } of vitals.servers) {
      const host = ServerExtensions.hostOf(vitals, server);
      if (!host) continue;
      for (const disk of host.disks) {
        const used = ServerExtensions.diskPercent(disk);
        if (used > WARN_PERCENT)
          items.push({ id: `disk:${server}:${disk.path}`, tone: used > CRITICAL_PERCENT ? 'danger' : 'warning',
            title: `${disk.path} on ${server} is ${Math.round(used)}% full`,
            detail: `${Measures.bytes(disk.freeBytes)} free of ${Measures.bytes(disk.totalBytes)}.`, href: '/servers' });
      }
      const memory = ServerExtensions.memoryPercent(host);
      if (memory != null && memory > CRITICAL_PERCENT)
        items.push({ id: `memory:${server}`, tone: 'warning', title: `${server} uses ${Math.round(memory)}% of its memory`,
          detail: `${Measures.bytes(host.memoryAvailableBytes)} available.`, href: '/servers' });
      const load = ServerExtensions.loadPercent(host);
      if (load != null && load > LOAD_WARNING_PERCENT)
        items.push({ id: `load:${server}`, tone: 'warning', title: `${server} is overloaded`,
          detail: `1-minute load ${host.load?.[0]} on ${host.cpus} CPUs.`, href: '/servers' });
    }
    return items;
  },

  /** Targets whose latest finished rollout failed and has not been followed by a success. */
  fromStats: (stats: DeploymentStats): AttentionItem[] =>
    stats.targets
      .filter((target) => target.failingSince !== null)
      .map((target) => ({
        id: `failing:${target.targetId}`, tone: 'danger' as const, title: `${target.targetId}'s last rollout failed`,
        detail: `Unrecovered since ${new Date(target.failingSince ?? '').toLocaleString()}.`, href: '/deployments',
      })),

  /** A sealed or unreachable vault, then its secrets and tokens due for rotation. */
  fromVault: (vault: VaultSummary, hygiene: VaultHygiene | undefined): AttentionItem[] => {
    if (vault.status !== VaultStatus.Unsealed)
      return [{ id: `vault:${vault.id}`, tone: 'danger', title: `${vault.name} is ${vault.status}`,
        detail: 'Wheelhouse cannot administer it, and products may fail to load new secret versions.', href: '/secrets' }];
    if (!hygiene) return [];
    const items: AttentionItem[] = [];
    if (hygiene.overdueSecrets.length)
      items.push({ id: `secrets:${vault.id}`, tone: 'warning',
        title: `${hygiene.overdueSecrets.length} secret${hygiene.overdueSecrets.length === 1 ? '' : 's'} in ${vault.name} due for rotation`,
        detail: `Older than ${hygiene.secretRotationDays} days: ${listed(hygiene.overdueSecrets.map((secret) => `${secret.namespace}/${secret.key}`))}.`,
        href: '/secrets' });
    const expired = hygiene.overdueTokens.filter((token) => token.reason === 'expired');
    if (expired.length)
      items.push({ id: `tokens-expired:${vault.id}`, tone: 'danger',
        title: `${expired.length} product token${expired.length === 1 ? '' : 's'} in ${vault.name} expired`,
        detail: `Revoke or replace: ${listed(expired.map((token) => `${token.namespace}/${token.name}`))}.`, href: '/secrets' });
    const due = hygiene.overdueTokens.filter((token) => token.reason !== 'expired');
    if (due.length)
      items.push({ id: `tokens:${vault.id}`, tone: 'warning',
        title: `${due.length} product token${due.length === 1 ? '' : 's'} in ${vault.name} due for rotation`,
        detail: `${listed(due.map((token) => `${token.namespace}/${token.name} (${token.reason})`))}.`, href: '/secrets' });
    return items;
  },

  /** Danger before warnings, keeping each group's order. */
  sort: (items: AttentionItem[]) =>
    [...items].sort((a, b) => (a.tone === b.tone ? 0 : a.tone === 'danger' ? -1 : 1)),
} as const;
