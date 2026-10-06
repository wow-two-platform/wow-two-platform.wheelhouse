using System.Diagnostics;
using AwesomeAssertions;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Time.Testing;
using Wheelhouse.Application.Abstractions;
using Wheelhouse.Infrastructure.Deployments;
using Wheelhouse.Infrastructure.Deployments.Parsers;
using Wheelhouse.Infrastructure.Settings;

namespace Wheelhouse.Tests.Integration.Tests;

/// <summary>Exercises the hosted observation loop through its real process gateway. The stand-in transport records
/// requested actions and can block or fail; Python's follower tests cover durable journals and SSH observations.</summary>
public sealed class DeploymentOutcomeFollowerTests : IDisposable
{
    private readonly string _root = Directory.CreateTempSubdirectory("wheelhouse-following-").FullName;
    private readonly FakeTimeProvider _time = new();

    public DeploymentOutcomeFollowerTests()
    {
        File.WriteAllText(Path.Combine(_root, "transport.py"), """
            import json, os, pathlib, sys, time
            root = pathlib.Path(sys.argv[sys.argv.index('--root') + 1])
            with (root / 'calls').open('a') as stream:
                stream.write(sys.argv[1] + '\n')
            (root / 'pid').write_text(str(os.getpid()))
            while (root / 'block').exists():
                time.sleep(0.01)
            if (root / 'fail').exists():
                (root / 'failed').write_text('1')
                sys.exit(1)
            print(json.dumps({'checked': 1, 'unavailable': 0, 'busy': 0}))
            """);
    }

    /// <inheritdoc />
    public void Dispose() => Directory.Delete(_root, recursive: true);

    [Fact]
    public async Task Following_ShouldRunOnStartupAndRestart_WithoutBrowserRequests()
    {
        using (var host = CreateHost())
        {
            await host.StartAsync();
            try
            {
                await UntilAsync(() => Calls().Length == 1);
            }
            finally
            {
                await host.StopAsync().WaitAsync(TimeSpan.FromSeconds(5));
            }
        }

        using var restarted = CreateHost();
        await restarted.StartAsync();
        try
        {
            await UntilAsync(() => Calls().Length == 2);
            Calls().Should().Equal("follow", "follow");
        }
        finally
        {
            await restarted.StopAsync().WaitAsync(TimeSpan.FromSeconds(5));
        }
    }

    [Fact]
    public async Task Following_ShouldKeepObserving_WhenAPassFails()
    {
        File.WriteAllText(Path.Combine(_root, "fail"), "");
        using var host = CreateHost();
        await host.StartAsync();
        try
        {
            await UntilAsync(() => File.Exists(Path.Combine(_root, "failed")));
            File.Delete(Path.Combine(_root, "fail"));
            await UntilAsync(() => Calls().Length >= 2, () => _time.Advance(TimeSpan.FromSeconds(10)));
            Calls().Should().OnlyContain(action => action == "follow");
            host.Services.GetRequiredService<IEnumerable<IHostedService>>()
                .OfType<DeploymentOutcomeFollower>().Single().ExecuteTask!.IsFaulted.Should().BeFalse();
        }
        finally
        {
            await host.StopAsync().WaitAsync(TimeSpan.FromSeconds(5));
        }
    }

    [Fact]
    public async Task Following_ShouldCancelItsProcessAndNeverOverlap_WhenShutdownInterruptsARead()
    {
        File.WriteAllText(Path.Combine(_root, "block"), "");
        using var host = CreateHost();
        await host.StartAsync();
        try
        {
            await UntilAsync(() => File.Exists(Path.Combine(_root, "pid")));
            _time.Advance(TimeSpan.FromHours(1));
            Calls().Should().Equal("follow");
            var pid = int.Parse(await File.ReadAllTextAsync(Path.Combine(_root, "pid")));

            await host.StopAsync().WaitAsync(TimeSpan.FromSeconds(5));

            await UntilAsync(() => HasExited(pid));
            Calls().Should().Equal("follow");
        }
        finally
        {
            await host.StopAsync().WaitAsync(TimeSpan.FromSeconds(5));
        }
    }

    [Fact]
    public async Task Read_ShouldLaunchNoProcess_WhenAlreadyCancelled()
    {
        var gateway = new DeploymentGateway(Settings(), new RunnerFailureParser());
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();

        Func<Task> read = () => gateway.ReadAsync("follow", null, cancellation.Token);

        await read.Should().ThrowAsync<OperationCanceledException>();
        Calls().Should().BeEmpty();
    }

    [Fact]
    public async Task Following_ShouldStartNoObserver_WhenDisabled()
    {
        using var host = CreateHost(interval: 0);
        await host.StartAsync();
        try
        {
            var follower = host.Services.GetRequiredService<IEnumerable<IHostedService>>()
                .OfType<DeploymentOutcomeFollower>().Single();
            await follower.ExecuteTask!.WaitAsync(TimeSpan.FromSeconds(5));
            Calls().Should().BeEmpty();
        }
        finally
        {
            await host.StopAsync().WaitAsync(TimeSpan.FromSeconds(5));
        }
    }

    private DeploymentSettings Settings() => new()
    {
        Root = _root,
        TransportPath = Path.Combine(_root, "transport.py"),
    };

    private IHost CreateHost(int interval = 10) => new HostBuilder()
        .ConfigureAppConfiguration(configuration => configuration.AddInMemoryCollection(
            new Dictionary<string, string?> { [DeploymentOutcomeFollower.IntervalKey] = interval.ToString() }))
        .ConfigureServices(services =>
        {
            services.AddSingleton(Settings());
            services.AddSingleton<TimeProvider>(_time);
            services.AddSingleton<RunnerFailureParser>();
            services.AddScoped<IDeploymentGateway, DeploymentGateway>();
            services.AddHostedService<DeploymentOutcomeFollower>();
        })
        .Build();

    private string[] Calls()
    {
        var path = Path.Combine(_root, "calls");
        return File.Exists(path) ? File.ReadAllLines(path) : [];
    }

    private static bool HasExited(int pid)
    {
        try
        {
            using var process = Process.GetProcessById(pid);
            return process.HasExited;
        }
        catch (ArgumentException)
        {
            return true;
        }
    }

    private static async Task UntilAsync(Func<bool> condition, Action? advance = null)
    {
        var deadline = Stopwatch.StartNew();
        while (!condition())
        {
            deadline.Elapsed.Should().BeLessThan(TimeSpan.FromSeconds(5), "the bounded observation should complete");
            advance?.Invoke();
            await Task.Delay(10);
        }
    }
}
