import type { Component } from "vue";
import {
  ArchiveRestore,
  CloudDownload,
  DatabaseBackup,
  Dot,
  FileCog,
  FileMinus,
  FlaskConical,
  Globe,
  HeartPulse,
  History,
  Hourglass,
  PackageMinus,
  Play,
  Radar,
  RotateCw,
  Route,
  ServerCog,
  Square,
  Unplug,
} from "lucide-vue-next";

import { StepKind } from "@/domain/deployments";

/** Each rollout step kind's mark beside its name: what the step does, while the status icon says how it went. */
export const StepKindIcons: Readonly<Record<StepKind, Component>> = {
  [StepKind.RenderSettings]: FileCog,
  [StepKind.CheckTarget]: ServerCog,
  [StepKind.Snapshot]: DatabaseBackup,
  [StepKind.Pull]: CloudDownload,
  [StepKind.Migrations]: FlaskConical,
  [StepKind.Drain]: Unplug,
  [StepKind.Wait]: Hourglass,
  [StepKind.Start]: Play,
  [StepKind.Stop]: Square,
  [StepKind.Restart]: RotateCw,
  [StepKind.Verify]: HeartPulse,
  [StepKind.PublishSites]: Globe,
  [StepKind.ProbeSites]: Radar,
  [StepKind.VerifyEndToEnd]: Route,
  [StepKind.RemoveImages]: PackageMinus,
  [StepKind.RemoveFiles]: FileMinus,
  [StepKind.RollBack]: History,
  [StepKind.Restore]: ArchiveRestore,
  [StepKind.Other]: Dot,
};
