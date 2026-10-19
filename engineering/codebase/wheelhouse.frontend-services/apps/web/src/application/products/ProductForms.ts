import { z } from "zod";
import { defaultMapFieldPath } from "@wow-two-beta/fe/forms-engine";
import { InventoryRules } from "@/domain/common";
import { ProductDeploy, ProductStatus, type Product, type ReleaseImage, type SaveProductRequest } from "@/domain/products";

/**
 * Represents a product's editable fields, flat; the release source applies only while the product publishes. The
 * workspace and the status save through their own requests once the product itself is saved.
 */
export interface ProductFormModel {
  slug: string;
  /** The workspace the product belongs to; null leaves a new product in the host's default workspace. */
  workspaceId: string | null;
  active: boolean;
  /** What deploys the product: Wheelhouse, its own CI or nothing. */
  deploy: ProductDeploy;
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
    workspaceId: z.string().nullable(),
    active: z.boolean(),
    deploy: z.enum(ProductDeploy),
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

/** Creates detached editing state from a product, or blank for a new one in the given workspace. */
export function productFormOf(product: Product | null, workspaceId: string | null = null): ProductFormModel {
  return {
    slug: product?.slug ?? "",
    workspaceId: product ? product.workspaceId : workspaceId,
    active: product ? product.status === ProductStatus.Active : true,
    deploy: product?.deploy ?? ProductDeploy.Wheelhouse,
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

/** The request a submitted form sends; the slug and the workspace go only with a new product. */
export function productRequestOf(values: ProductFormModel, isNew: boolean): SaveProductRequest {
  return {
    ...(isNew ? { slug: values.slug } : {}),
    ...(isNew && values.workspaceId ? { workspaceId: values.workspaceId } : {}),
    name: values.name,
    description: values.description,
    repository: values.repository,
    defaultBranch: values.defaultBranch,
    deploy: values.deploy,
    release: values.publishes
      ? { asset: values.asset, workflow: values.workflow || null, images: values.images }
      : null,
  };
}

/** Maps a host error path onto the flat form: `Release.Images[0].Image` lands on `images[0].image`. */
export function productFieldPath(serverPath: string): string {
  return defaultMapFieldPath(serverPath).replace(/^release\./, "");
}

/** What a saved product still needs after its own request: a move to another workspace, a status change, or both. */
export function productFollowUpsOf(
  values: ProductFormModel,
  saved: Pick<Product, "workspaceId" | "status">,
): { workspaceId: string | null; status: ProductStatus | null } {
  const status = values.active ? ProductStatus.Active : ProductStatus.Inactive;
  return {
    workspaceId: values.workspaceId && values.workspaceId !== saved.workspaceId ? values.workspaceId : null,
    status: status !== saved.status ? status : null,
  };
}
