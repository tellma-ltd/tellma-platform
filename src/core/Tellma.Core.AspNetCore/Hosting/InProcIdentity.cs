// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Mvc.ApplicationModels;
using Microsoft.AspNetCore.Mvc.Controllers;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using System.Globalization;
using System.Reflection;
using Tellma.Core.Abstractions.Composition;
using Tellma.Core.Abstractions.Hosting;
using Tellma.Core.AspNetCore.Bff;
using Tellma.Core.AspNetCore.Routing;
using Tellma.Core.Tenancy;

namespace Tellma.Core.AspNetCore.Hosting
{
    /// <summary>
    ///     The in-proc identity engine's wiring: its registration with the options the distribution
    ///     dictates, its middleware at the pipeline's eighth step, and its endpoints under the one
    ///     convention that makes them tenantless and keeps the platform's fallback policy off the
    ///     protocol endpoints.
    /// </summary>
    /// <remarks>
    ///     The web host takes no compile-time dependency on the engine, which most distributions never
    ///     run: a distribution that runs it in-proc references the engine's package, and the engine's three
    ///     entry points are bound here by name. A host configured in-proc without the package fails at
    ///     composition, naming the package.
    /// </remarks>
    internal static class InProcIdentity
    {
        /// <summary>The configuration section the engine's own settings bind from.</summary>
        public const string EngineSectionPath = "TellmaIdentity";

        /// <summary>The engine's assembly.</summary>
        public const string EngineAssembly = "Tellma.Identity";

        /// <summary>The tenantless reason the engine's endpoints carry.</summary>
        public const string TenantlessReason = "identity";

        /// <summary>The engine's Razor Pages area.</summary>
        public const string EngineArea = "Identity";

        /// <summary>The application property that records that the engine is composed.</summary>
        internal const string ComposedKey = "Tellma.InProcIdentity";

        /// <summary>The engine's registration entry point.</summary>
        private const string ServicesType = "Tellma.Identity.Hosting.TellmaIdentityServiceCollectionExtensions";

        /// <summary>The engine's middleware entry point.</summary>
        private const string PipelineType = "Tellma.Identity.Hosting.TellmaIdentityApplicationBuilderExtensions";

        /// <summary>The engine's registration method.</summary>
        private const string AddMethod = "AddTellmaIdentity";

        /// <summary>The engine's middleware method.</summary>
        private const string UseMethod = "UseTellmaIdentity";

        /// <summary>The route prefix of every engine endpoint, without slashes.</summary>
        private static readonly string RoutePrefix = IdentityHostOptions.InProcPath.Trim('/');

        /// <summary>Whether a configuration runs the engine in-proc.</summary>
        /// <param name="configuration">The application's configuration.</param>
        /// <returns>True in in-proc identity mode.</returns>
        public static bool IsInProc(IConfiguration configuration)
        {
            return HostSettings.Read<IdentityHostOptions>(configuration, IdentityHostOptions.SectionPath).Mode == IdentityMode.InProc;
        }

        // ------------------------------------------------------------------------------------------
        // Registration
        // ------------------------------------------------------------------------------------------

