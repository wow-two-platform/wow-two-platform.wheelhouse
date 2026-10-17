/** The areas of the Activity view's filter, each owning a set of action prefixes. */
export const AuditArea = {
  All: 'all',
  Deployments: 'deployments',
  Secrets: 'secrets',
  Products: 'products',
} as const;
export type AuditArea = (typeof AuditArea)[keyof typeof AuditArea];

const Labels: Record<string, string> = {
  'deployment.start': 'Deployed',
  'deployment.reconcile': 'Reconciled a rollout',
  'build.request': 'Requested a build',
  'vault.namespace.create': 'Created a namespace',
  'vault.secret.set': 'Set a secret',
  'vault.secret.state': 'Changed a secret’s state',
  'vault.token.mint': 'Minted a product token',
  'vault.token.revoke': 'Revoked a product token',
  'product.create': 'Registered a product',
  'product.update': 'Updated a product',
  'product.delete': 'Removed a product',
  'product.status': 'Changed a product’s status',
  'product.workspace': 'Moved a product to a workspace',
  'product.refresh': 'Read a product’s manifest again',
  'service.status': 'Changed a service’s status',
  'target.create': 'Added an environment',
  'target.update': 'Updated an environment',
  'target.delete': 'Removed an environment',
};
const AreaPrefixes: Record<AuditArea, readonly string[]> = {
  [AuditArea.All]: [''],
  [AuditArea.Deployments]: ['deployment.', 'build.'],
  [AuditArea.Secrets]: ['vault.'],
  [AuditArea.Products]: ['product.', 'service.', 'target.'],
};

/** Pure display rules for audit entries. */
export const AuditExtensions = {
  /** The action in words; an action this build does not know shows its stable name. */
  label: (action: string) => Labels[action] ?? action,
  inArea: (action: string, area: AuditArea) => AreaPrefixes[area].some((prefix) => action.startsWith(prefix)),
  /** Why a chain failed, in words. */
  breakLabel: (reason: string | null | undefined) =>
    reason === 'HashMismatch'
      ? 'an entry was edited'
      : reason === 'BrokenLink'
        ? 'an entry was removed or reordered'
        : reason === 'SequenceGap'
          ? 'entries are missing'
          : 'the chain did not verify',
};
