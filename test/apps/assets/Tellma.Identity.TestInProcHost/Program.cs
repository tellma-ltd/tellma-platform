// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

using Tellma.Identity.Hosting;

namespace Tellma.Identity.TestInProcHost
{
    /// <summary>
    ///     Entry point for the distribution-shaped test host: a web app with its own root route
    ///     that mounts the identity engine in-proc at the reserved <c>/id</c> path base, exactly
    ///     the way a distribution's web host would.
    /// </summary>
    public static class Program
    {
        /// <summary>Builds and runs the in-proc test host.</summary>
        /// <param name="args">Command-line arguments forwarded to the host builder.</param>
        public static void Main(string[] args)
        {
            WebApplicationBuilder builder = WebApplication.CreateBuilder(args);

            // Same single registration path as the standalone host, in in-proc shape.
            builder.Services.AddTellmaIdentity(builder.Configuration.GetSection("TellmaIdentity"));

            WebApplication app = builder.Build();

            app.UseTellmaIdentity();
            app.UseAuthentication();
            app.UseAuthorization();

            app.MapStaticAssets();

            // The "distribution's" own surface, proving host routes coexist with the engine's.
            app.MapGet("/", static () => "Distribution host");

            app.MapTellmaIdentity();

            app.Run();
        }
    }
}