        /// <summary>
        ///     Registers the engine in-proc: mode, path base and issuer under the public origin, the store
        ///     on the catalog's connection, no migrations at startup, and the distribution's own clients
        ///     seeded with their secrets. Called after the tenancy runtime registered the registry's
        ///     refresh, so its first load runs before the engine's store check and seeding.
        /// </summary>
        /// <param name="services">The services.</param>
        /// <param name="slug">The distribution's slug, its client id.</param>
        /// <param name="configuration">The application's configuration.</param>
        /// <param name="environment">The host environment.</param>
        /// <param name="secrets">The clients' secrets.</param>
        /// <exception cref="TellmaCompositionException">The engine is not referenced, or the catalog or public origin is not configured.</exception>
        public static void Register(IServiceCollection services, string slug, IConfiguration configuration, IHostEnvironment environment, IdentityClientSecrets secrets)
        {
            ArgumentNullException.ThrowIfNull(services);
            ArgumentNullException.ThrowIfNull(configuration);
            ArgumentNullException.ThrowIfNull(environment);
            ArgumentNullException.ThrowIfNull(secrets);

            // --- What the engine is derived from: the public origin and the catalog ---
            List<CompositionProblem> problems = [];
            string? origin = configuration[HostConfigurationKeys.PublicOrigin];
            if (!Uri.TryCreate(origin, UriKind.Absolute, out Uri? publicOrigin) || origin.EndsWith('/'))
            {
                problems.Add(Problem(
                    $"{HostConfigurationKeys.PublicOrigin} is not an absolute origin; the in-proc identity engine's issuer is derived from it.",
                    $"Set {HostConfigurationKeys.PublicOrigin} to the distribution's origin, such as https://acme.example.com, without a trailing slash."));
            }

            string? catalog = configuration[TellmaCatalogOptions.SectionPath + ":" + nameof(TellmaCatalogOptions.ConnectionString)];
            if (catalog is null || !TellmaCatalogOptions.Parses(catalog))
            {
                problems.Add(Problem(
                    $"{TellmaCatalogOptions.SectionPath}:{nameof(TellmaCatalogOptions.ConnectionString)} is not a valid connection string; the in-proc identity engine keeps its store in the catalog.",
                    $"Set {TellmaCatalogOptions.SectionPath}:{nameof(TellmaCatalogOptions.ConnectionString)}."));
            }

            // --- The engine's entry point: a missing assembly and an incompatible one are told apart ---
            (Type? servicesType, MethodInfo? add) = EntryPoint(ServicesType, AddMethod, typeof(IServiceCollection), typeof(IConfiguration));
            if (servicesType is null)
            {
                problems.Add(Problem(
                    $"{IdentityHostOptions.SectionPath}:{nameof(IdentityHostOptions.Mode)} is InProc, but the identity engine's assembly, {EngineAssembly}, is not referenced by the web host.",
                    $"Reference the {EngineAssembly} package from the distribution's web host, or set {IdentityHostOptions.SectionPath}:{nameof(IdentityHostOptions.Mode)} to Standalone."));
            }
            else if (add is null)
            {
                problems.Add(Problem(
                    $"The referenced {EngineAssembly} assembly has no public static {ServicesType}.{AddMethod}(IServiceCollection, IConfiguration), so its version does not match this web host's.",
                    $"Reference the {EngineAssembly} package of the same release as the platform's packages."));
            }

            if (problems.Count > 0)
            {
                throw new TellmaCompositionException(problems);
            }

            // --- The engine's own section, overlaid with what the distribution dictates ---
            string composedCatalog = TenantConnectionStrings.ComposeCatalog(catalog!, new DeploymentIdentity(slug, environment.EnvironmentName).DeploymentId);
            IConfiguration engine = new ConfigurationBuilder()
                .AddConfiguration(configuration.GetSection(EngineSectionPath))
                .AddInMemoryCollection(Overrides(slug, origin!, publicOrigin!, composedCatalog, environment, configuration, secrets))
                .Build();

            _ = add!.Invoke(null, [services, engine]);

            // --- The engine's mail: sent outside any tenant, so given the engine's own sandbox answer ---
            InProcIdentityMail.Register(services);
        }

