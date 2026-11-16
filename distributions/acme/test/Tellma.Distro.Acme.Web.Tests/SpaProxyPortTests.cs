// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

using System.Text.RegularExpressions;
using System.Xml.Linq;
using Tellma.Distro.Acme.Web.Tests.Infrastructure;

namespace Tellma.Distro.Acme.Web.Tests
{
    /// <summary>
    ///     The port the web project's build gives the SPA proxy is the one the web host and the client
    ///     dev server read from the worktree's port file: the last <c>ACME_CLIENT</c> line wins, and
    ///     only digits, bare or in matching quotes, make a port.
    /// </summary>
    public sealed class SpaProxyPortTests
    {
        [Theory]
        [InlineData("ACME_CLIENT=4210", "4210")]
        [InlineData("ACME_WEB=7110\nACME_CLIENT=4210\nDB_SUFFIX=pc1\n", "4210")]
        [InlineData("ACME_CLIENT=4221\nACME_CLIENT=4222\n", "4222")]
        [InlineData("ACME_CLIENT=4221\r\nACME_CLIENT=4222\r\n", "4222")]
        [InlineData("ACME_CLIENT=\"4223\"", "4223")]
        [InlineData("ACME_CLIENT='4224'\r\n", "4224")]
        [InlineData("  ACME_CLIENT = 4225 \t\n", "4225")]
        [InlineData("ACME_CLIENT=4221\n# ACME_CLIENT=4226\n", "4221")]
        [InlineData("ACME_CLIENT=-4221", "")]
        [InlineData("ACME_CLIENT=4221\nACME_CLIENT=abc\n", "")]
        [InlineData("ACME_CLIENT=\"4227'", "")]
        [InlineData("ACME_CLIENT=42 27", "")]
        [InlineData("ACME_CLIENT_SHOWCASE=4228\nMY_ACME_CLIENT=4229\n", "")]
        [InlineData("", "")]
        public void The_build_reads_the_client_port_as_the_web_host_does(string file, string port)
        {
            // An empty result is the build's cue to fall back to 4200.
            Assert.Equal(port, Regex.Match(file, PortPattern()).Groups[2].Value);
        }

        /// <summary>
        ///     The pattern the web project's build matches the port file with, as MSBuild sees it: the
        ///     backtick-quoted argument of the property that holds the port.
        /// </summary>
        /// <returns>The pattern.</returns>
        private static string PortPattern()
        {
            string project = Path.Combine(AcmePaths.Web, "Tellma.Distro.Acme.Web.csproj");
            string property = XDocument.Load(project).Descendants("_AcmeClientPort")
                .Select(static element => element.Value)
                .Single(static value => value.Contains('`', StringComparison.Ordinal));
            int start = property.IndexOf('`', StringComparison.Ordinal) + 1;
            return property[start..property.IndexOf('`', start)];
        }
    }
}
