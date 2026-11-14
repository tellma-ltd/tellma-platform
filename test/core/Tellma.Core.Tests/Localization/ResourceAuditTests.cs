// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

using System.Xml.Linq;
using Tellma.Testing.Resources;

namespace Tellma.Core.Tests.Localization
{
    /// <summary>
    ///     The resource audit every feature's suite runs: an orphan, a satellite-only key and a stale
    ///     pending entry each fail it; the literal rules; the source tokenizers; and the repository
    ///     root found from the build output on any operating system.
    /// </summary>
    public sealed class ResourceAuditTests : IDisposable
    {
        /// <summary>A scratch folder for the resource files of one test.</summary>
        private readonly DirectoryInfo _folder = Directory.CreateTempSubdirectory("tellma-audit-");

        [Fact]
        public void Sound_resources_pass()
        {
            string neutral = Resx("Strings.resx", "Acme_Order", "Acme_Order_Plural", "Settings_Unknown");
            _ = Resx("Strings.ar.resx", "Acme_Order");

            IReadOnlyList<ResourceAuditFinding> findings = ResourceAudit.Run(new ResourceAuditRequest
            {
                NeutralFiles = [neutral],
                DerivedKeys = new HashSet<string> { "Acme_Order", "Acme_Order_Plural" },
                Literals = new HashSet<string> { "Settings.Unknown" },
            });

            Assert.Empty(findings);
        }

        [Fact]
        public void An_orphan_fails_the_audit()
        {
            string neutral = Resx("Strings.resx", "Acme_Order", "Acme_Forgotten");

            ResourceAuditFinding finding = Assert.Single(ResourceAudit.Run(new ResourceAuditRequest
            {
                NeutralFiles = [neutral],
                DerivedKeys = new HashSet<string> { "Acme_Order" },
                Literals = new HashSet<string> { "Acme Forgotten" },
            }));

            Assert.Equal(new ResourceAuditFinding(ResourceAuditFindingKind.Orphan, neutral, "Acme_Forgotten"), finding);
            Assert.Contains("'Acme_Forgotten'", ResourceAudit.Report([finding]), StringComparison.Ordinal);
        }

        [Fact]
        public void A_satellite_only_key_fails_the_audit()
        {
            string neutral = Resx("Strings.resx", "Acme_Order");
            string arabic = Resx("Strings.ar.resx", "Acme_Order", "Acme_Stray");

            ResourceAuditFinding finding = Assert.Single(ResourceAudit.Run(new ResourceAuditRequest
            {
                NeutralFiles = [neutral],
                DerivedKeys = new HashSet<string> { "Acme_Order", "Acme_Stray" },
                Literals = new HashSet<string>(),
            }));

            Assert.Equal(new ResourceAuditFinding(ResourceAuditFindingKind.SatelliteOnly, arabic, "Acme_Stray"), finding);
        }

        [Fact]
        public void A_pending_key_spares_its_key_until_it_is_used_or_gone()
        {
            string neutral = Resx("Strings.resx", "Acme_Waiting", "Acme_Arrived");

            IReadOnlyList<ResourceAuditFinding> findings = ResourceAudit.Run(new ResourceAuditRequest
            {
                NeutralFiles = [neutral],
                DerivedKeys = new HashSet<string> { "Acme_Arrived" },
                Literals = new HashSet<string>(),
                Pending = new Dictionary<string, string> { ["Acme_Waiting"] = "later", ["Acme_Arrived"] = "later", ["Acme_Gone"] = "later" },
            });

            Assert.Equal(
                [
                    new ResourceAuditFinding(ResourceAuditFindingKind.StalePending, neutral, "Acme_Arrived"),
                    new ResourceAuditFinding(ResourceAuditFindingKind.StalePending, string.Empty, "Acme_Gone"),
                ],
                findings);
        }

        [Fact]
        public void A_non_string_resource_is_not_a_key()
        {
            string neutral = Resx("Strings.resx", "Acme_Order");
            var document = XDocument.Load(neutral);
            document.Root!.Add(new XElement("data", new XAttribute("name", "Acme_Logo"), new XAttribute("type", "System.Resources.ResXFileRef, System.Windows.Forms"), new XElement("value", "logo.png")));
            document.Save(neutral);

            Assert.Equal(["Acme_Order"], ResxFile.Read(neutral).Keys);
        }

        [Theory]
        [InlineData("concurrency-conflict", new[] { "concurrency-conflict", "Problem_concurrency_conflict", "Problem_concurrency_conflict_Detail" })]
        [InlineData("Settings.TimeZoneUnknown", new[] { "Settings.TimeZoneUnknown", "Settings_TimeZoneUnknown" })]
        [InlineData("QX3001", new[] { "QX3001", "Queryex_QX3001" })]
        [InlineData("Boot_Title", new[] { "Boot_Title" })]
        [InlineData("Not-kebab", new[] { "Not-kebab" })]
        public void The_platform_rules_turn_a_literal_into_the_keys_it_uses(string literal, string[] keys)
        {
            Assert.Equal(keys, LiteralKeyRules.Platform.SelectMany(rule => rule(literal)));
        }

