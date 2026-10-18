<script lang="ts">
import type { Product } from "@/domain/products";

/** The control that opens a product's sites. */
export interface OpenSiteMenuProps {
  readonly product: Product;
  /** `sm` in a card or row; the trigger grows to the touch target on phones either way. */
  readonly size?: "sm" | "md";
  /** Shows "Open site" beside the icon, as the product page's header does. */
  readonly showText?: boolean;
  /** Ghost in a card or row; outline beside the header's other actions. */
  readonly variant?: "ghost" | "outline";
}
</script>
<script setup lang="ts">
import { computed, ref } from "vue";
import { ChevronDown, ExternalLink, Globe, Lock } from "lucide-vue-next";
import { Button } from "@wow-two-beta/fe/presentation/actions";
import { Popover, PopoverContent, PopoverTrigger } from "@wow-two-beta/fe/presentation/overlays";
import { directSiteOf, siteLinksOf } from "@/domain/portfolio";
import { EnvironmentOrder } from "@/domain/targets";

/**
 * Opens a product's front-facing sites. One address — its only site, or its homepage when it publishes none — is a
 * plain link; several open a compact list: one row per site, public before private (marked with a lock), each with a
 * dev and a prod button that open that environment's address in a new tab. An environment that does not publish the
 * site keeps an empty slot, so the buttons line up. Nothing renders for a product with no address at all.
 */
defineOptions({ name: "OpenSiteMenu" });
const props = withDefaults(defineProps<OpenSiteMenuProps>(), { size: "sm", showText: false, variant: "ghost" });
defineSlots<{}>();
const open = ref(false);
const links = computed(() => siteLinksOf(props.product));
const direct = computed(() => directSiteOf(props.product, links.value));
const hasAny = computed(() => Boolean(direct.value) || links.value.length > 0);
const sizeClass = computed(() => (props.showText ? "max-md:min-h-11" : "max-md:size-11"));
</script>
<template>
  <template v-if="hasAny">
    <Button
      v-if="direct"
      as-child
      :size="props.size"
      :variant="props.variant"
      tone="neutral"
      :shape="props.showText ? 'default' : 'square'"
      :class="sizeClass"
    >
      <a
        :href="direct.href"
        target="_blank"
        rel="noopener noreferrer"
        :aria-label="props.showText ? undefined : `Open ${direct.host}`"
        :title="`Open ${direct.host}`"
        ><ExternalLink :size="props.showText ? 14 : 15" aria-hidden="true" /><span v-if="props.showText"
          >Open site</span
        ></a
      >
    </Button>
    <Popover v-else v-model:open="open" placement="bottom-end">
      <PopoverTrigger as-child>
        <Button
          :size="props.size"
          :variant="props.variant"
          tone="neutral"
          :shape="props.showText ? 'default' : 'square'"
          :class="sizeClass"
          :aria-label="props.showText ? undefined : `Open a site of ${props.product.name}`"
          :title="`Open a site of ${props.product.name}`"
          ><ExternalLink :size="props.showText ? 14 : 15" aria-hidden="true" /><template v-if="props.showText"
            >Open site<ChevronDown :size="14" class="text-muted-foreground" aria-hidden="true" /></template
        ></Button>
      </PopoverTrigger>
      <PopoverContent class="w-[min(20rem,calc(100vw-2rem))] p-1.5 text-sm" :aria-label="`Sites of ${props.product.name}`">
        <p class="px-2 pt-1 pb-1.5 text-xs font-semibold text-muted-foreground">Open a site</p>
        <ul role="list" class="flex flex-col">
          <li
            v-for="link in links"
            :key="link.name"
            class="flex min-h-10 items-center gap-2 rounded-lg px-2 hover:bg-muted/60"
          >
            <component
              :is="link.private ? Lock : Globe"
              :size="15"
              class="shrink-0 text-muted-foreground"
              aria-hidden="true"
            />
            <span class="min-w-0 flex-1">
              <span class="block truncate font-medium">{{ link.name }}</span>
              <span class="block truncate text-xs text-muted-foreground">{{
                link.private ? "Private network" : (link.addresses["prod"] ?? Object.values(link.addresses)[0])?.host
              }}</span>
            </span>
            <template v-for="environment in EnvironmentOrder" :key="environment">
              <a
                v-if="link.addresses[environment]"
                :href="link.addresses[environment]!.href"
                target="_blank"
                rel="noopener noreferrer"
                :title="link.addresses[environment]!.host"
                :aria-label="`Open ${link.name} on ${environment}: ${link.addresses[environment]!.host}`"
                class="inline-flex h-7 w-14 shrink-0 items-center justify-center gap-1 rounded-full border border-border bg-card text-xs font-semibold hover:border-primary/40 hover:text-primary focus-visible:outline-2 focus-visible:outline-ring max-md:h-9"
                @click="open = false"
                >{{ environment }}<ExternalLink :size="11" class="text-muted-foreground" aria-hidden="true"
              /></a>
              <span
                v-else
                class="inline-flex h-7 w-14 shrink-0 items-center justify-center rounded-full border border-dashed border-border text-xs text-subtle-foreground max-md:h-9"
                :title="`${link.name} has no ${environment} address`"
                ><span class="sr-only">No {{ environment }} address</span><span aria-hidden="true">{{ environment }}</span></span
              >
            </template>
          </li>
        </ul>
      </PopoverContent>
    </Popover>
  </template>
</template>
