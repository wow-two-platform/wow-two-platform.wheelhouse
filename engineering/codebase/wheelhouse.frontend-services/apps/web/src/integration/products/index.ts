import { z } from "zod";
import {
  ProductDeploy,
  ProductIdentitySource,
  ProductStatus,
  type Product,
  type ProductDescriptor,
  type SaveProductRequest,
} from "@/domain/products";
import { requestAccepted, requestData, requestEmpty } from "@/integration/common";

const ProductSchema = z.object({
  id: z.string().optional(),
  slug: z.string(),
  name: z.string(),
  description: z.string(),
  homepage: z.string().nullable(),
  identity: z.object({
    source: z.enum(ProductIdentitySource),
    commit: z.string().nullable(),
    problem: z.string().nullable(),
    services: z.array(
      z.object({ service: z.string(), name: z.string().nullable(), description: z.string().nullable() }),
    ),
  }),
  workspaceId: z.string().nullable(),
  status: z.enum(ProductStatus),
  // A host before the stored mode sends none; every product it deploys is Wheelhouse's.
  deploy: z.enum(ProductDeploy).default(ProductDeploy.Wheelhouse),
  inactiveServices: z.array(z.string()),
  repository: z.object({
    name: z.string(),
    url: z.string(),
    defaultBranch: z.string(),
  }),
  release: z
    .object({
      asset: z.string(),
      workflow: z.string().nullable(),
      images: z.array(z.object({ service: z.string(), image: z.string() })),
    })
    .nullable(),
  iconUrl: z.string(),
  environments: z.array(
    z.object({
      name: z.string(),
      sites: z.array(
        z.object({
          name: z.string(),
          url: z.string(),
          exposure: z.enum(["public", "private"]),
          primary: z.boolean().default(false),
        }),
      ),
      secrets: z.object({ vault: z.string(), namespace: z.string() }).nullable(),
    }),
  ),
});

/** What a product's deploy.yml declares; a descriptor that cannot be read comes with its reason and nothing else. */
export const ProductDescriptorSchema = z.object({
  commit: z.string().nullable(),
  problem: z.string().nullable(),
  services: z.array(z.object({ service: z.string(), needs: z.array(z.string()).default([]) })).default([]),
  sites: z
    .array(z.object({ name: z.string(), exposure: z.enum(["public", "private"]), primary: z.boolean().default(false) }))
    .default([]),
});

/** A product's address segment: the routes take its identifier or its slug. @internal */
const productPath = (product: string) => `/api/products/${encodeURIComponent(product)}`;

/** The portfolio's products, with validated responses and explicit writes. */
export const productsApi = {
  listProducts: (signal?: AbortSignal) =>
    requestData<Product[]>("/api/products", ProductSchema.array(), { signal }),
  createProduct: (body: SaveProductRequest, signal?: AbortSignal) =>
    requestData<Product>("/api/products", ProductSchema, { method: "POST", body, signal, action: "product" }),
  updateProduct: (slug: string, body: SaveProductRequest, signal?: AbortSignal) =>
    requestData<Product>(productPath(slug), ProductSchema, {
      method: "PUT", body, signal, action: "product",
    }),
  deleteProduct: (slug: string, signal?: AbortSignal) =>
    requestEmpty(productPath(slug), { method: "DELETE", signal, action: "product" }),
  /** Reads the product's manifest again now; the host checks the branch head without waiting for the remembered one. */
  refreshIdentity: (slug: string, signal?: AbortSignal) =>
    requestData<Product>(`${productPath(slug)}/identity/refresh`, ProductSchema, {
      method: "POST", signal, action: "refresh",
    }),
  /** Moves a product into another workspace; `product` is its identifier or slug. */
  moveToWorkspace: (product: string, workspaceId: string, signal?: AbortSignal) =>
    requestAccepted(`${productPath(product)}/workspace`, {
      method: "PUT", body: { workspaceId }, signal, action: "workspace",
    }),
  /** What the product's deploy.yml declares at its branch head: services, their needs and sites. */
  getDescriptor: (product: string, signal?: AbortSignal) =>
    requestData<ProductDescriptor>(`${productPath(product)}/descriptor`, ProductDescriptorSchema, { signal }),
  /** Marks a product active or inactive. */
  updateStatus: (product: string, status: ProductStatus, signal?: AbortSignal) =>
    requestAccepted(`${productPath(product)}/status`, {
      method: "PUT", body: { status }, signal, action: "status",
    }),
  /** Marks one service of a product active or inactive. */
  updateServiceStatus: (product: string, service: string, status: ProductStatus, signal?: AbortSignal) =>
    requestAccepted(`${productPath(product)}/services/${encodeURIComponent(service)}/status`, {
      method: "PUT", body: { status }, signal, action: "status",
    }),
};
