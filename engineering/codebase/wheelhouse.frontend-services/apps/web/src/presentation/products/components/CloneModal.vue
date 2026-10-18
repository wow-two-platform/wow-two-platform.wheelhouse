<script lang="ts">
/** The dialog that clones a product's repository. */
export interface CloneModalProps {
  /** The open state — the `v-model:open` target. */
  readonly open: boolean;
  /** The GitHub repository, `owner/name`. */
  readonly repository: string;
  /** The runner catalog product for this repository; branches and commits are listed only when there is one. */
  readonly runnerProduct: string | null;
}

/** @internal The clone command shapes the owner can copy. */
const CloneFormats = {
  https: "HTTPS",
  ssh: "SSH",
  gh: "GitHub CLI",
} as const;
type CloneFormat = keyof typeof CloneFormats;
</script>
<script setup lang="ts">
import { computed, ref, watch } from "vue";
import { Check, ExternalLink, GitBranch, GitCommitHorizontal } from "lucide-vue-next";
import { Button, CopyButton } from "@wow-two-beta/fe/presentation/actions";
import { Spinner } from "@wow-two-beta/fe/presentation/feedback";
import { ToggleGroup, ToggleInput } from "@wow-two-beta/fe/presentation/forms";
import {
  Modal,
  ModalBody,
  ModalContent,
  ModalDescription,
  ModalFooter,
  ModalHeader,
  ModalTitle,
} from "@wow-two-beta/fe/presentation/overlays";
import { useProductBranches, useProductCommits } from "@/application/deployments";

/**
 * Copies a clone command for the repository — HTTPS, SSH or the GitHub CLI — pointed at the default branch, a chosen
 * branch or a chosen commit, and opens that same ref on GitHub. Branches and commits are read only while the dialog
 * is open, so a product page costs no GitHub calls until asked.
 */
defineOptions({ name: "CloneModal" });
const props = defineProps<CloneModalProps>();
const emit = defineEmits<{ "update:open": [open: boolean] }>();
defineSlots<{}>();

const format = ref<CloneFormat>("https");
const mode = ref<"branch" | "commit">("branch");
const branch = ref<string | null>(null);
const commit = ref<{ sha: string; message: string } | null>(null);
const branches = useProductBranches(() => (props.open ? props.runnerProduct : null));
const commits = useProductCommits(
  () => (props.open && mode.value === "commit" ? props.runnerProduct : null),
  () => branch.value ?? "main",
);

const repositoryUrl = computed(() => `https://github.com/${props.repository}`);
const folder = computed(() => props.repository.split("/")[1] ?? props.repository);
const remote = computed(() =>
  format.value === "ssh" ? `git@github.com:${props.repository}.git` : `${repositoryUrl.value}.git`,
);
/** The clone command for the chosen format and ref. */
const command = computed(() => {
  const clone = format.value === "gh" ? `gh repo clone ${props.repository}` : `git clone ${remote.value}`;
  if (commit.value) return `${clone} && git -C ${folder.value} checkout ${commit.value.sha.slice(0, 12)}`;
  if (!branch.value) return clone;
  return format.value === "gh"
    ? `${clone} -- --branch ${branch.value}`
    : `git clone --branch ${branch.value} ${remote.value}`;
});
/** Where the open action goes: the commit, the branch, or the repository. */
const openUrl = computed(() => {
  if (commit.value) return `${repositoryUrl.value}/commit/${commit.value.sha}`;
  if (branch.value) return `${repositoryUrl.value}/tree/${branch.value.split("/").map(encodeURIComponent).join("/")}`;
  return repositoryUrl.value;
});

/** A different product starts from its default branch. */
watch(
  () => props.repository,
  () => {
    branch.value = null;
    commit.value = null;
  },
);

/** Points both actions at a branch; a commit pick is cleared because it belonged to the old branch. */
function chooseBranch(name: string | null): void {
  branch.value = name;
  commit.value = null;
}

function setFormat(value: unknown): void {
  if (value === "https" || value === "ssh" || value === "gh") format.value = value;
}

