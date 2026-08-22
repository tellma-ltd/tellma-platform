// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

using System.Globalization;

namespace Tellma.Connector.MarminAe.Tests.Infrastructure
{
    /// <summary>That every parser input says where it came from.</summary>
    /// <remarks>
    ///     The distinction between a capture and a stand-in is the difference between evidence and a
    ///     guess, and it is only useful if it cannot be quietly lost. A file added without a
    ///     provenance marker fails here rather than sliding into a review as though it were a
    ///     recording.
    /// </remarks>
    public class MarminAeVectorProvenanceTests
    {
        [Fact]
        public void Every_vector_declares_where_it_came_from()
        {
            List<string> undeclared = [];
            int recorded = 0;
            int synthetic = 0;

            foreach (string path in Vectors.AllFiles())
            {
                string name = Path.GetFileName(path);
                if (string.Equals(name, "PROVENANCE.md", StringComparison.Ordinal))
                {
                    continue;
                }

                if (!Vectors.DeclaresProvenance(name))
                {
                    undeclared.Add(name);
                }
                else if (name.Contains(".recorded.", StringComparison.Ordinal))
                {
                    recorded++;
                }
                else
                {
                    synthetic++;
                }
            }

            // Reported on every run, so the balance between evidence and guesswork is visible
            // without anyone having to go looking for it.
            TestContext.Current.TestOutputHelper?.WriteLine(
                string.Create(
                    CultureInfo.InvariantCulture,
                    $"vectors: {recorded} recorded, {synthetic} synthetic"));

            Assert.Empty(undeclared);
            Assert.True(recorded > 0, "Not one vector is a capture; something has gone wrong with the live suite.");
        }

        [Fact]
        public void Ships_the_note_that_explains_the_difference()
        {
            string provenance = Path.Combine(Vectors.Root, "PROVENANCE.md");

            Assert.True(File.Exists(provenance), $"{provenance} is missing.");
            Assert.Contains(
                "Not a recording",
                File.ReadAllText(provenance),
                StringComparison.Ordinal);
        }

        [Fact]
        public void Prefers_a_capture_over_a_stand_in_of_the_same_name()
        {
            // Somewhere of its own, outside the folder the census walks: a file left behind by a
            // killed run must not be counted as a recording, and must not still be there next time.
            string root = Path.Combine(Path.GetTempPath(), "marminae-vectors-" + Guid.NewGuid().ToString("N"));
            string directory = Path.Combine(root, "Provenance");
            Directory.CreateDirectory(directory);

            try
            {
                File.WriteAllText(
                    Path.Combine(directory, "sample.synthetic.json"),
                    /*lang=json,strict*/ "{\"from\":\"a guess\"}");
                Vector guess = Vectors.LoadFrom(root, "Provenance/sample");
                Assert.Equal(VectorProvenance.Synthetic, guess.Provenance);

                File.WriteAllText(
                    Path.Combine(directory, "sample.recorded.json"),
                    /*lang=json,strict*/ "{\"from\":\"a capture\"}");
                Vector capture = Vectors.LoadFrom(root, "Provenance/sample");

                Assert.Equal(VectorProvenance.Recorded, capture.Provenance);
                Assert.Contains("a capture", capture.Content, StringComparison.Ordinal);
            }
            finally
            {
                Directory.Delete(root, recursive: true);
            }
        }

        [Fact]
        public void Refuses_a_name_that_resolves_to_nothing()
        {
            Assert.Throws<InvalidOperationException>(() => Vectors.Load("Errors/no-such-vector"));
        }

        [Fact]
        public void Names_the_file_it_read_so_a_failure_says_which_it_was()
        {
            Vector vector = Vectors.Load("Errors/400-field-validation");

            Assert.Equal("400-field-validation.recorded.json", vector.FileName);
            Assert.Equal(VectorProvenance.Recorded, vector.Provenance);
        }
    }
}
