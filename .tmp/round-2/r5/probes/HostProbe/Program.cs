using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

// What a host does when an IHostedService's StartAsync throws (the identity seeder's shape):
// does StartAsync of the next service run, and does the host start?
HostApplicationBuilder builder = Host.CreateApplicationBuilder(args);
builder.Services.AddHostedService<ThrowingSeeder>();
builder.Services.AddHostedService<NextService>();
using IHost host = builder.Build();
try
{
    await host.StartAsync();
    Console.WriteLine("host started");
}
catch (Exception e)
{
    Console.WriteLine($"host.StartAsync threw {e.GetType().Name}: {e.Message}");
}

/// <summary>Throws from StartAsync like a seeder whose first query fails.</summary>
internal sealed class ThrowingSeeder : IHostedService
{
    public Task StartAsync(CancellationToken cancellationToken) => throw new InvalidOperationException("seeding failed");
    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;
}

/// <summary>Registered after the seeder.</summary>
internal sealed class NextService : IHostedService
{
    public Task StartAsync(CancellationToken cancellationToken) { Console.WriteLine("NextService.StartAsync ran"); return Task.CompletedTask; }
    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;
}