        [Fact]
        public void The_csharp_tokenizer_reads_every_kind_of_string_and_skips_comments_and_characters()
        {
            string source = string.Join(
                "\n",
                "// \"Commented_Line\"",
                "/* \"Commented_Block\" */",
                "char quote = '\"'; char escaped = '\\'';",
                "string a = \"Regular_Key\";",
                "string b = @\"Verbatim \"\"Key\"\"\";",
                "string c = $\"Problem_{code} {(flag ? \"Nested_Key\" : \"Other_Key\")} {{literal}}\";",
                "string d = \"\"\"",
                "    Raw_Key",
                "    \"\"\";",
                "string e = $$\"\"\"{\"Raw_Text\"} {{\"Hole_Key\"}}\"\"\";",
                "string f = \"Escaped \\\"Quote\\\"\";",
                "string g = @x;");

            IReadOnlyList<string> literals = SourceLiterals.InCSharp(source);

            Assert.Equal(
                ["Regular_Key", "Verbatim \"Key\"", "Problem_", " ", "Nested_Key", "Other_Key", " {literal}", "Raw_Key", "{\"Raw_Text\"} ", "Hole_Key", string.Empty, "Escaped \"Quote\""],
                literals);
        }

        [Fact]
        public void The_typescript_tokenizer_reads_strings_and_templates_and_skips_comments_and_regular_expressions()
        {
            string source = string.Join(
                "\n",
                "// 'Commented_Line'",
                "/* \"Commented_Block\" */",
                "const a = 'Single_Key';",
                "const b = \"Double_Key\";",
                "const c = `Template ${flag ? 'Nested_Key' : `inner`} end`;",
                "const r = /'not a string'/g.test(x) ? 1 / 2 : 3;",
                "const d = 'Escaped \\'Quote\\'';",
                "if (x) return /\"also not\"/.source;");

            IReadOnlyList<string> literals = SourceLiterals.InTypeScript(source);

            Assert.Equal(["Single_Key", "Double_Key", "Template ", "Nested_Key", "inner", " end", "Escaped 'Quote'"], literals);
        }

        [Fact]
        public void The_source_reader_skips_build_output_dependencies_and_typescript_tests()
        {
            Write("src/Shipped.cs", "class A { string s = \"Shipped_Key\"; }");
            Write("src/app.ts", "export const k = 'App_Key';");
            Write("src/app.spec.ts", "const k = 'Spec_Key';");
            Write("src/bin/Debug/Generated.cs", "class B { string s = \"Bin_Key\"; }");
            Write("src/node_modules/lib/index.ts", "export const k = 'Dependency_Key';");
            Write("src/.angular/cache.ts", "export const k = 'Cache_Key';");

            IReadOnlySet<string> literals = SourceLiterals.Read([Path.Combine(_folder.FullName, "src")]);

            Assert.Equal(["App_Key", "Shipped_Key"], literals.Order(StringComparer.Ordinal));
            _ = Assert.Throws<DirectoryNotFoundException>(() => SourceLiterals.Read([Path.Combine(_folder.FullName, "missing")]));
        }

        [Fact]
        public void The_repository_root_is_the_directory_above_the_build_output_that_holds_the_solution()
        {
            string root = RepositoryRoot.Find();

            Assert.True(File.Exists(Path.Combine(root, RepositoryRoot.SolutionFile)));
            Assert.StartsWith(root, AppContext.BaseDirectory, StringComparison.OrdinalIgnoreCase);
            Assert.Equal(Path.Combine(root, "src", "core"), RepositoryRoot.Combine("src", "core"));
            _ = Assert.Throws<InvalidOperationException>(() => RepositoryRoot.Find(_folder.FullName));
        }

        /// <inheritdoc />
        public void Dispose()
        {
            _folder.Delete(recursive: true);
        }

        /// <summary>Writes a resource file of string entries, each valued as its key, into the scratch folder.</summary>
        /// <param name="name">The file's name.</param>
        /// <param name="keys">The keys.</param>
        /// <returns>The file's path.</returns>
        private string Resx(string name, params string[] keys)
        {
            XDocument document = new(new XElement(
                "root",
                keys.Select(static key => new XElement("data", new XAttribute("name", key), new XElement("value", key)))));
            string path = Path.Combine(_folder.FullName, name);
            document.Save(path);
            return path;
        }

        /// <summary>Writes a source file into the scratch folder.</summary>
        /// <param name="relativePath">The file's path under the folder, with forward slashes.</param>
        /// <param name="text">The file's text.</param>
        private void Write(string relativePath, string text)
        {
            string path = Path.Combine(_folder.FullName, relativePath.Replace('/', Path.DirectorySeparatorChar));
            _ = Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            File.WriteAllText(path, text);
        }
    }
}
