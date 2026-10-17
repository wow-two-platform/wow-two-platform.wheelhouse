<script lang="ts">
/** A product's source repository and the three things an operator does with it. */
export interface RepositoryActionsProps {
  /** The GitHub repository, `owner/name`. */
  readonly repository: string;
  /** The runner catalog product for this repository; branches and commits are listed only when there is one. */
  readonly runnerProduct: string | null;
}

/** @internal The clone command shapes an operator can copy. */
const CloneFormats = {
  https: "HTTPS",
  ssh: "SSH",
  gh: "GitHub CLI",
} as const;
type CloneFormat = keyof typeof CloneFormats;
</script>
<script setup lang="ts">
import { computed, ref, watch } from "vue";
import {
  Check,
  ChevronDown,
  Copy,
  ExternalLink,
  GitBranch,
  GitCommitHorizontal,
} from "lucide-vue-next";
import { Button, CopyButton } from "@wow-two-beta/fe/presentation/actions";
import {
  Popover,
  PopoverContent,
  PopoverTrigger,
} from "@wow-two-beta/fe/presentation/overlays";
import { Spinner } from "@wow-two-beta/fe/presentation/feedback";
import {
  useProductBranches,
  useProductCommits,
} from "@/application/deployments";

/**
 * Opens the repository (or the chosen branch or commit) on GitHub, copies a clone command for it, and lets the
 * operator pick the branch or commit both actions point at.
 */
defineOptions({ name: "RepositoryActions" });
const props = defineProps<RepositoryActionsProps>();
defineSlots<{}>();

const open = ref(false);
const format = ref<CloneFormat>("https");
const mode = ref<"branch" | "commit">("branch");
const branch = ref<string | null>(null);
const commit = ref<{ sha: string; message: string } | null>(null);
/* Read only while the chooser is open, so a product page costs no GitHub calls until asked. */
const branches = useProductBranches(() =>
  open.value ? props.runnerProduct : null,
);
const commits = useProductCommits(
  () => (open.value && mode.value === "commit" ? props.runnerProduct : null),
  () => branch.value ?? "main",
);

