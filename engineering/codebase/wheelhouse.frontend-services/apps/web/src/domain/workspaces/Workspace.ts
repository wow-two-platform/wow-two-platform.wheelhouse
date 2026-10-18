/** A group of products, such as Platform or Ventures, in the owner's order. */
export interface Workspace {
  id: string;
  /** The workspace's address: `/w/{slug}`. Fixed once the workspace exists. */
  slug: string;
  name: string;
  description: string;
  /** The workspace's place in the custom order, from zero. */
  order: number;
}

/** A new workspace; the slug is fixed once it exists. */
export interface CreateWorkspaceRequest {
  slug: string;
  name: string;
  description?: string;
}

/** A workspace's editable fields. */
export interface UpdateWorkspaceRequest {
  name: string;
  description?: string;
}
