import { z } from "zod";
import { ProductLifecycle, type Product, type SaveProductRequest } from "@/domain/products";
import { requestData, requestEmpty } from "@/integration/common";

const ProductSchema = z.object({
  slug: z.string(),
  name: z.string(),
  description: z.string(),
  lifecycle: z.enum(ProductLifecycle),
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
        }),
      ),
      secrets: z.object({ vault: z.string(), namespace: z.string() }).nullable(),
    }),
  ),
});

/** The portfolio's products, with validated responses and explicit writes. */
export const productsApi = {
  listProducts: (signal?: AbortSignal) =>
    requestData<Product[]>("/api/products", ProductSchema.array(), { signal }),
  createProduct: (body: SaveProductRequest, signal?: AbortSignal) =>
    requestData<Product>("/api/products", ProductSchema, { method: "POST", body, signal, action: "product" }),
  updateProduct: (slug: string, body: SaveProductRequest, signal?: AbortSignal) =>
    requestData<Product>(`/api/products/${encodeURIComponent(slug)}`, ProductSchema, {
      method: "PUT", body, signal, action: "product",
    }),
  deleteProduct: (slug: string, signal?: AbortSignal) =>
    requestEmpty(`/api/products/${encodeURIComponent(slug)}`, { method: "DELETE", signal, action: "product" }),
  updateLifecycle: (
    slug: string,
    lifecycle: ProductLifecycle,
    signal?: AbortSignal,
  ) =>
    requestData<Product>(
      `/api/products/${encodeURIComponent(slug)}/lifecycle`,
      ProductSchema,
      { method: "PUT", body: { lifecycle }, signal, action: "lifecycle" },
    ),
};
