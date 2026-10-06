/** What an integration key may reach, each with the line the console shows beside it. */
export const IntegrationScope = {
  CatalogRead: 'catalog:read',
  DeploymentsRead: 'deployments:read',
} as const;
export type IntegrationScope = (typeof IntegrationScope)[keyof typeof IntegrationScope];

/** The scopes a key can be created with, in the order the console offers them. */
export const IntegrationScopes: readonly { value: IntegrationScope; description: string }[] = [
  { value: IntegrationScope.CatalogRead, description: 'Read products, their lifecycle, sites and vault namespaces.' },
  { value: IntegrationScope.DeploymentsRead, description: 'Read servers, targets, deployment status and health through MCP.' },
];

/** A key another program presents to read Wheelhouse; never its secret. */
export interface IntegrationKey {
  id: string;
  name: string;
  /** The secret's first characters, to tell keys apart. */
  prefix: string;
  scopes: string[];
  createdBy: string;
  createdAt: string;
  lastUsedAt: string | null;
  revokedAt: string | null;
}

/** A new key with its secret, which the host returns this once. */
export interface IntegrationKeyWithSecret {
  key: IntegrationKey;
  secret: string;
}

/** Creates a key. */
export interface CreateIntegrationKeyRequest {
  name: string;
  scopes: IntegrationScope[];
}
