using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using NSubstitute;
using SmsService.Api.Sending;

namespace SmsService.Tests.Infrastructure;

/// <summary>
/// The real API against a fresh test database, with a fake SMS provider.
/// The background SendWorker is removed so API tests see deterministic statuses (the worker has its own tests).
/// </summary>
public sealed class ApiFactory(string connectionString) : WebApplicationFactory<Program>
{
    public ISmsProviderClient Provider { get; } = Substitute.For<ISmsProviderClient>();
    public CapturingLoggerProvider Logs { get; } = new();

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        // Not Development: that would load user-secrets, whose Database:Password overrides the test container's password.
        builder.UseEnvironment("Testing");
        builder.UseSetting("ConnectionStrings:Default", connectionString);
        builder.ConfigureServices(services =>
        {
            services.RemoveAll<IHostedService>();
            services.RemoveAll<ISmsProviderClient>();
            services.AddSingleton(Provider);
            services.AddSingleton<ILoggerProvider>(Logs);
        });
    }
}
