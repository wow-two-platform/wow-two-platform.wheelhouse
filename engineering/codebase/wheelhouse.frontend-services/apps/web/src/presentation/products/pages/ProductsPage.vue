<script setup lang="ts">
import { ref } from "vue";
import { Plus } from "lucide-vue-next";
import { Button } from "@wow-two-beta/fe/presentation/actions";
import { PageHeaderActions } from "@wow-two-beta/fe/presentation/layout";
import ProductsPanel from "../components/ProductsPanel.vue";

/** Renders the products route with its toolbar action. */
defineOptions({ name: "ProductsPage" });
const creating = ref(false);
</script>

<template>
  <PageHeaderActions>
    <Button :is-disabled="creating" @click="creating = true">
      <template #leading><Plus :size="16" /></template>New product
    </Button>
  </PageHeaderActions>
  <ProductsPanel v-model:creating="creating" />
</template>
