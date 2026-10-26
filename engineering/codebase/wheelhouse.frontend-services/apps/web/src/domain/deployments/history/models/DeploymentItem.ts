import type { SettingChange } from "@/domain/settings";
import type { Attention, DeployState, ServiceResult } from '@/domain/common/StatusVocabulary';
import type { JobStatus } from '../../enums/JobStatus';
import type { ReleaseKind } from '../../models/ReleaseArtifact';
import type { ReleaseComponent } from '../../models/ReleaseComponent';
import type { DeploymentOperation } from '../../controls/ServiceControls';

/** What a deployment status amounted to before the status taxonomy; kept one release beside the state. */
export const DeploymentOutcome = {
  Succeeded: 'succeeded',
  Failed: 'failed',
  Refused: 'refused',
  Pending: 'pending',
  Unknown: 'unknown',
} as const;
export type DeploymentOutcome = (typeof DeploymentOutcome)[keyof typeof DeploymentOutcome];

/** The step a live rollout is on. */
export interface CurrentStep {
  name: string;
  status?: string | null;
  startedAt?: string | null;
  /** The step's position in the rollout, from 1. */
  index?: number | null;
}

/** The watch window after a successful rollout: its containers and sites looked at once a minute. */
export interface DeploymentWatch {
  /** `replaced`: another rollout ended the window. */
  status: 'watching' | 'passed' | 'degraded' | 'replaced' | 'unverified';
  until: string | null;
  checks: number | null;
  /** What regressed: a container that stopped, turned unhealthy or restarted, or a site gone silent. */
  findings: string[];
  degradedAt: string | null;
  completedAt: string | null;
}

/** Why a deployment ended in its state, with where it started. */
export interface DeploymentStateCause {
  /** `service`, `step`, `gate`, `freeze`, `validation`, `platform`, `busy`, `runner` or `operator`. */
  kind: string;
  /** A service's key or a step's name; null when nothing narrower is known. */
  origin: string | null;
  message: string | null;
  at: string | null;
}

/** A service the origin likely hurt, along a declared path; inferred, never an observed call. */
export interface DeploymentEffect {
  service: string;
  finding: string;
  /** The origin it declares it depends on. */
  via: string;
  at: string | null;
}

/** How one service ended in a deployment. */
export interface DeploymentServiceResult {
  service: string;
  result: ServiceResult;
  /** Whether the deployment's trouble started here. */
  origin: boolean;
  from: string | null;
  to: string | null;
  health: string | null;
  /** Why it changed: `added`, `image`, `definition` (memory, health check, mounts) or `settings`; null when unchanged. */
  reason: string | null;
}

/** One deployment as lists show it: what went where, how it went and what it changed. */
export interface DeploymentItem {
  id: string;
  targetId: string | null;
  /** Null for a target the inventory no longer holds. */
  environment: string | null;
  product: string | null;
  workspace: string | null;
  release: string | null;
  /** The bundle a redeploy submits again. */
  bundleId: string | null;
  /** Each repository's release when the release is a set of a product built from several (decision 70). */
  components: ReleaseComponent[] | null;
  kind: ReleaseKind | null;
  branch: string | null;
  sourceCommit: string | null;
  actor: string | null;
  /** The owner's one-line why for the deploy, when one was given. */
  deployReason: string | null;
  status: JobStatus;
  outcome: DeploymentOutcome;
  /** What the environment runs because of it. */
  state: DeployState;
  /** Which way it got there, such as `partly_rolled_back`. */
  substate: string | null;
  /** How much attention it needs; its colour follows this. */
  attention: Attention;
  cause: DeploymentStateCause | null;
  effects: DeploymentEffect[];
  /** Each service's result in rollout order; null when the target did not report them. */
  services: DeploymentServiceResult[] | null;
  reason: string | null;
  warnings: string[];
  submittedAt: string;
  startedAt: string | null;
  completedAt: string | null;
  durationSeconds: number | null;
  /** The services whose image changed; null when the runner no longer holds the bundle. */
  changedServices: string[] | null;
  currentStep: CurrentStep | null;
  /** The watch after a successful rollout; null when it was not watched. */
  watch: DeploymentWatch | null;
  /** The setting and secret keys its render changed against the target's last good render; null when none. */
  settingsChanged?: SettingChange[] | null;
  /** Whether those changes are the likely cause: it failed, rolled back, regressed or failed its check. */
  settingsSuspect?: boolean;
  /** Set when the item is a control — a stop, start or restart of the release its target runs, which `release` names;
   * null for a deploy. */
  operation?: DeploymentOperation | null;
}
