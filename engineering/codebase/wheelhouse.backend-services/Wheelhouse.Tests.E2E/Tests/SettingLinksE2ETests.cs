using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using AwesomeAssertions;
using Microsoft.Extensions.DependencyInjection;
using Wheelhouse.Application.Abstractions;
using Wheelhouse.Domain.Products.Entities;
using Wheelhouse.Domain.Targets.Entities;
using Wheelhouse.Domain.Targets.Enums;
using Wheelhouse.Tests.E2E.Harness;

namespace Wheelhouse.Tests.E2E.Tests;

/// <summary>
/// E2E for an owner's link from a target's setting key to another product's key in the same environment (v0.15
/// Iteration 1): Events on <c>events-dev</c> presents the client secret ForeverPin's vault holds on <c>foreverpin-dev</c>.
/// The plan names the source with its secrecy and version, a deploy sends the source's current value under the linking
/// key (over a value of its own), a source's new version marks the key unapplied, a value saved to a linked key is
/// refused until it is unlinked, and each refusal of a link — a missing source target, the same product, another
/// environment, a derived key, a key the release links with <c>from</c>, a second hop — keeps nothing. No value appears
/// in a reply, the audit or a history.
/// </summary>
[Collection(WheelhouseCollection.Name)]
public sealed class SettingLinksE2ETests(WheelhouseAppFixture fixture) : WheelhouseE2EBase(fixture)
{
    private const string SourceKey = "Identity:Server:Clients:8:ClientSecret";
    private const string LinkingKey = "Scfd:ClientSecret";
    private const string SourceValue = "SOURCE_LINK_SECRET_4d2e";
    private const string OwnValue = "OWN_EVENTS_SECRET_91b7";