function setMode(value: unknown): void {
  if (value === "branch" || value === "commit") mode.value = value;
}
</script>
<template>
  <Modal :open="props.open" @update:open="emit('update:open', $event)">
    <ModalContent size="md">
      <ModalHeader>
        <ModalTitle>Clone {{ folder }}</ModalTitle>
        <ModalDescription>{{ props.repository }}</ModalDescription>
      </ModalHeader>
      <ModalBody class="flex flex-col gap-3 text-sm">
        <ToggleGroup variant="segmented" aria-label="Clone format" :model-value="format" @update:model-value="setFormat">
          <ToggleInput v-for="(label, key) in CloneFormats" :key="key" :value="key" size="sm" class="max-md:min-h-9">{{
            label
          }}</ToggleInput>
        </ToggleGroup>
        <div class="flex items-start gap-2 rounded-md bg-muted p-2 font-mono text-xs">
          <span class="min-w-0 flex-1 break-all">{{ command }}</span>
          <CopyButton :text="command" size="sm" aria-label="Copy the clone command" copied-aria-label="Clone command copied" />
        </div>
        <template v-if="props.runnerProduct">
          <ToggleGroup variant="segmented" aria-label="Point at" :model-value="mode" @update:model-value="setMode">
            <ToggleInput value="branch" size="sm" class="max-md:min-h-9"
              ><GitBranch :size="14" aria-hidden="true" /><span class="ml-1">Branch</span></ToggleInput
            >
            <ToggleInput value="commit" size="sm" class="max-md:min-h-9"
              ><GitCommitHorizontal :size="14" aria-hidden="true" /><span class="ml-1">Commit</span></ToggleInput
            >
          </ToggleGroup>
          <div class="max-h-60 overflow-y-auto rounded-md border border-border p-1" role="listbox" :aria-label="mode === 'branch' ? 'Branches' : 'Commits'">
            <div v-if="(mode === 'branch' ? branches : commits).isFirstLoad.value" class="p-2">
              <Spinner size="sm" label="Reading GitHub" />
            </div>
            <p v-else-if="(mode === 'branch' ? branches : commits).error.value" class="p-2 text-xs text-destructive">
              GitHub could not be read.
            </p>
            <template v-else-if="mode === 'branch'">
              <button
                type="button"
                role="option"
                :aria-selected="branch === null"
                class="flex min-h-9 w-full items-center rounded-md px-2 text-left hover:bg-muted"
                @click="chooseBranch(null)"
              >
                <span class="flex-1">Default branch</span>
                <Check v-if="branch === null" :size="14" />
              </button>
              <button
                v-for="name in branches.data.value ?? []"
                :key="name"
                type="button"
                role="option"
                :aria-selected="branch === name"
                class="flex min-h-9 w-full items-center rounded-md px-2 text-left font-mono text-xs hover:bg-muted"
                @click="chooseBranch(name)"
              >
                <span class="min-w-0 flex-1 truncate">{{ name }}</span>
                <Check v-if="branch === name" :size="14" />
              </button>
            </template>
            <template v-else>
              <button
                v-for="entry in (commits.data.value ?? []).slice(0, 15)"
                :key="entry.sha"
                type="button"
                role="option"
                :aria-selected="commit?.sha === entry.sha"
                class="flex min-h-9 w-full items-baseline gap-2 rounded-md px-2 py-1.5 text-left hover:bg-muted"
                @click="commit = { sha: entry.sha, message: entry.message }"
              >
                <span class="shrink-0 font-mono text-xs">{{ entry.sha.slice(0, 7) }}</span>
                <span class="min-w-0 flex-1 truncate text-xs text-muted-foreground" :title="entry.message">{{
                  entry.message
                }}</span>
                <Check v-if="commit?.sha === entry.sha" :size="14" class="shrink-0 self-center" />
              </button>
            </template>
          </div>
        </template>
      </ModalBody>
      <ModalFooter class="sm:justify-between">
        <Button as-child variant="outline" tone="neutral"
          ><a :href="openUrl" target="_blank" rel="noopener noreferrer"
            ><ExternalLink :size="16" aria-hidden="true" />Open on GitHub</a
          ></Button
        >
        <Button @click="emit('update:open', false)">Done</Button>
      </ModalFooter>
    </ModalContent>
  </Modal>
</template>
