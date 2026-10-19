<script setup lang="ts">
import { Database } from "lucide-vue-next";

import { InfoPopover, type InfoSection } from "@/presentation/common/components";

/**
 * What the Services panel's info button holds: the map's line legend and what its lines do not measure, how to move
 * around the map, and what the list reads.
 */
defineOptions({ name: "ServicesInfo" });
defineSlots<{}>();

const Sections: readonly InfoSection[] = [
  { key: "map", title: "Map" },
  { key: "moving", title: "Moving around" },
  { key: "list", title: "List" },
];
</script>

<template>
  <InfoPopover title="Services" :sections="Sections">
    <template #map>
      <p>
        The services the verified release declares, with their networks, sites and the platform services they need. The
        buttons above the map switch each layer on and off.
      </p>
      <ul class="mt-2.5 flex flex-col gap-1.5 text-xs" aria-label="Lines">
        <li class="flex items-center gap-2"><span class="h-px w-5 shrink-0 bg-primary" aria-hidden="true" />Shared network</li>
        <li class="flex items-center gap-2">
          <span class="w-5 shrink-0 border-t border-dashed border-accent" aria-hidden="true" />A → B: A waits for B to start
        </li>
        <li class="flex items-center gap-2"><span class="h-px w-5 shrink-0 bg-success" aria-hidden="true" />Site the ingress routes</li>
        <li class="flex items-center gap-2">
          <span class="h-px w-5 shrink-0 bg-muted-foreground" aria-hidden="true" />Platform service it needs
        </li>
        <li class="flex items-center gap-2">
          <Database :size="12" class="w-5 shrink-0 text-info" aria-hidden="true" />Named volume
        </li>
      </ul>
      <p class="mt-2.5">Lines show Compose membership and startup order; application traffic is not measured.</p>
    </template>
    <template #moving>
      <p>Drag to move · Pinch or Ctrl + scroll to zoom · Select a service for its details.</p>
    </template>
    <template #list>
      <p>
        Each running container with its health, restarts, CPU and memory, read from the host every minute, and the
        host's own load and memory. Readings are observational: a deploy always updates the whole environment.
      </p>
    </template>
  </InfoPopover>
</template>