        /// <summary>The settings the distribution dictates to the engine.</summary>
        /// <param name="slug">The distribution's slug.</param>
        /// <param name="origin">The public origin, as configured.</param>
        /// <param name="publicOrigin">The public origin, parsed.</param>
        /// <param name="catalog">The composed catalog connection string.</param>
        /// <param name="environment">The host environment.</param>
        /// <param name="configuration">The application's configuration.</param>
        /// <param name="secrets">The clients' secrets.</param>
        /// <returns>The settings, keyed under the engine's section.</returns>
        internal static Dictionary<string, string?> Overrides(
            string slug,
            string origin,
            Uri publicOrigin,
            string catalog,
            IHostEnvironment environment,
            IConfiguration configuration,
            IdentityClientSecrets secrets)
        {
            // The distribution's seed entry goes after any the engine's section already lists.
            int index = configuration.GetSection(EngineSectionPath + ":Seed:Clients").GetChildren()
                .Select(static child => int.TryParse(child.Key, NumberStyles.None, CultureInfo.InvariantCulture, out int position) ? position + 1 : 0)
                .DefaultIfEmpty(0)
                .Max();
            string client = string.Create(CultureInfo.InvariantCulture, $"Seed:Clients:{index}:");

            Dictionary<string, string?> overrides = new(StringComparer.OrdinalIgnoreCase)
            {
                ["Mode"] = "InProc",
                ["PathBase"] = IdentityHostOptions.InProcPath,
                ["Issuer"] = origin + IdentityHostOptions.InProcPath,
                ["ConnectionString"] = catalog,
                ["Seed:ApplyMigrations"] = "false",
                ["Seed:MigrateCommand"] = new TenancyEnvironment(slug, environment.IsDevelopment()).MigrateCommand,
                [client + "ClientId"] = slug,
                [client + "Kind"] = "Distribution",
                [client + "Enabled"] = "true",
                [client + "Origin"] = origin,
                [client + "BackchannelLogoutUri"] = origin + BffEndpoints.BackchannelLogoutRoute,
                [client + "ClientSecret"] = secrets.ClientSecret,
                [client + "ServiceClientSecret"] = secrets.ServiceClientSecret,
            };

            // Development over plain HTTP: the engine refuses an http issuer unless told this is local.
            if (environment.IsDevelopment() && publicOrigin.Scheme == Uri.UriSchemeHttp)
            {
                overrides["Development:AllowInsecureHttp"] = "true";
            }

            return overrides;
        }

        // ------------------------------------------------------------------------------------------
        // The pipeline and the endpoints
        // ------------------------------------------------------------------------------------------

        /// <summary>Installs the engine's middleware: request localization, its asset caching and security headers.</summary>
        /// <param name="app">The application, past routing.</param>
        public static void Use(IApplicationBuilder app)
        {
            ArgumentNullException.ThrowIfNull(app);
            MethodInfo use = EntryPoint(PipelineType, UseMethod, typeof(IApplicationBuilder)).Method
                ?? throw new InvalidOperationException(
                    $"The referenced {EngineAssembly} assembly has no public static {PipelineType}.{UseMethod}(IApplicationBuilder); reference the {EngineAssembly} package of the same release as the platform's packages.");
            _ = use.Invoke(null, [app]);
            app.Properties[ComposedKey] = true;
        }

        /// <summary>
        ///     Maps the engine's controllers and pages, and stamps every engine endpoint tenantless and the
        ///     platform's; an engine endpoint that states no authorization of its own is made anonymous, so
        ///     the platform's fallback policy never applies to it, and one with <c>[Authorize]</c> keeps its
        ///     own policy and scheme.
        /// </summary>
        /// <param name="routes">The application's routes.</param>
        public static void Map(IEndpointRouteBuilder routes)
        {
            ArgumentNullException.ThrowIfNull(routes);
            var engine = Assembly.Load(EngineAssembly);

            // --- Controllers: their attributes are on the endpoint, so the decision is made here ---
            routes.MapControllers().Add(builder =>
            {
                if (builder.Metadata.OfType<ControllerActionDescriptor>().FirstOrDefault() is { } action
                    && action.ControllerTypeInfo.Assembly == engine
                    && builder is RouteEndpointBuilder route
                    && IsUnderPrefix(route.RoutePattern.RawText))
                {
                    Stamp(builder.Metadata, anonymousUnlessStated: true);
                }
            });

            // --- Pages: the page model's attributes arrive when the page loads, where the page convention decides ---
            routes.MapRazorPages().Add(builder =>
            {
                if (builder.Metadata.OfType<PageActionDescriptor>().FirstOrDefault() is { AreaName: EngineArea }
                    && builder is RouteEndpointBuilder route
                    && IsUnderPrefix(route.RoutePattern.RawText))
                {
                    Stamp(builder.Metadata, anonymousUnlessStated: false);
                }
            });
        }

