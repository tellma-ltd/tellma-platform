// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

using System.Runtime.Versioning;
using Tellma.Core.Excel;

namespace Tellma.Core.Tests.Excel
{
    /// <summary>
    ///     The spools' scratch files: gone once their stream is disposed, and on Unix readable and
    ///     writable by their owner alone and unlinked while still open.
    /// </summary>
    public sealed class ScratchFilesTests : IDisposable
    {
        /// <summary>The scratch directory, one per test.</summary>
        private readonly string _scratch = Directory.CreateDirectory(Path.Combine(Path.GetTempPath(), "tellma-excel-tests", Guid.NewGuid().ToString("N"))).FullName;

        [Fact]
        public void A_scratch_file_reads_back_what_it_was_given_and_is_gone_once_disposed()
        {
            string path;
            using (FileStream spool = ScratchFiles.Create(_scratch))
            {
                path = spool.Name;
                spool.Write([1, 2, 3]);
                spool.Position = 0;
                Assert.Equal(1, spool.ReadByte());

                // Windows keeps the name until the last handle closes, and shares it with no one meanwhile.
                if (OperatingSystem.IsWindows())
                {
                    Assert.True(File.Exists(path));
                    _ = Assert.Throws<IOException>(() => File.OpenRead(path));
                }
            }

            Assert.False(File.Exists(path));
            Assert.Empty(Directory.EnumerateFiles(_scratch));
        }

        [Fact]
        [SupportedOSPlatform("linux")]
        public void On_linux_a_scratch_file_is_its_owners_alone_and_unlinked_while_open()
        {
            Assert.SkipUnless(OperatingSystem.IsLinux(), "File modes and unlinking an open file are Unix behaviour.");
            using FileStream spool = ScratchFiles.Create(_scratch);

            Assert.Equal(UnixFileMode.UserRead | UnixFileMode.UserWrite, File.GetUnixFileMode(spool.SafeFileHandle));
            Assert.False(File.Exists(spool.Name));
            spool.Write([1, 2, 3]);
            spool.Position = 0;
            Assert.Equal(1, spool.ReadByte());
        }

        /// <inheritdoc />
        public void Dispose()
        {
            Directory.Delete(_scratch, recursive: true);
        }
    }
}
