import { z } from "zod";
import { TopologyAvailability } from "@/domain/topology";

/** Logical identifiers never contain credentials, resource interpolation, or filesystem paths. @internal */
const NameSchema = z.string().regex(/^[A-Za-z0-9][A-Za-z0-9_.-]{0,127}$/);
/** Service names follow the deployment runner's validated slug contract. @internal */
const ServiceNameSchema = z.string().regex(/^[a-z][a-z0-9-]{0,47}$/);
/** Published/container ports, excluding host addresses and arbitrary Compose content. @internal */
const PortSchema = z
  .string()
  .regex(/^\d+(?:-\d+)?(?::\d+(?:-\d+)?)?(?:\/(?:tcp|udp|sctp))?$/);
/** Named memberships must remain unique to give every connector one unambiguous identity. @internal */
const NamesSchema = z
  .array(NameSchema)
  .refine((items) => new Set(items).size === items.length);

/** Decodes only the safe operational projection, discarding unknown server fields. */
export const ServiceTopologySchema = z
  .object({
    targetId: z.string().min(1),
    availability: z.enum(TopologyAvailability),
    release: z.string().nullable(),
    collectedAt: z.iso.datetime({ offset: true }),
    services: z.array(
      z.object({
        name: ServiceNameSchema,
        image: z.string().nullable(),
        networks: NamesSchema,
        volumes: NamesSchema,
        ports: z.array(PortSchema),
        // Release facts beside Compose; an older runner omits them.
        version: z
          .object({ version: z.string(), changedIn: z.string() })
          .nullable()
          .default(null),
        needs: z.array(z.enum(["postgres", "valkey", "broker"])).default([]),
        sites: z
          .array(
            z.object({
              name: ServiceNameSchema,
              path: z.string().regex(/^\/[A-Za-z0-9._~/-]*$/),
              port: z.number().int().min(1).max(65535),
              exposure: z.enum(["public", "private"]),
              // Opened in the owner's browser, so only http(s) survives decoding.
              url: z
                .string()
                .regex(/^https?:\/\//)
                .nullable(),
              reachable: z.boolean().nullable(),
            }),
          )
          .default([]),
      }),
    ),
    networks: z.array(z.object({ name: NameSchema, external: z.boolean() })),
    volumes: z.array(z.object({ name: NameSchema, external: z.boolean() })),
    dependencies: z.array(
      z.object({
        from: ServiceNameSchema,
        to: ServiceNameSchema,
        condition: z.string().nullable(),
        required: z.boolean(),
      }),
    ),
    warnings: z.array(z.string()),
  })
  .superRefine((topology, context) => {
    const services = new Set(topology.services.map((item) => item.name));
    const networks = new Set(topology.networks.map((item) => item.name));
    const volumes = new Set(topology.volumes.map((item) => item.name));
    const dependencies = new Set(
      topology.dependencies.map((item) => JSON.stringify([item.from, item.to])),
    );
    if (
      services.size !== topology.services.length ||
      networks.size !== topology.networks.length ||
      volumes.size !== topology.volumes.length ||
      dependencies.size !== topology.dependencies.length
    ) {
      context.addIssue({
        code: "custom",
        message: "Topology identities must be unique.",
      });
    }
    if (
      topology.services.some(
        (service) =>
          service.networks.some((name) => !networks.has(name)) ||
          service.volumes.some((name) => !volumes.has(name)),
      ) ||
      topology.dependencies.some(
        (edge) => !services.has(edge.from) || !services.has(edge.to),
      )
    ) {
      context.addIssue({
        code: "custom",
        message: "Topology references must name declared resources.",
      });
    }
    if (
      topology.availability !== TopologyAvailability.Available &&
      (topology.release !== null ||
        services.size ||
        networks.size ||
        volumes.size ||
        dependencies.size)
    ) {
      context.addIssue({
        code: "custom",
        message: "Unavailable topology must not supply a partial graph.",
      });
    }
  });
