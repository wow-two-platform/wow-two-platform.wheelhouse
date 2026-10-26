import { ServerExtensions, type ContainerVitals } from "@/domain/servers";
import { ServiceControls, type TargetStop } from "@/domain/deployments/controls";

/** The observed status remains separate from the saved Compose declaration. */
export interface ServiceObservation {
  readonly label: string;
  readonly tone: "success" | "warning" | "danger" | "info" | "neutral";
  readonly count: number | null;
}

/** Summarizes supplied observations without treating a missing snapshot as an absent container; a service the owner
 * stopped reads stopped, never failing. */
export function describeServiceObservation(
  service: string,
  containers?: readonly ContainerVitals[] | null,
  stopped?: TargetStop | null,
): ServiceObservation {
  if (ServiceControls.isStopped(stopped, service))
    return { label: "Stopped", tone: "neutral", count: containers?.filter((item) => item.service === service).length ?? null };
  if (containers == null)
    return { label: "Runtime unavailable", tone: "neutral", count: null };
  const matches = containers.filter(
    (container) => container.service === service,
  );
  if (!matches.length)
    return { label: "Declared · not observed", tone: "neutral", count: 0 };
  if (matches.length > 1)
    return {
      label: `${matches.length} containers observed`,
      tone: "info",
      count: matches.length,
    };
  const container = matches[0]!;
  const tone = ServerExtensions.containerTone(container);
  return {
    label: ServerExtensions.containerLabel(container),
    tone: tone === "destructive" ? "danger" : tone,
    count: 1,
  };
}
