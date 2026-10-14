import { computed, ref } from "vue";
import { monogramHue, monogramOf, productIconUrl } from "@/domain/products";
import type { IconColor, IconForm, IconTheme } from "@/domain/products";

/** What a product icon reads: each value is a getter, so the icon follows the product and the theme as they change. */
export interface ProductIconSource {
  /** The catalog slug; without one (a product outside the catalog) only the monogram shows. */
  readonly slug: () => string | null | undefined;
  /** A service of the product, whose own icon is preferred. */
  readonly service?: () => string | null | undefined;
  /** The product's name, for the monogram and its tint. */
  readonly name: () => string;
  /** The theme of the active color mode; the icon is requested again when it changes. */
  readonly theme: () => IconTheme;
  readonly form?: () => IconForm | undefined;
  readonly color?: () => IconColor | undefined;
}

/**
 * Decides what a product's mark shows: the icon its repository carries for the active theme, else a monogram. The
 * address names the theme, so a theme change asks again; an address the host answered not found stays the monogram
 * until the product, service, theme, form or color changes.
 */
export function useProductIcon(source: ProductIconSource) {
  const failedUrl = ref<string | null>(null);
  const url = computed(() => {
    const slug = source.slug();
    return slug
      ? productIconUrl(slug, {
          service: source.service?.(),
          theme: source.theme(),
          form: source.form?.(),
          color: source.color?.(),
        })
      : null;
  });
  return {
    /** The icon's address, or `null` when the monogram shows. */
    src: computed(() => (url.value !== null && failedUrl.value !== url.value ? url.value : null)),
    monogram: computed(() => monogramOf(source.name())),
    hue: computed(() => monogramHue(source.name())),
    /** Records that the current address did not answer an image. */
    failed: () => {
      failedUrl.value = url.value;
    },
  };
}