    [Fact]
    public async Task Link_ShouldRenderTheSourcesSecret_OverTheKeysOwn_AndShowItsVersionOnly()
    {
        await AddEventsAsync();
        (await Save("foreverpin-dev", SourceKey, SourceValue, secret: true)).StatusCode.Should().Be(HttpStatusCode.OK);
        (await Save("events-dev", LinkingKey, OwnValue, secret: true)).StatusCode.Should().Be(HttpStatusCode.OK);
        (await Save("events-dev", LinkingKey, OwnValue + "-2", secret: true)).StatusCode.Should().Be(HttpStatusCode.OK);

        (await AdminClient.PutAsJsonAsync(LinkPath("events-dev", LinkingKey), new { sourceTarget = "foreverpin-dev", sourceKey = SourceKey }))
            .StatusCode.Should().Be(HttpStatusCode.BadRequest, "a link carries its action header");
        var linked = await Link("events-dev", LinkingKey, "foreverpin-dev", SourceKey);
        linked.StatusCode.Should().Be(HttpStatusCode.OK, await linked.Content.ReadAsStringAsync());
        var source = (await linked.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("data");
        (source.GetProperty("target").GetString(), source.GetProperty("key").GetString()).Should().Be(("foreverpin-dev", SourceKey));

        var key = Extra(await DataAsync(Settings("events-dev")), LinkingKey);
        (key.GetProperty("source").GetString(), key.GetProperty("secret").GetBoolean()).Should().Be(("linked", true));
        (key.GetProperty("link").GetProperty("target").GetString(), key.GetProperty("link").GetProperty("key").GetString())
            .Should().Be(("foreverpin-dev", SourceKey));
        key.GetProperty("vault").GetProperty("version").GetInt32().Should().Be(1, "the source's version, not the key's own");
        key.GetProperty("value").ValueKind.Should().Be(JsonValueKind.Null, "a secret source's value never shows");

        (await Deploy("events-dev", "events-gh-1")).StatusCode.Should().Be(HttpStatusCode.Accepted);
        Fixture.Deployments.LastSettings!.Value.GetProperty("values").GetProperty(LinkingKey).GetString()
            .Should().Be(SourceValue, "the link wins over the key's own secret");
        (await AdminClient.GetAsync("/api/deployments/targets/events-dev/check?release=events-gh-1")).StatusCode.Should().Be(HttpStatusCode.OK);
        Fixture.Deployments.LastCheckKeys.Should().Contain(LinkingKey);

        var audit = (await AdminClient.AuditItemsAsync()).EnumerateArray()
            .Single(entry => entry.GetProperty("action").GetString() == "settings.link");
        (audit.GetProperty("subject").GetString(), audit.GetProperty("detail").GetString(), audit.GetProperty("outcome").GetString())
            .Should().Be(("events-dev/" + LinkingKey, "foreverpin-dev/" + SourceKey, "succeeded"));
        await ShouldNotShow(SourceValue, OwnValue);
    }

    [Fact]
    public async Task RegeneratedSource_ShouldMarkTheLinkingKeyUnapplied_AndReachItsNextDeploy()
    {
        await AddEventsAsync();
        Fixture.Deployments.TargetDeclarations["foreverpin-dev"] = new
        {
            targetId = "foreverpin-dev", release = "v1.0.0", product = "foreverpin", environment = "dev",
            services = new object[]
            {
                new
                {
                    name = "identity", hostFile = false,
                    keys = new object[] { new { key = SourceKey, required = true, secret = true, generate = new { random = 32, encoding = "base64url" } } },
                },
            },
        };
        Fixture.Deployments.History.Add(Succeeded("foreverpin-dev", "foreverpin-gh-1"));
        Fixture.Deployments.History.Add(Succeeded("events-dev", "events-gh-1"));
        (await Save("foreverpin-dev", SourceKey, SourceValue, secret: true)).StatusCode.Should().Be(HttpStatusCode.OK);
        (await Link("events-dev", LinkingKey, "foreverpin-dev", SourceKey)).StatusCode.Should().Be(HttpStatusCode.OK);
        (await Deploy("events-dev", "events-gh-1")).StatusCode.Should().Be(HttpStatusCode.Accepted);
        (await DataAsync(Settings("events-dev"))).GetProperty("unapplied").EnumerateArray().Should().BeEmpty("the deploy rendered it");

        var regenerated = await Owner("settings").PostAsJsonAsync("/api/deployments/targets/foreverpin-dev/secrets/" + SourceKey + "/regenerate", new { });
        regenerated.StatusCode.Should().Be(HttpStatusCode.OK, await regenerated.Content.ReadAsStringAsync());

        var plan = await DataAsync(Settings("events-dev"));
        plan.GetProperty("unapplied").EnumerateArray().Select(key => key.GetString()).Should().Equal(LinkingKey);
        Extra(plan, LinkingKey).GetProperty("vault").GetProperty("version").GetInt32().Should().Be(2);
        Fixture.Host.Clock.Advance(TimeSpan.FromMinutes(1)); // the next render is the latest
        (await Deploy("events-dev", "events-gh-1")).StatusCode.Should().Be(HttpStatusCode.Accepted);
        var rendered = Fixture.Deployments.LastSettings!.Value.GetProperty("values").GetProperty(LinkingKey).GetString()!;
        rendered.Should().MatchRegex("^[A-Za-z0-9_-]{43}$", "the source's regenerated value").And.NotBe(SourceValue);
        (await DataAsync(Settings("events-dev"))).GetProperty("unapplied").EnumerateArray().Should().BeEmpty("the latest render holds v2");
        await ShouldNotShow(SourceValue, rendered);
    }

    [Fact]
    public async Task GeneratedClientSecret_ShouldReadUnread_UntilAKeyTakesItWithFromOrAnotherProductLinksIt()
    {
        const string Read = "Identity:Server:Clients:0:ClientSecret";
        const string Orphan = "Identity:Server:Clients:9:ClientSecret";
        const string Own = "Identity:Otp:HashKey";
        await AddEventsAsync();
        var generate = new { random = 32, encoding = "base64url" };
        Fixture.Deployments.TargetDeclarations["foreverpin-dev"] = new
        {
            targetId = "foreverpin-dev", release = "v1.0.0", product = "foreverpin", environment = "dev",
            services = new object[]
            {
                new
                {
                    name = "identity", hostFile = false,
                    keys = new object[]
                    {
                        new { key = SourceKey, required = true, secret = true, generate },
                        new { key = Read, required = true, secret = true, generate },
                        new { key = Orphan, required = true, secret = true, generate },
                        new { key = Own, required = true, secret = true, generate },
                    },
                },
                new
                {
                    name = "desk", hostFile = false,
                    keys = new object[] { new { key = "Identity:Client:ClientSecret", required = false, secret = true, from = Read } },
                },
            },
        };
        Fixture.Deployments.History.Add(Succeeded("foreverpin-dev", "foreverpin-gh-1"));
        foreach (var key in new[] { SourceKey, Read, Orphan, Own })
            (await Save("foreverpin-dev", key, SourceValue + key.Length, secret: true)).StatusCode.Should().Be(HttpStatusCode.OK);

        bool Unread(JsonElement plan, string key) => Declared(plan, key).GetProperty("unread").GetBoolean();
        var before = await DataAsync(Settings("foreverpin-dev"));
        Unread(before, SourceKey).Should().BeTrue("no key takes it and no other product links it yet");
        Unread(before, Orphan).Should().BeTrue();
        Unread(before, Read).Should().BeFalse("desk takes it with from");
        Unread(before, Own).Should().BeFalse("only a client secret has a reader to miss");

        (await Link("events-dev", LinkingKey, "foreverpin-dev", SourceKey)).StatusCode.Should().Be(HttpStatusCode.OK);
        var after = await DataAsync(Settings("foreverpin-dev"));
        Unread(after, SourceKey).Should().BeFalse("events-dev links it");
        Unread(after, Orphan).Should().BeTrue();
    }

    [Fact]
    public async Task DeclaredKey_ShouldWaitForItsLinkedSource_AndShowAPlainSourcesValue()
    {
        await AddEventsAsync();
        Fixture.Deployments.History.Add(Succeeded("events-dev", "events-gh-1"));
        Fixture.Deployments.TargetDeclarations["events-dev"] = new
        {
            targetId = "events-dev", release = "v0.1.0", product = "events", environment = "dev",
            services = new object[]
            {
                new
                {
                    name = "events", hostFile = false,
                    keys = new object[]
                    {
                        new { key = LinkingKey, required = true, secret = true },
                        new { key = "Scfd:BaseUrl", required = true, secret = false, type = "url" },
                    },
                },
            },
        };
        (await Link("events-dev", LinkingKey, "foreverpin-dev", SourceKey)).StatusCode.Should().Be(HttpStatusCode.OK);
        (await Link("events-dev", "Scfd:BaseUrl", "foreverpin-dev", "Scfd:PublicUrl")).StatusCode.Should().Be(HttpStatusCode.OK);

        var waiting = await DataAsync(Settings("events-dev"));
        (Declared(waiting, LinkingKey).GetProperty("source").GetString(), waiting.GetProperty("missing").GetInt32()).Should().Be(("missing", 2));
        Declared(waiting, LinkingKey).GetProperty("link").GetProperty("target").GetString().Should().Be("foreverpin-dev");
        var refused = await Deploy("events-dev", "events-gh-1");
        refused.StatusCode.Should().Be(HttpStatusCode.Conflict);
        (await refused.Content.ReadAsStringAsync()).Should().Contain(LinkingKey + " (linked to foreverpin-dev " + SourceKey + ")");

        (await Save("foreverpin-dev", SourceKey, SourceValue, secret: true)).StatusCode.Should().Be(HttpStatusCode.OK);
        (await Save("foreverpin-dev", "Scfd:PublicUrl", "https://scfd.example.test/", secret: false)).StatusCode.Should().Be(HttpStatusCode.OK);
        var ready = await DataAsync(Settings("events-dev"));
        ready.GetProperty("missing").GetInt32().Should().Be(0);
        var url = Declared(ready, "Scfd:BaseUrl");
        (url.GetProperty("source").GetString(), url.GetProperty("secret").GetBoolean(), url.GetProperty("value").GetString())
            .Should().Be(("linked", false, "https://scfd.example.test/"), "a plain source's value shows through");
        (Declared(ready, LinkingKey).GetProperty("secret").GetBoolean(), Declared(ready, LinkingKey).GetProperty("vault").GetProperty("version").GetInt32())
            .Should().Be((true, 1));

        (await Deploy("events-dev", "events-gh-1")).StatusCode.Should().Be(HttpStatusCode.Accepted);
        var values = Fixture.Deployments.LastSettings!.Value.GetProperty("values");
        (values.GetProperty(LinkingKey).GetString(), values.GetProperty("Scfd:BaseUrl").GetString())
            .Should().Be((SourceValue, "https://scfd.example.test/"));
        await ShouldNotShow(SourceValue);
    }

    [Fact]
    public async Task Link_ShouldRefuseAMissingSourceTheSameProductAnotherEnvironmentADerivedKeyAReleaseLinkAndASecondHop()
    {
        await AddEventsAsync();
        Fixture.Deployments.History.Add(Succeeded("events-dev", "events-gh-1"));
        Fixture.Deployments.TargetDeclarations["events-dev"] = new
        {
            targetId = "events-dev", release = "v0.1.0", product = "events", environment = "dev",
            services = new object[]
            {
                new
                {
                    name = "events", hostFile = false,
                    keys = new object[]
                    {
                        new { key = "Auth:ClientSecret", required = true, secret = false, from = "Identity:Server:Clients:0:ClientSecret" },
                        new { key = "Identity:Server:Clients:0:ClientSecret", required = true, secret = true },
                    },
                },
            },
        };

        await ShouldRefuse("nothing-here", LinkingKey, "foreverpin-dev", SourceKey, HttpStatusCode.NotFound, "Target 'nothing-here' was not found.");
        await ShouldRefuse("events-dev", LinkingKey, "nothing-here", SourceKey, HttpStatusCode.BadRequest, "Source target 'nothing-here' was not found.");
        await ShouldRefuse("events-dev", LinkingKey, "events-dev", "Events:Other", HttpStatusCode.BadRequest, "events-dev belongs to events too");
        await ShouldRefuse("events-dev", LinkingKey, "foreverpin-prod", SourceKey, HttpStatusCode.BadRequest, "a link stays inside one environment");
        await ShouldRefuse("events-dev", "Bad Key", "foreverpin-dev", SourceKey, HttpStatusCode.BadRequest, "");
        await ShouldRefuse("events-dev", LinkingKey, "foreverpin-dev", "bad key", HttpStatusCode.BadRequest, "A source key is a configuration path");
        await ShouldRefuse("events-dev", "AllowedHosts", "foreverpin-dev", SourceKey, HttpStatusCode.BadRequest, "AllowedHosts is derived at each deploy");
        await ShouldRefuse("events-dev", LinkingKey, "foreverpin-dev", "Sites:app:Url", HttpStatusCode.BadRequest, "Sites:app:Url is derived");
        await ShouldRefuse("events-dev", "Auth:ClientSecret", "foreverpin-dev", SourceKey, HttpStatusCode.Conflict,
            "The release links Auth:ClientSecret to Identity:Server:Clients:0:ClientSecret");

        // One hop: a key another product links to links nowhere, and a linked key is no source.
        (await Link("foreverpin-dev", "Desk:EventsKey", "events-dev", "Events:ApiKey")).StatusCode.Should().Be(HttpStatusCode.OK);
        await ShouldRefuse("events-dev", LinkingKey, "foreverpin-dev", "Desk:EventsKey", HttpStatusCode.Conflict,
            "foreverpin-dev Desk:EventsKey is itself linked to events-dev Events:ApiKey");
        await ShouldRefuse("events-dev", "Events:ApiKey", "foreverpin-dev", SourceKey, HttpStatusCode.Conflict,
            "Events:ApiKey is the source of foreverpin's Desk:EventsKey; links go one hop");

        var plan = await DataAsync(Settings("events-dev"));
        plan.GetProperty("extra").EnumerateArray().Should().BeEmpty("no refused link was kept");
        (await AdminClient.AuditItemsAsync()).EnumerateArray()
            .Where(entry => entry.GetProperty("action").GetString() == "settings.link")
            .Count(entry => entry.GetProperty("outcome").GetString() == "succeeded").Should().Be(1);
    }

    [Fact]
    public async Task LinkedKey_ShouldRefuseAValue_KeepItsSourceOnRemove_AndRenderItsOwnAfterUnlink()
    {
        await AddEventsAsync();
        (await Save("foreverpin-dev", SourceKey, SourceValue, secret: true)).StatusCode.Should().Be(HttpStatusCode.OK);
        (await Save("events-dev", LinkingKey, OwnValue, secret: true)).StatusCode.Should().Be(HttpStatusCode.OK);
        (await Link("events-dev", LinkingKey, "foreverpin-dev", SourceKey)).StatusCode.Should().Be(HttpStatusCode.OK);

        var typed = await Save("events-dev", LinkingKey, "typed-by-hand", secret: true);
        typed.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        (await typed.Content.ReadAsStringAsync()).Should().Contain(
            LinkingKey + " is linked to foreverpin-dev " + SourceKey + "; unlink it to give it a value of its own.");
        (await DataAsync("/api/deployments/targets/events-dev/secrets/" + LinkingKey + "/versions"))
            .GetProperty("versions").GetArrayLength().Should().Be(1, "nothing was written");

        (await AdminClient.DeleteAsync(LinkPath("events-dev", LinkingKey))).StatusCode
            .Should().Be(HttpStatusCode.BadRequest, "an unlink carries its action header");
        (await Owner("settings").DeleteAsync(LinkPath("events-dev", LinkingKey))).StatusCode.Should().Be(HttpStatusCode.NoContent);
        (await Owner("settings").DeleteAsync(LinkPath("events-dev", LinkingKey))).StatusCode.Should().Be(HttpStatusCode.NotFound);
        var unlinked = (await AdminClient.AuditItemsAsync()).EnumerateArray()
            .Single(entry => entry.GetProperty("action").GetString() == "settings.unlink" && entry.GetProperty("outcome").GetString() == "succeeded");
        (unlinked.GetProperty("subject").GetString(), unlinked.GetProperty("detail").GetString())
            .Should().Be(("events-dev/" + LinkingKey, "foreverpin-dev/" + SourceKey));
        var own = Extra(await DataAsync(Settings("events-dev")), LinkingKey);
        (own.GetProperty("source").GetString(), own.GetProperty("link").ValueKind).Should().Be(("vault", JsonValueKind.Null));
        (await Deploy("events-dev", "events-gh-1")).StatusCode.Should().Be(HttpStatusCode.Accepted);
        Fixture.Deployments.LastSettings!.Value.GetProperty("values").GetProperty(LinkingKey).GetString().Should().Be(OwnValue);

        // Linked again, the key's own value may go; the source keeps everything it holds.
        (await Link("events-dev", LinkingKey, "foreverpin-dev", SourceKey)).StatusCode.Should().Be(HttpStatusCode.OK);
        (await Owner("settings").PutAsJsonAsync(Settings("events-dev"),
                new { values = new[] { new { key = LinkingKey, value = "", secret = true, remove = true } } }))
            .StatusCode.Should().Be(HttpStatusCode.OK);
        var history = await DataAsync("/api/deployments/targets/foreverpin-dev/secrets/" + SourceKey + "/versions");
        (history.GetProperty("removed").GetBoolean(), history.GetProperty("versions").GetArrayLength()).Should().Be((false, 1));
        Extra(await DataAsync(Settings("events-dev")), LinkingKey).GetProperty("source").GetString().Should().Be("linked");
        (await Deploy("events-dev", "events-gh-1")).StatusCode.Should().Be(HttpStatusCode.Accepted);
        Fixture.Deployments.LastSettings!.Value.GetProperty("values").GetProperty(LinkingKey).GetString().Should().Be(SourceValue);
        await ShouldNotShow(SourceValue, OwnValue);
    }

    /// <summary>Adds Events with a dev target beside foreverpin-dev and a prod one beside foreverpin-prod.</summary>
    private async Task AddEventsAsync()
    {
        await using var scope = Fixture.Host.Services.CreateAsyncScope();
        var services = scope.ServiceProvider;
        var products = services.GetRequiredService<IProductsRepository>();
        var foreverPin = await products.GetBySlugAsync("foreverpin");
        var events = await products.CreateAsync(new ProductEntity
        {
            Id = Guid.NewGuid(), Slug = "events", Name = "Events", Description = "Event delivery.",
            Repository = "wow-two-sdk-beta/wow-two-sdk-beta.events", DefaultBranch = "main", WorkspaceId = foreverPin!.WorkspaceId,
        });
        var servers = services.GetRequiredService<IServersRepository>();
        var targets = services.GetRequiredService<ITargetsRepository>();
        foreach (var (slug, server, environment) in new[] { ("events-dev", "pilot-host", DeploymentEnvironment.Dev), ("events-prod", "prod-host", DeploymentEnvironment.Prod) })
            await targets.CreateAsync(new TargetEntity
            {
                Id = Guid.NewGuid(), Slug = slug, ProductId = events.Id, ServerId = (await servers.GetBySlugAsync(server))!.Id,
                Environment = environment, Network = "platform", Root = "/srv/wheelhouse",
            });
    }

    private async Task ShouldRefuse(string target, string key, string sourceTarget, string sourceKey, HttpStatusCode status, string reason)
    {
        var response = await Link(target, key, sourceTarget, sourceKey);
        var body = await response.Content.ReadAsStringAsync();
        response.StatusCode.Should().Be(status, body);
        Texts(JsonDocument.Parse(body).RootElement).Should().Contain(text => text.Contains(reason, StringComparison.Ordinal), body);
    }

    /// <summary>Every string a reply carries, unescaped, so a message reads as the owner sees it.</summary>
    private static IEnumerable<string> Texts(JsonElement element) => element.ValueKind switch
    {
        JsonValueKind.String => [element.GetString()!],
        JsonValueKind.Object => element.EnumerateObject().SelectMany(property => Texts(property.Value)),
        JsonValueKind.Array => element.EnumerateArray().SelectMany(Texts),
        _ => [],
    };

    /// <summary>Asserts that no reply the console reads carries any of <paramref name="texts"/>.</summary>
    private async Task ShouldNotShow(params string[] texts)
    {
        foreach (var path in new[]
                 {
                     "/api/audit", Settings("events-dev"), Settings("foreverpin-dev"), "/api/deployments/history?notApplied=true",
                     "/api/deployments/targets/foreverpin-dev/secrets/" + SourceKey + "/versions",
                 })
        {
            var body = await (await AdminClient.GetAsync(path)).Content.ReadAsStringAsync();
            foreach (var text in texts)
                body.Should().NotContain(text, path);
        }
    }

    /// <summary>A succeeded deploy of a release to a target, so its declarations are the running release's.</summary>
    private static object Succeeded(string target, string bundle) => new
    {
        id = Guid.NewGuid().ToString(), targetId = target, environment = "dev", bundleId = bundle,
        release = "v1.0.0", actor = "test-admin", status = "succeeded", submittedAt = "2026-11-09T10:00:00+00:00",
    };

    private static string Settings(string target) => "/api/deployments/targets/" + target + "/settings";

    private static string LinkPath(string target, string key) => Settings(target) + "/" + Uri.EscapeDataString(key) + "/link";

    private Task<HttpResponseMessage> Link(string target, string key, string sourceTarget, string sourceKey) =>
        Owner("settings").PutAsJsonAsync(LinkPath(target, key), new { sourceTarget, sourceKey });

    private Task<HttpResponseMessage> Save(string target, string key, string value, bool secret) =>
        Owner("settings").PutAsJsonAsync(Settings(target), new { values = new[] { new { key, value, secret } } });

    private Task<HttpResponseMessage> Deploy(string target, string release) =>
        Owner("deploy").PostAsJsonAsync("/api/deployments", new { target, release });

    private static JsonElement Extra(JsonElement plan, string key) =>
        plan.GetProperty("extra").EnumerateArray().Single(item => item.GetProperty("key").GetString() == key);

    private static JsonElement Declared(JsonElement plan, string key) =>
        plan.GetProperty("services").EnumerateArray().SelectMany(service => service.GetProperty("keys").EnumerateArray())
            .Single(item => item.GetProperty("key").GetString() == key);

    private HttpClient Owner(string action)
    {
        var client = AdminClient;
        client.DefaultRequestHeaders.Add("X-Wheelhouse-Action", action);
        return client;
    }

    private async Task<JsonElement> DataAsync(string path)
    {
        var response = await AdminClient.GetAsync(path);
        response.StatusCode.Should().Be(HttpStatusCode.OK, await response.Content.ReadAsStringAsync());
        return (await response.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("data");
    }
}
