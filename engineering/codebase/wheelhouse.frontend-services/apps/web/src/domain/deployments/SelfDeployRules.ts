import type { DeploymentPromotion } from './history/models/DeploymentPromotion';
import type { DeploymentTarget } from './models/DeploymentTarget';
import type { SelfDeployment } from './models/SelfDeployment';

/** How a notice after a self-rollout reads. */
export type SelfNoticeTone = 'success' | 'warning' | 'danger' | 'info';

/** What the console says once it booted from a rollout of itself. */
export interface SelfNotice {
  /** The rollout it is about; a dismissal remembers it. */
  readonly id: string;
  readonly tone: SelfNoticeTone;
  readonly title: string;
  readonly description: string;
}

/** Wheelhouse's own product slug: its targets are the consoles themselves (decision 64). */
export const SelfProduct = 'wheelhouse';

/** @internal How long after it ended a self-rollout is still news. */
const NoticeHours = 24;

/** The rules of Wheelhouse deploying itself, as the console shows them. */
export const SelfDeployRules = {
  /** Whether the product is Wheelhouse itself: never part of a fleet, since its console restarts as it is promoted. */
  isSelfProduct(product: string | null | undefined): boolean {
    return product === SelfProduct;
  },

  /** Whether the target runs Wheelhouse's own prod: promoted only by the owner, with its ID typed and a reason. */
  isSelfProd(target: DeploymentTarget | null | undefined): boolean {
    return target?.self === true && target.environment === 'prod';
  },

  /** Whether deploying the target replaces the console in use, which serves `environment`. */
  replacesConsole(target: DeploymentTarget | null | undefined, environment: string | null | undefined): boolean {
    return target?.self === true && Boolean(environment) && target.environment === environment;
  },

  /** The release a prod target is offered first: the newest dev-proven one Ready to promote lists for it, when the
   *  release list holds it; null otherwise, so the owner picks. */
  preselected(
    offers: readonly DeploymentPromotion[] | null | undefined,
    target: DeploymentTarget | null | undefined,
    available: readonly { id: string }[],
  ): string | null {
    if (!target || target.environment !== 'prod') return null;
    const offer = (offers ?? []).find((item) => item.prodTargetId === target.id);
    return offer && available.some((item) => item.id === offer.bundleId) ? offer.bundleId : null;
  },

  /** The notice after this console booted from a rollout of itself, within a day of its end and until dismissed; null
   *  when there is nothing to say. */
  notice(reading: SelfDeployment | null | undefined, now: Date, dismissed: ReadonlySet<string>): SelfNotice | null {
    const run = reading?.latest;
    if (!run || !run.beforeBoot || dismissed.has(run.id)) return null;
    const ended = Date.parse(run.completedAt ?? run.submittedAt);
    if (Number.isFinite(ended) && now.getTime() - ended > NoticeHours * 3_600_000) return null;
    const release = run.release ?? 'a new release';
    const by = run.actor ? ` by ${run.actor}` : '';
    const why = run.reason ? `: “${run.reason}”` : '';
    switch (run.status) {
      case 'succeeded':
        return { id: run.id, tone: 'success', title: `Wheelhouse now runs ${release}`,
          description: `Promoted${run.previousRelease ? ` from ${run.previousRelease}` : ''}${by}${why}. This console started with it.` };
      case 'rolled_back':
        return { id: run.id, tone: 'warning', title: `The promotion to ${release} rolled back`,
          description: `${run.failure ?? 'The new console failed its checks'}. This console runs ${run.previousRelease ?? 'the previous release'} again.` };
      case 'failed':
      case 'rollback_failed':
      case 'interrupted':
        return { id: run.id, tone: 'danger', title: `The promotion to ${release} did not finish`,
          description: `${run.failure ?? 'The rollout stopped'}. Reconcile it in Deployments, or roll back with self-deploy.py on the host.` };
      case 'submitting':
      case 'queued':
      case 'running':
        return { id: run.id, tone: 'info', title: `Finishing the promotion to ${release}`,
          description: 'The host runner is checking the new console through its site and tidying up.' };
      default:
        return null;
    }
  },
} as const;
