/** What a config check found wrong with one setting, as its step names it (the service's `config-result` line). */
export const StepFindingKind = {
  /** A missing required value, or one its rules reject. */
  Invalid: 'invalid',
  /** A key under the service's own sections that no settings record binds. */
  Unknown: 'unknown',
  /** A change that leaves some people no way to use the service. */
  Stranded: 'stranded',
} as const;
export type StepFindingKind = (typeof StepFindingKind)[keyof typeof StepFindingKind];
