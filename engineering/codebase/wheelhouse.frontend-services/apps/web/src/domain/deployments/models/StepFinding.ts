import type { StepFindingKind } from '../enums/StepFindingKind';

/** One finding a config check step names: the setting and the rule it broke, never its value nor the rule's message. */
export interface StepFinding {
  kind: StepFindingKind;
  /** The setting's configuration path, such as `Identity:Password:MinLength`; null when the name was unreadable. */
  setting: string | null;
  /** The rule's short kebab-case id, such as `range`; null for an unknown key. */
  code: string | null;
  /** How many people a stranding change leaves with no way in; null for any other finding. */
  count: number | null;
}
