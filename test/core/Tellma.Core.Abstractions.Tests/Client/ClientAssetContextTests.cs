// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

using System.Text;
using Tellma.Core.Abstractions.Client;

namespace Tellma.Core.Abstractions.Tests.Client
{
    /// <summary>What the client-asset context accepts from a contributor, and what it refuses.</summary>
    public class ClientAssetContextTests
    {
        private readonly ClientAssetContext _context = new(new EmptyServices());

        [Fact]
        public void A_section_captures_the_one_value_its_writer_wrote()
        {
            _context.Section(ClientMetadataChunk.Core, "routing", static writer =>
            {
                writer.WriteStartObject();
                writer.WriteString("slug", "acme");
                writer.WriteEndObject();
            });

            ClientMetadataSection section = Assert.Single(_context.Sections);
            Assert.Equal((ClientMetadataChunk.Core, "routing"), (section.Chunk, section.Name));
            Assert.Equal(/*lang=json,strict*/ """{"slug":"acme"}""", Encoding.UTF8.GetString(section.Json.Span));
        }

        [Fact]
        public void One_name_may_appear_once_per_chunk()
        {
            _context.Section(ClientMetadataChunk.Core, "features", static writer => writer.WriteNumberValue(1));
            _context.Section(ClientMetadataChunk.Entities, "features", static writer => writer.WriteNumberValue(2));

            _ = Assert.Throws<InvalidOperationException>(() =>
                _context.Section(ClientMetadataChunk.Core, "features", static writer => writer.WriteNumberValue(3)));
            Assert.Equal(2, _context.Sections.Count);
        }

        [Theory]
        [InlineData("Routing")]
        [InlineData("setting-keys")]
        [InlineData("1st")]
        [InlineData("")]
        public void A_section_name_is_a_camelCase_identifier(string name)
        {
            _ = Assert.Throws<ArgumentException>(() => _context.Section(ClientMetadataChunk.Core, name, static writer => writer.WriteNullValue()));
        }

        [Fact]
        public void A_section_needs_a_defined_chunk_and_a_writer()
        {
            _ = Assert.Throws<ArgumentOutOfRangeException>(() => _context.Section((ClientMetadataChunk)9, "routing", static writer => writer.WriteNullValue()));
            _ = Assert.Throws<ArgumentNullException>(() => _context.Section(ClientMetadataChunk.Core, "routing", null!));
        }

        [Fact]
        public void A_writer_that_writes_nothing_or_leaves_a_value_open_is_refused()
        {
            _ = Assert.Throws<InvalidOperationException>(() => _context.Section(ClientMetadataChunk.Core, "empty", static _ => { }));
            _ = Assert.Throws<InvalidOperationException>(() => _context.Section(ClientMetadataChunk.Core, "open", static writer => writer.WriteStartArray()));
            Assert.Empty(_context.Sections);
        }

        [Fact]
        public void A_writer_that_writes_two_values_fails_inside_its_own_call()
        {
            _ = Assert.Throws<InvalidOperationException>(() => _context.Section(ClientMetadataChunk.Core, "two", static writer =>
            {
                writer.WriteNumberValue(1);
                writer.WriteNumberValue(2);
            }));
        }

        [Fact]
        public void A_file_is_recorded_under_its_relative_path()
        {
            _context.File("types/tellma-entities.d.ts", "export {};\n");

            ClientGeneratedFile file = Assert.Single(_context.Files);
            Assert.Equal(("types/tellma-entities.d.ts", "export {};\n"), (file.RelativePath, file.Content));
        }

        [Theory]
        [InlineData("")]
        [InlineData("/rooted.ts")]
        [InlineData("dir/")]
        [InlineData("a//b.ts")]
        [InlineData("../escape.ts")]
        [InlineData("./here.ts")]
        [InlineData("windows\\path.ts")]
        [InlineData("c:drive.ts")]
        [InlineData("what?.ts")]
        public void A_file_path_is_a_plain_relative_path(string path)
        {
            _ = Assert.Throws<ArgumentException>(() => _context.File(path, string.Empty));
        }

        [Fact]
        public void Two_files_whose_paths_differ_only_in_case_collide()
        {
            _context.File("tellma-metadata.ts", "a");

            _ = Assert.Throws<InvalidOperationException>(() => _context.File("Tellma-Metadata.ts", "b"));
            _ = Assert.Throws<ArgumentNullException>(() => _context.File("other.ts", null!));
        }

        [Fact]
        public void The_context_needs_services()
        {
            _ = Assert.Throws<ArgumentNullException>(() => new ClientAssetContext(null!));
        }

        /// <summary>A provider with no services.</summary>
        private sealed class EmptyServices : IServiceProvider
        {
            /// <inheritdoc />
            public object? GetService(Type serviceType)
            {
                return null;
            }
        }
    }
}
