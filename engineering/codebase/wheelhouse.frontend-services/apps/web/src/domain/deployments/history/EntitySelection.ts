/** One product in the entity tree, with its service keys. */
export interface EntityProduct {
  readonly slug: string;
  readonly name: string;
  readonly services: readonly string[];
}

/** One workspace in the entity tree, with its products. */
export interface EntityWorkspace {
  readonly slug: string;
  readonly name: string;
  readonly products: readonly EntityProduct[];
}

/** The tree a deployment view's entities are picked from: workspace, then product, then service. */
export type EntityTree = readonly EntityWorkspace[];

/** A parsed entity key. */
export type EntityRef =
  | { readonly kind: 'workspace'; readonly workspace: string }
  | { readonly kind: 'product'; readonly product: string }
  | { readonly kind: 'service'; readonly product: string; readonly service: string };

/** A node a picker shows: a workspace, one of its products, or one of a product's services. */
export type EntityNode =
  | { readonly kind: 'workspace'; readonly workspace: string }
  | { readonly kind: 'product'; readonly workspace: string; readonly product: string }
  | { readonly kind: 'service'; readonly workspace: string; readonly product: string; readonly service: string };

/**
 * How a node's checkbox reads: `checked` when picked itself, `implied` when a picked ancestor covers it, `partial` when
 * only some of what it holds is picked, `unchecked` otherwise.
 */
export type CheckState = 'checked' | 'implied' | 'partial' | 'unchecked';

/** One picked entity as a removable chip. */
export interface EntityChip {
  readonly key: string;
  readonly kind: EntityRef['kind'];
  readonly label: string;
}

const SLUG = /^[a-z][a-z0-9-]{0,47}$/;

/** Builds and reads the keys a selection holds: `w:{workspace}`, `p:{product}`, `s:{product}/{service}`. */
export const EntityKeys = {
  workspace: (slug: string) => `w:${slug}`,
  product: (slug: string) => `p:${slug}`,
  service: (product: string, service: string) => `s:${product}/${service}`,

  /** The key's entity, or null for a malformed key. */
  parse(key: string): EntityRef | null {
    const value = key.slice(2);
    if (key.startsWith('w:') && SLUG.test(value)) return { kind: 'workspace', workspace: value };
    if (key.startsWith('p:') && SLUG.test(value)) return { kind: 'product', product: value };
    if (key.startsWith('s:')) {
      const [product, service, ...rest] = value.split('/');
      if (rest.length === 0 && product && service && SLUG.test(product) && SLUG.test(service))
        return { kind: 'service', product, service };
    }
    return null;
  },

  /** A node's own key. */
  of(node: EntityNode): string {
    return node.kind === 'workspace'
      ? EntityKeys.workspace(node.workspace)
      : node.kind === 'product'
        ? EntityKeys.product(node.product)
        : EntityKeys.service(node.product, node.service);
  },
} as const;

/** The products of a workspace in the tree. @internal */
function productsOf(tree: EntityTree, workspace: string): readonly EntityProduct[] {
  return tree.find((item) => item.slug === workspace)?.products ?? [];
}

/** The services of a product in the tree. @internal */
function servicesOf(tree: EntityTree, product: string): readonly string[] {
  for (const workspace of tree) {
    const found = workspace.products.find((item) => item.slug === product);
    if (found) return found.services;
  }
  return [];
}

/** Keeps each key once, in first-seen order. @internal */
function unique(keys: readonly string[]): string[] {
  return [...new Set(keys)];
}

/**
 * The selection model of the entity picker: workspaces, products and services mix and match. Picking a node drops
 * what it already covers; clearing a node a picked ancestor covers keeps its siblings picked instead.
 */
export const EntitySelection = {
  /** How a node's checkbox reads under a selection. */
  state(tree: EntityTree, selection: readonly string[], node: EntityNode): CheckState {
    const picked = new Set(selection);
    if (picked.has(EntityKeys.of(node))) return 'checked';
    if (node.kind !== 'workspace' && picked.has(EntityKeys.workspace(node.workspace))) return 'implied';
    if (node.kind === 'service') return picked.has(EntityKeys.product(node.product)) ? 'implied' : 'unchecked';
    if (node.kind === 'product')
      return selection.some((key) => key.startsWith(`s:${node.product}/`)) ? 'partial' : 'unchecked';
    const products = new Set(productsOf(tree, node.workspace).map((item) => item.slug));
    return selection.some((key) => {
      const entity = EntityKeys.parse(key);
      return entity !== null && entity.kind !== 'workspace' && products.has(entity.product);
    })
      ? 'partial'
      : 'unchecked';
  },

  /** The selection after the operator clicks a node's checkbox. */
  toggle(tree: EntityTree, selection: readonly string[], node: EntityNode): string[] {
    const own = EntityKeys.of(node);
    const workspaceKey = EntityKeys.workspace(node.workspace);
    const without = (keys: readonly string[], drop: (key: string) => boolean) => keys.filter((key) => !drop(key));
    if (selection.includes(own)) return without(selection, (key) => key === own);

    if (node.kind === 'workspace') {
      const products = new Set(productsOf(tree, node.workspace).map((item) => item.slug));
      return unique([
        ...without(selection, (key) => {
          const entity = EntityKeys.parse(key);
          return entity !== null && entity.kind !== 'workspace' && products.has(entity.product);
        }),
        own,
      ]);
    }

    if (node.kind === 'product') {
      if (selection.includes(workspaceKey))
        return unique([
          ...without(selection, (key) => key === workspaceKey),
          ...productsOf(tree, node.workspace)
            .filter((item) => item.slug !== node.product)
            .map((item) => EntityKeys.product(item.slug)),
        ]);
      return unique([...without(selection, (key) => key.startsWith(`s:${node.product}/`)), own]);
    }

    const productKey = EntityKeys.product(node.product);
    const siblings = servicesOf(tree, node.product)
      .filter((service) => service !== node.service)
      .map((service) => EntityKeys.service(node.product, service));
    if (selection.includes(productKey)) return unique([...without(selection, (key) => key === productKey), ...siblings]);
    if (selection.includes(workspaceKey))
      return unique([
        ...without(selection, (key) => key === workspaceKey),
        ...productsOf(tree, node.workspace)
          .filter((item) => item.slug !== node.product)
          .map((item) => EntityKeys.product(item.slug)),
        ...siblings,
      ]);
    return unique([...selection, own]);
  },

  /** The selection as chips, labelled from the tree; a key the tree lacks keeps its slug. */
  chips(tree: EntityTree, selection: readonly string[]): EntityChip[] {
    const productName = (slug: string) =>
      tree.flatMap((workspace) => workspace.products).find((item) => item.slug === slug)?.name ?? slug;
    return selection.flatMap((key): EntityChip[] => {
      const entity = EntityKeys.parse(key);
      if (!entity) return [];
      if (entity.kind === 'workspace')
        return [{ key, kind: entity.kind, label: tree.find((item) => item.slug === entity.workspace)?.name ?? entity.workspace }];
      if (entity.kind === 'product') return [{ key, kind: entity.kind, label: productName(entity.product) }];
      return [{ key, kind: entity.kind, label: `${productName(entity.product)} · ${entity.service}` }];
    });
  },

  /** Keeps only well-formed keys, each once. */
  normalize(keys: readonly string[]): string[] {
    return unique(keys.filter((key) => EntityKeys.parse(key) !== null));
  },
} as const;
