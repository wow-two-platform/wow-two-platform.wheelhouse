<script setup lang="ts">
import { ref } from "vue";
import { Plus } from "lucide-vue-next";
import { Button } from "@wow-two-beta/ui-vue/presentation/actions";
import { PageActions } from "@/presentation/common/components";
import ProductsPanel from "../components/ProductsPanel.vue";

/** Renders the products route with its toolbar action. */
defineOptions({ name: "ProductsPage" });
const creating = ref(false);
</script>

<template>
  <PageActions>
    <Button :is-disabled="creating" @click="creating = true">
      <template #leading><Plus :size="16" /></template>New product
    </Button>
  </PageActions>
  <ProductsPanel v-model:creating="creating" />
</template>
