// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

using Microsoft.AspNetCore.Builder;
using Microsoft.Extensions.Configuration;
using Tellma.Core.AspNetCore.Hosting;

namespace Tellma.Core.AspNetCore.Tests.Hosting
{
    /// <summary>
    ///     The worktree's ports file: in Development it sets the listening port and the public origin,
    ///     below the environment and the command line and above the configuration files; elsewhere, or
    ///     without the file, nothing changes.
    /// </summary>
    public sealed class DevelopmentPortsTests : IDisposable
    {
        private readonly string _root = Path.Combine(Path.GetTempPath(), "tellma-ports-" + Guid.NewGuid().ToString("N"));

        private readonly string _repository;

        /// <summary>A repository inside a folder of its own, with a project folder two levels down as the content root.</summary>
        public DevelopmentPortsTests()
        {
            _repository = Path.Combine(_root, "repository");
            _ = Directory.CreateDirectory(Path.Combine(_repository, ".git"));
            _ = Directory.CreateDirectory(ContentRoot);
        }

        private string ContentRoot => Path.Combine(_repository, "distributions", "acme", "web");

        [Fact]
        public void The_file_is_read_as_key_value_lines()
        {
            Dictionary<string, string> values = DevelopmentPorts.Parse("# ports\nACME_WEB=7120\r\n  ACME_CLIENT = \"4220\" \n\nBROKEN\nDB_SUFFIX='pc2'\n");

            Assert.Equal(
                new Dictionary<string, string> { ["ACME_WEB"] = "7120", ["ACME_CLIENT"] = "4220", ["DB_SUFFIX"] = "pc2" },
                values);
        }

        [Fact]
        public void The_keys_are_the_slug_in_upper_case()
        {
            Assert.Equal("ACME_WEB", DevelopmentPorts.WebKey("acme"));
            Assert.Equal("MY_ERP_CLIENT", DevelopmentPorts.ClientKey("my-erp"));
        }

        [Fact]
        public void The_file_is_found_up_to_the_repository_s_root_and_no_further()
        {
            Assert.Null(DevelopmentPorts.Find(ContentRoot));

            // Above the repository's root, outside the checkout, it is never read.
            File.WriteAllText(Path.Combine(_root, DevelopmentPorts.FileName), "ACME_WEB=7999");
            Assert.Null(DevelopmentPorts.Find(ContentRoot));

            WritePorts("ACME_WEB=7120");
            Assert.Equal(Path.Combine(_repository, DevelopmentPorts.FileName), DevelopmentPorts.Find(ContentRoot));
        }

        [Fact]
        public void Development_listens_on_the_web_port_and_serves_the_client_s_origin()
        {
            WritePorts("ACME_WEB=7120\nACME_CLIENT=4220");

            ConfigurationManager configuration = Apply("Development", files: new() { ["Tellma:PublicOrigin"] = "https://localhost:4200", ["urls"] = "https://localhost:7052" });

            Assert.Equal("https://localhost:7120", configuration["urls"]);
            Assert.Equal("https://localhost:4220", configuration["Tellma:PublicOrigin"]);
        }

        [Fact]
        public void The_environment_and_the_command_line_still_win()
        {
            WritePorts("ACME_WEB=7120\nACME_CLIENT=4220");

            ConfigurationManager configuration = Apply(
                "Development",
                files: new() { ["Tellma:PublicOrigin"] = "https://localhost:4200" },
                overrides: new() { ["Tellma:PublicOrigin"] = "https://localhost:9999", ["urls"] = "https://localhost:9998" });

            Assert.Equal("https://localhost:9998", configuration["urls"]);
            Assert.Equal("https://localhost:9999", configuration["Tellma:PublicOrigin"]);
        }

        [Fact]
        public void A_port_the_file_does_not_name_keeps_its_fallback()
        {
            WritePorts("ACME_WEB=7120");

            ConfigurationManager configuration = Apply("Development", files: new() { ["Tellma:PublicOrigin"] = "https://localhost:4200" });

            Assert.Equal("https://localhost:7120", configuration["urls"]);
            Assert.Equal("https://localhost:4200", configuration["Tellma:PublicOrigin"]);
        }

        [Fact]
        public void Outside_Development_the_file_is_ignored()
        {
            WritePorts("ACME_WEB=7120\nACME_CLIENT=4220");

            ConfigurationManager configuration = Apply("Production", files: new() { ["Tellma:PublicOrigin"] = "https://acme.app.tellma.com" });

            Assert.Null(configuration["urls"]);
            Assert.Equal("https://acme.app.tellma.com", configuration["Tellma:PublicOrigin"]);
        }

        [Theory]
        [InlineData("ACME_WEB=http")]
        [InlineData("ACME_CLIENT=70000")]
        [InlineData("ACME_WEB=-1")]
        public void A_value_that_is_not_a_port_fails_naming_the_file_and_the_key(string line)
        {
            WritePorts(line);

            InvalidOperationException exception = Assert.Throws<InvalidOperationException>(() => Apply("Development", files: []));

            Assert.Contains(DevelopmentPorts.FileName, exception.Message, StringComparison.Ordinal);
            Assert.Contains(line.Split('=')[0], exception.Message, StringComparison.Ordinal);
        }

        /// <inheritdoc />
        public void Dispose()
        {
            Directory.Delete(_root, recursive: true);
        }

        /// <summary>Writes the repository's ports file.</summary>
        /// <param name="text">Its text.</param>
        private void WritePorts(string text)
        {
            File.WriteAllText(Path.Combine(_repository, DevelopmentPorts.FileName), text);
        }

        /// <summary>
        ///     A builder whose configuration has a JSON file below and the environment's values above,
        ///     as a web host's has, with the ports applied.
        /// </summary>
        /// <param name="environment">The host environment.</param>
        /// <param name="files">What the configuration files say.</param>
        /// <param name="overrides">What the environment's variables and the command line say, or null.</param>
        /// <returns>The resulting configuration.</returns>
        private ConfigurationManager Apply(string environment, Dictionary<string, string?> files, Dictionary<string, string?>? overrides = null)
        {
            WebApplicationBuilder builder = WebApplication.CreateBuilder(new WebApplicationOptions
            {
                EnvironmentName = environment,
                ContentRootPath = ContentRoot,
            });
            builder.Configuration.Sources.Clear();

            // A configuration file, then what follows the files in a web host's configuration.
            string json = Path.Combine(ContentRoot, "appsettings.json");
            File.WriteAllText(json, System.Text.Json.JsonSerializer.Serialize(Nest(files)));
            _ = builder.Configuration.AddJsonFile(json, optional: false);
            _ = builder.Configuration.AddInMemoryCollection(overrides ?? []);

            DevelopmentPorts.Apply(builder, "acme");
            return builder.Configuration;
        }

        /// <summary>Nests colon-separated keys into the objects a JSON file holds.</summary>
        /// <param name="values">The flat values.</param>
        /// <returns>The nested values.</returns>
        private static Dictionary<string, object?> Nest(Dictionary<string, string?> values)
        {
            Dictionary<string, object?> root = [];
            foreach ((string key, string? value) in values)
            {
                string[] path = key.Split(':');
                Dictionary<string, object?> node = root;
                foreach (string segment in path[..^1])
                {
                    if (!node.TryGetValue(segment, out object? child) || child is not Dictionary<string, object?> nested)
                    {
                        nested = [];
                        node[segment] = nested;
                    }

                    node = nested;
                }

                node[path[^1]] = value;
            }

            return root;
        }
    }
}
