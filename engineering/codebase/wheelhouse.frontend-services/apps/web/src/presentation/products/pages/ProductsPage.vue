<script setup lang="ts">
import { ref } from "vue";
import { Plus } from "lucide-vue-next";
import { Button } from "@wow-two-beta/fe/presentation/actions";
import { PageHeaderActions } from "@wow-two-beta/fe/presentation/layout";
import ProductsPanel from "../components/ProductsPanel.vue";
import WorkspacesPanel from "../components/WorkspacesPanel.vue";

/** Renders the registry: the products with their definitions and environments, then the workspaces that group them. */
defineOptions({ name: "ProductsPage" });
const creating = ref(false);
</script>

<template>
  <PageHeaderActions>
    <Button :is-disabled="creating" @click="creating = true">
      <template #leading><Plus :size="16" /></template>New product
    </Button>
  </PageHeaderActions>
  <div class="flex flex-col gap-6">
    <ProductsPanel v-model:creating="creating" />
    <WorkspacesPanel />
  </div>
</template>
