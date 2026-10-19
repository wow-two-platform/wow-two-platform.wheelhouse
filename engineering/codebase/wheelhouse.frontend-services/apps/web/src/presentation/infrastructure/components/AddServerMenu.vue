<script lang="ts">
/** The Servers page's Add server: enroll a fresh server, or register an existing one. */
export interface AddServerMenuProps {
  readonly isDisabled?: boolean;
}
</script>

<script setup lang="ts">
import { computed, ref, shallowRef } from "vue";
import { ChevronDown, Plus, Rocket, ServerCog } from "lucide-vue-next";
import { Button } from "@wow-two-beta/fe/presentation/actions";
import { DropdownMenu, DropdownMenuContent, DropdownMenuTrigger, MenuItem } from "@wow-two-beta/fe/presentation/nav";
import { useEnrollmentChanges, useTailnet } from "@/application/infrastructure";
import { useServerChanges } from "@/application/servers";
import type { OneTimeKey } from "@/domain/infrastructure";
import type { Server } from "@/domain/servers";
import EnrollmentModal from "./EnrollmentModal.vue";
import FreshServerModal, { type FreshServerCreated } from "./FreshServerModal.vue";

/**
 * Adds a server one of two ways: enroll a fresh one — its public IP, then the enrollment dialog with its one-time key —
 * or register an existing one through the full server form, which the page owns (`register`). On deployed dev,
 * enrollment runs on prod only, so the first way is off.
 */
defineOptions({ name: "AddServerMenu" });
const props = withDefaults(defineProps<AddServerMenuProps>(), { isDisabled: false });
const emit = defineEmits<{ register: [] }>();
defineSlots<{}>();

const tailnet = useTailnet();
const servers = useServerChanges();
const enrollment = useEnrollmentChanges();
const fresh = ref(false);
const enrolling = shallowRef<{ open: boolean; server: Server | null; oneTimeKey: OneTimeKey | null }>({
  open: false,
  server: null,
  oneTimeKey: null,
});
const blocked = computed(() =>
  tailnet.data.value?.prodOnly ? (tailnet.data.value.reason ?? "Enrollment runs on prod only.") : null,
);

/** Opens the enrollment dialog on the server just registered. @internal */
function created(value: FreshServerCreated): void {
  enrolling.value = { open: true, server: value.server, oneTimeKey: value.oneTimeKey };
}
</script>

<template>
  <DropdownMenu placement="bottom-end">
    <DropdownMenuTrigger as-child>
      <Button :is-disabled="props.isDisabled || fresh || enrolling.open">
        <template #leading><Plus :size="16" /></template>Add server<ChevronDown :size="14" aria-hidden="true" />
      </Button>
    </DropdownMenuTrigger>
    <DropdownMenuContent aria-label="Add server" class="w-64">
      <MenuItem :is-disabled="Boolean(blocked)" :title="blocked ?? undefined" @select="fresh = true"
        ><Rocket :size="15" />Enroll a fresh server</MenuItem
      >
      <MenuItem @select="emit('register')"><ServerCog :size="15" />Register an existing server</MenuItem>
    </DropdownMenuContent>
  </DropdownMenu>
  <FreshServerModal
    :open="fresh"
    :create="servers.create"
    :create-key="enrollment.createKey"
    @update:open="fresh = $event"
    @created="created"
  />
  <EnrollmentModal
    :open="enrolling.open"
    :server="enrolling.server"
    :one-time-key="enrolling.oneTimeKey"
    :blocked="blocked"
    :operations="enrollment"
    @update:open="(open) => (enrolling = { ...enrolling, open })"
  />
</template>
