import { z } from "zod";
import { InventoryRules } from "@/domain/common";
import {
  DEPLOY_ACCOUNT,
  EnrollmentPatterns,
  EnrollmentRules,
  NetworkAddress,
  ProductsHostIngress,
  RESERVED_ADMINS,
  type Enrollment,
  type StartEnrollmentRequest,
} from "@/domain/infrastructure";
import { VpsProvider, type SaveServerRequest, type Server } from "@/domain/servers";

/** An enrollment's editable fields; empty optional text means absent. */
export interface EnrollmentFormModel {
  publicAddress: string;
  sshPort: number | null;
  loginUser: string;
  admin: string;
  adminKey: string;
  tailnetHostname: string;
  expectedHostKey: string;
}

/** Validates what the dialog can judge alone, with the host's own patterns; the host checks the rest. */
export const enrollmentFormSchema = z
  .object({
    publicAddress: z
      .string()
      .trim()
      .refine(NetworkAddress.isIp, "Use the server's public IP address, as the provider shows it.")
      .refine((value) => !NetworkAddress.isTailnet(value), "Use the server's public address, not a tailnet one."),
    sshPort: z.number("Enter the SSH port.").int().min(1).max(65535),
    loginUser: z
      .string()
      .trim()
      .regex(EnrollmentPatterns.loginUser, "Use the account the image delivers: root, or its administrator such as ubuntu."),
    admin: z.string().trim(),
    adminKey: z
      .string()
      .trim()
      .max(EnrollmentPatterns.publicKeyLength, "Paste one SSH public key line.")
      .refine((value) => !value || EnrollmentPatterns.publicKey.test(value), "Paste one SSH public key line."),
    tailnetHostname: z.string().trim().regex(EnrollmentPatterns.hostname, "Use a lowercase hostname, such as ocharo-vps."),
    expectedHostKey: z
      .string()
      .trim()
      .refine((value) => !value || EnrollmentPatterns.fingerprint.test(value), "Use the form SHA256:…, as the provider's console shows it."),
  })
  .superRefine((values, context) => {
    // Without root the administrator is the sign-in account, whose own refusal already names the field.
    if (!EnrollmentPatterns.loginUser.test(values.loginUser)) return;
    const admin = adminOf(values);
    if (!EnrollmentPatterns.admin.test(admin) || RESERVED_ADMINS.includes(admin))
      context.addIssue({
        code: "custom",
        path: ["admin"],
        message: `Name the administrator with lowercase letters, other than root or ${DEPLOY_ACCOUNT}.`,
      });
  });

/** The administrator the request names: the typed one when signing in as root, else the sign-in account itself. */
function adminOf(values: Pick<EnrollmentFormModel, "loginUser" | "admin">): string {
  const login = values.loginUser.trim();
  return login === "root" ? values.admin.trim() : login;
}

/**
 * The dialog's starting values: a rerun repeats the recorded request; a first run reads the server — its public host,
 * the account its provider's image delivers and its slug as the tailnet hostname.
 */
export function enrollmentFormOf(server: Server, enrollment: Enrollment | null | undefined): EnrollmentFormModel {
  const request = enrollment?.request;
  if (request)
    return {
      publicAddress: request.publicAddress,
      sshPort: request.sshPort,
      loginUser: request.loginUser,
      admin: request.admin,
      adminKey: request.adminKey ?? "",
      tailnetHostname: request.tailnetHostname,
      expectedHostKey: request.expectedHostKey ?? "",
    };
  const loginUser = EnrollmentRules.loginUserFor(server.provider);
  return {
    publicAddress: EnrollmentRules.publicAddressOf(server),
    sshPort: 22,
    loginUser,
    admin: EnrollmentRules.adminFor(loginUser),
    adminKey: "",
    tailnetHostname: EnrollmentRules.hostnameFor(server),
    expectedHostKey: "",
  };
}

/** The administrator after the sign-in account changes: root keeps (or suggests) one; any other account is it. */
export function adminAfterLogin(previousLogin: string, nextLogin: string, admin: string): string {
  if (nextLogin.trim() !== "root") return nextLogin.trim();
  return previousLogin.trim() === "root" && admin.trim() ? admin : EnrollmentRules.adminFor("root");
}

/** The request a submitted dialog sends; optional text left empty goes as null. */
export function enrollmentRequestOf(values: EnrollmentFormModel): StartEnrollmentRequest {
  return {
    publicAddress: values.publicAddress.trim(),
    sshPort: values.sshPort ?? 22,
    loginUser: values.loginUser.trim(),
    admin: adminOf(values),
    adminKey: values.adminKey.trim() || null,
    tailnetHostname: values.tailnetHostname.trim(),
    expectedHostKey: values.expectedHostKey.trim() || null,
  };
}

/** The providers a fresh server comes from: every one but the local rig. */
export const FreshServerProviders: readonly VpsProvider[] = Object.values(VpsProvider).filter(
  (provider) => provider !== VpsProvider.Local,
);

/** A fresh server's fields: who hosts it, where, and the public IP the provider gave it. */
export interface FreshServerFormModel {
  slug: string;
  name: string;
  provider: VpsProvider;
  region: string;
  publicAddress: string;
}

/** Validates a fresh server's fields; the host checks names and duplicates. */
export const freshServerFormSchema = z.object({
  slug: z.string().trim().regex(InventoryRules.slug, "Start with a lowercase letter; use lowercase letters, digits and dashes."),
  name: z.string().trim().min(1, "Enter a name."),
  provider: z.enum(VpsProvider).refine((value) => value !== VpsProvider.Local, "Choose the server's provider."),
  region: z.string().trim().min(1, "Enter a region."),
  publicAddress: z
    .string()
    .trim()
    .refine(NetworkAddress.isPublic, "Use the public IP address the provider shows, not a tailnet one."),
});

/** A blank fresh server, on Contabo. */
export function freshServerFormOf(): FreshServerFormModel {
  return { slug: "", name: "", provider: VpsProvider.Contabo, region: "", publicAddress: "" };
}

/**
 * The server a fresh one is registered as: reached on its public IP as the deploy account on port 22, publishing sites
 * as a products host does. Once enrolled, Wheelhouse moves its host onto the tailnet address.
 */
export function freshServerRequestOf(values: FreshServerFormModel): SaveServerRequest {
  return {
    slug: values.slug.trim(),
    name: values.name.trim(),
    provider: values.provider,
    host: values.publicAddress.trim(),
    region: values.region.trim(),
    sshUser: DEPLOY_ACCOUNT,
    sshPort: 22,
    ingress: {
      ...ProductsHostIngress,
      entryPoints: [...ProductsHostIngress.entryPoints],
      privateEntryPoints: [...ProductsHostIngress.privateEntryPoints],
    },
  };
}
