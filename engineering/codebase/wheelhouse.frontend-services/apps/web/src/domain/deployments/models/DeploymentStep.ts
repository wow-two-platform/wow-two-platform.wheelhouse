import type { StepStatus } from '../enums/StepStatus';
import type { StepFinding } from './StepFinding';

/** One step of a rollout; details are display-safe text and never carry setting values. */
export interface DeploymentStep {
  name: string;
  status: StepStatus;
  startedAt?: string;
  completedAt?: string;
  detail?: string;
  /** What a config check step found, by setting and rule; absent on every other step. */
  findings?: readonly StepFinding[];
}
