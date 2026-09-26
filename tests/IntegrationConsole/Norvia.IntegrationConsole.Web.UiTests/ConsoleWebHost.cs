using System.Reflection;
using Azure.Core;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Hosting.Server;
using Microsoft.AspNetCore.Hosting.Server.Features;
using Microsoft.AspNetCore.Http.Features;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Norvia.IntegrationConsole.Application.Operators;

namespace Norvia.IntegrationConsole.Web.UiTests;

/// <summary>
/// The console on Kestrel with every port faked, so a browser drives it without Azure. Every interface in the
/// Application project is a port, and each is replaced by an NSubstitute fake that a test configures through
/// <see cref="Port{T}"/>. The credential the adapters would call Azure with is faked too, so nothing leaves the machine.
/// Operators sign in as Easy Auth signs them in, by the header it adds.
/// </summary>
public sealed class ConsoleWebHost : WebApplicationFactory<Program>
{
    private static readonly Assembly Application = typeof(OperatorRoles).Assembly;

    private readonly Dictionary<Type, object> _ports = Application.GetTypes()
        .Where(type => type.IsInterface)
        .ToDictionary(port => port, port => Substitute.For([port], []));

    public ConsoleWebHost() => UseKestrel(0);

    /// <summary>The ports the console's Application layer defines, each with its fake.</summary>
    public IReadOnlyDictionary<Type, object> Ports => _ports;

    /// <summary>Where the console listens.</summary>
    public Uri Address =>
        new(Services.GetRequiredService<IServer>().Features.GetRequiredFeature<IServerAddressesFeature>().Addresses.First());

    /// <summary>The fake behind a port, for a test to say what it answers.</summary>
    public T Port<T>()
        where T : class => (T)_ports[typeof(T)];

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        ArgumentNullException.ThrowIfNull(builder);

        builder.UseEnvironment("Test");
        // Outside Development the host doesn't read the build's static web assets on its own, and interactive pages need
        // the framework's script from them.
        builder.UseStaticWebAssets();
        // What the estate Bicep passes on as integrationRetentionDays.
        builder.UseSetting("Estate:IntegrationRetentionDays", "90");
        builder.ConfigureServices(services =>
        {
            foreach (var (port, fake) in _ports)
            {
                services.RemoveAll(port);
                services.AddSingleton(port, fake);
            }

            services.RemoveAll<TokenCredential>();
            services.AddSingleton(Substitute.For<TokenCredential>());
        });
    }
}
