<script lang="ts">
import type { TargetCondition } from "@/domain/deployments";

/** Defines a target readiness badge. */
export interface ConditionBadgeProps {
  /** The target's deployment readiness condition. */
  readonly condition: TargetCondition;
}
</script>

<script setup lang="ts">
import { Badge } from "@wow-two-beta/fe/presentation/display";

import { DeploymentExtensions } from "@/domain/deployments";

/** Renders a target's readiness condition separately from container health. */
defineOptions({ name: "ConditionBadge" });
const props = defineProps<ConditionBadgeProps>();
</script>

<template>
  <Badge :variant="DeploymentExtensions.conditionVariant(props.condition)">
    {{ DeploymentExtensions.label(props.condition) }}
  </Badge>
</template>