        /// <summary>
        ///     Registers the page convention that makes an engine page anonymous unless its model states its
        ///     own authorization, decided where the model's attributes are known.
        /// </summary>
        /// <param name="services">The services.</param>
        public static void AddPageConvention(IServiceCollection services)
        {
            _ = services.Configure<RazorPagesOptions>(static pages => pages.Conventions.Add(new EnginePageConvention()));
        }

        /// <summary>Whether a route sits under the engine's path base.</summary>
        /// <param name="route">The route pattern's text.</param>
        /// <returns>True for <c>id</c> and <c>id/…</c>.</returns>
        private static bool IsUnderPrefix(string? route)
        {
            string path = (route ?? string.Empty).TrimStart('/');
            return path.Equals(RoutePrefix, StringComparison.OrdinalIgnoreCase)
                || path.StartsWith(RoutePrefix + "/", StringComparison.OrdinalIgnoreCase);
        }

        /// <summary>Stamps an engine endpoint tenantless and the platform's, and anonymous when it states no authorization.</summary>
        /// <param name="metadata">The endpoint's metadata.</param>
        /// <param name="anonymousUnlessStated">Whether to make it anonymous when it carries neither authorization nor anonymity.</param>
        private static void Stamp(IList<object> metadata, bool anonymousUnlessStated)
        {
            if (anonymousUnlessStated && !metadata.OfType<IAuthorizeData>().Any() && !metadata.OfType<IAllowAnonymous>().Any())
            {
                metadata.Add(new AllowAnonymousAttribute());
            }

            metadata.Add(new TenantlessEndpointMetadata(TenantlessReason));
            metadata.Add(PlatformEndpointMetadata.Instance);
        }

        /// <summary>One of the engine's entry points.</summary>
        /// <param name="typeName">The declaring type's full name.</param>
        /// <param name="method">The method's name.</param>
        /// <param name="parameters">The method's parameter types.</param>
        /// <returns>
        ///     The declaring type, null when the engine is not referenced; and the method, null when the
        ///     type is missing or has no such method.
        /// </returns>
        internal static (Type? Type, MethodInfo? Method) EntryPoint(string typeName, string method, params Type[] parameters)
        {
            var type = Type.GetType($"{typeName}, {EngineAssembly}", throwOnError: false);
            return (type, type?.GetMethod(method, BindingFlags.Public | BindingFlags.Static, parameters));
        }

        /// <summary>A problem of the in-proc composition.</summary>
        /// <param name="problem">What is wrong.</param>
        /// <param name="fix">How to fix it.</param>
        /// <returns>The problem.</returns>
        private static CompositionProblem Problem(string problem, string fix)
        {
            return new CompositionProblem(IdentityCheck.CheckName, problem, fix);
        }

        /// <summary>
        ///     Makes an engine page anonymous when its model states neither <c>[Authorize]</c> nor
        ///     <c>[AllowAnonymous]</c>; a page that states <c>[Authorize]</c> keeps it.
        /// </summary>
        private sealed class EnginePageConvention : IPageApplicationModelConvention
        {
            /// <inheritdoc />
            public void Apply(PageApplicationModel model)
            {
                ArgumentNullException.ThrowIfNull(model);
                if (!string.Equals(model.AreaName, EngineArea, StringComparison.Ordinal)
                    || model.HandlerType.Assembly.GetName().Name != EngineAssembly)
                {
                    return;
                }

                bool stated = model.HandlerTypeAttributes.Any(static attribute => attribute is IAuthorizeData or IAllowAnonymous)
                    || model.EndpointMetadata.Any(static item => item is IAuthorizeData or IAllowAnonymous);
                if (!stated)
                {
                    model.EndpointMetadata.Add(new AllowAnonymousAttribute());
                }
            }
        }
    }
}
