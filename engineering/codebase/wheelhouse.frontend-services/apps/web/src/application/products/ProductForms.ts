import { z } from "zod";
import { defaultMapFieldPath } from "@wow-two-beta/ui-vue/forms-engine";
import { InventoryRules } from "@/domain/common";
import type { Product, ReleaseImage, SaveProductRequest } from "@/domain/products";

/** Represents a product's editable fields, flat; the release source applies only while the product publishes. */
export interface ProductFormModel {
  slug: string;
  name: string;
  description: string;
  repository: string;
  defaultBranch: string;
  publishes: boolean;
  asset: string;
  workflow: string;
  images: ReleaseImage[];
}

/** Validates what the form can judge alone; the host checks the rest and names the field it refuses. */
export const productFormSchema = z
  .object({
    slug: z.string().trim().regex(InventoryRules.slug, "Start with a lowercase letter; use lowercase letters, digits and dashes."),
    name: z.string().trim().min(1, "Enter a name."),
    description: z.string().trim(),
    repository: z.string().trim().regex(InventoryRules.repository, "Use owner/name."),
    defaultBranch: z.string().trim().min(1, "Enter the default branch."),
    publishes: z.boolean(),
    asset: z.string().trim(),
    workflow: z.string().trim(),
    images: z.array(z.object({ service: z.string().trim(), image: z.string().trim() })),
  })
  .superRefine((values, context) => {
    if (values.publishes && !values.asset)
      context.addIssue({ code: "custom", path: ["asset"], message: "Enter the release asset." });
  });

/** Creates detached editing state from a product, or blank for a new one. */
export function productFormOf(product: Product | null): ProductFormModel {
  return {
    slug: product?.slug ?? "",
    name: product?.name ?? "",
    description: product?.description ?? "",
    repository: product?.repository.name ?? "",
    defaultBranch: product?.repository.defaultBranch ?? "main",
    publishes: product?.release != null,
    asset: product?.release?.asset ?? "",
    workflow: product?.release?.workflow ?? "",
    images: product?.release?.images.map((image) => ({ ...image })) ?? [],
  };
}

/** The request a submitted form sends; the slug goes only with a new product. */
export function productRequestOf(values: ProductFormModel, isNew: boolean): SaveProductRequest {
  return {
    ...(isNew ? { slug: values.slug } : {}),
    name: values.name,
    description: values.description,
    repository: values.repository,
    defaultBranch: values.defaultBranch,
    release: values.publishes
      ? { asset: values.asset, workflow: values.workflow || null, images: values.images }
      : null,
  };
}

/** Maps a host error path onto the flat form: `Release.Images[0].Image` lands on `images[0].image`. */
export function productFieldPath(serverPath: string): string {
  return defaultMapFieldPath(serverPath).replace(/^release\./, "");
}