const repositoryUrl = computed(() => `https://github.com/${props.repository}`);
const folder = computed(
  () => props.repository.split("/")[1] ?? props.repository,
);
const remote = computed(() =>
  format.value === "ssh"
    ? `git@github.com:${props.repository}.git`
    : `${repositoryUrl.value}.git`,
);
/** The clone command for the chosen format and ref. */
const command = computed(() => {
  const clone =
    format.value === "gh"
      ? `gh repo clone ${props.repository}`
      : `git clone ${remote.value}`;
  if (commit.value)
    return `${clone} && git -C ${folder.value} checkout ${commit.value.sha.slice(0, 12)}`;
  if (!branch.value) return clone;
  return format.value === "gh"
    ? `${clone} -- --branch ${branch.value}`
    : `git clone --branch ${branch.value} ${remote.value}`;
});
/** Where the open action goes: the commit, the branch, or the repository. */
const openUrl = computed(() => {
  if (commit.value) return `${repositoryUrl.value}/commit/${commit.value.sha}`;
  if (branch.value)
    return `${repositoryUrl.value}/tree/${branch.value.split("/").map(encodeURIComponent).join("/")}`;
  return repositoryUrl.value;
});
const refLabel = computed(() =>
  commit.value ? commit.value.sha.slice(0, 7) : branch.value,
);

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
</script>
<template>
  <div class="flex flex-wrap items-center gap-2">
    <a
      :href="openUrl"
      target="_blank"
      rel="noopener noreferrer"
      class="inline-flex min-w-0 items-center gap-2 rounded-md font-medium hover:text-primary hover:underline focus-visible:outline-2 focus-visible:outline-ring"
      :title="`Open ${openUrl} on GitHub`"
    >
      <GitBranch :size="16" class="shrink-0 text-muted-foreground" />
      <span class="break-all">{{ props.repository }}</span>
      <span
        v-if="refLabel"
        class="rounded bg-muted px-1.5 py-0.5 font-mono text-xs font-normal"
        >{{ refLabel }}</span
      >
      <ExternalLink :size="14" class="shrink-0 text-muted-foreground" />
    </a>
    <div class="flex items-center">
      <CopyButton
        :text="command"
        variant="outline"
        size="sm"
        class="rounded-r-none"
        aria-label="Copy the clone command"
        copied-aria-label="Clone command copied"
      >
        <template #default="{ copied }">
          <Check v-if="copied" :size="14" /><Copy v-else :size="14" />{{
            copied ? "Copied" : "Clone"
          }}
        </template>
      </CopyButton>
      <Popover v-model:open="open" placement="bottom-end">
        <PopoverTrigger as-child>
          <Button
            variant="outline"
            tone="neutral"
            size="sm"
            class="-ml-px rounded-l-none px-2"
            aria-label="Clone format, branch and commit"
          >
            <ChevronDown :size="14" />
          </Button>
        </PopoverTrigger>
        <PopoverContent class="w-[min(24rem,calc(100vw-2rem))] p-3 text-sm">
          <div class="flex gap-1" role="group" aria-label="Clone format">
            <Button
              v-for="(label, key) in CloneFormats"
              :key="key"
              size="sm"
              :variant="format === key ? 'soft' : 'ghost'"
              tone="neutral"
              :aria-pressed="format === key"
              @click="format = key"
              >{{ label }}</Button
            >
          </div>
          <div
            class="mt-2 flex items-start gap-2 rounded-md bg-muted p-2 font-mono text-xs"
          >
            <span class="min-w-0 flex-1 break-all">{{ command }}</span>
            <CopyButton
              :text="command"
              size="sm"
              aria-label="Copy the clone command"
              copied-aria-label="Clone command copied"
            />
          </div>
          <template v-if="props.runnerProduct">
            <div
              class="mt-3 flex gap-1 border-t border-border pt-3"
              role="group"
              aria-label="Point at"
            >
              <Button
                size="sm"
                :variant="mode === 'branch' ? 'soft' : 'ghost'"
                tone="neutral"
                :aria-pressed="mode === 'branch'"
                @click="mode = 'branch'"
                ><template #leading><GitBranch :size="14" /></template
                >Branch</Button
              >
              <Button
                size="sm"
                :variant="mode === 'commit' ? 'soft' : 'ghost'"
                tone="neutral"
                :aria-pressed="mode === 'commit'"
                @click="mode = 'commit'"
                ><template #leading><GitCommitHorizontal :size="14" /></template
                >Commit</Button
              >
            </div>
            <div class="mt-2 max-h-60 overflow-y-auto" role="listbox" :aria-label="mode === 'branch' ? 'Branches' : 'Commits'">
              <div
                v-if="(mode === 'branch' ? branches : commits).isFirstLoad.value"
                class="py-2"
              >
                <Spinner size="sm" label="Reading GitHub" />
              </div>
              <p
                v-else-if="(mode === 'branch' ? branches : commits).error.value"
                class="py-2 text-xs text-destructive"
              >
                GitHub could not be read.
              </p>
              <template v-else-if="mode === 'branch'">
                <button
                  type="button"
                  role="option"
                  :aria-selected="branch === null"
                  class="flex w-full items-center rounded-md px-2 py-1.5 text-left hover:bg-muted"
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
                  class="flex w-full items-center rounded-md px-2 py-1.5 text-left font-mono text-xs hover:bg-muted"
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
                  class="flex w-full items-baseline gap-2 rounded-md px-2 py-1.5 text-left hover:bg-muted"
                  @click="commit = { sha: entry.sha, message: entry.message }"
                >
                  <span class="shrink-0 font-mono text-xs">{{
                    entry.sha.slice(0, 7)
                  }}</span>
                  <span
                    class="min-w-0 flex-1 truncate text-xs text-muted-foreground"
                    :title="entry.message"
                    >{{ entry.message }}</span
                  >
                  <Check
                    v-if="commit?.sha === entry.sha"
                    :size="14"
                    class="shrink-0 self-center"
                  />
                </button>
              </template>
            </div>
          </template>
        </PopoverContent>
      </Popover>
    </div>
  </div>
</template>
