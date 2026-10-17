import { computed } from "vue";

import { TailwindBreakpoints, useBreakpoint } from "@wow-two-beta/fe/foundation/device";

/** Whether the screen is a phone (below `md`), where a picker opens as a bottom sheet rather than a popover. */
export function useIsPhone() {
  const breakpoint = useBreakpoint(TailwindBreakpoints);
  return computed(() => breakpoint.value === null || breakpoint.value === "sm");
}
