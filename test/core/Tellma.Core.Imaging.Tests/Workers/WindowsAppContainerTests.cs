// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

using System.Runtime.Versioning;
using System.Security.AccessControl;
using System.Security.Principal;
using Tellma.Core.Imaging.Workers;

namespace Tellma.Core.Imaging.Tests.Workers
{
    /// <summary>
    ///     What a Windows worker's AppContainer may read, and what the host does where it may not:
    ///     the access-list forms that let every AppContainer or the workers' capability read a
    ///     directory and what lies beneath it, and those that do not; a file that already grants
    ///     every AppContainer read left alone; a directory every AppContainer reads told apart; and
    ///     a grant the host's account may not make, on the .NET installation or on the host's own
    ///     files, answered with why its workers cannot be confined, never with a failure.
    /// </summary>
    [SupportedOSPlatform("windows")]
    public sealed class WindowsAppContainerTests
    {
        // FILE_GENERIC_READ | FILE_GENERIC_EXECUTE, as an access list holds read and execute.
        private const string ReadAndExecute = "0x1200a9";

        public static TheoryData<string, bool, bool> AccessLists => new()
        {
            // Program Files: read and execute on the folder itself, and generic read and execute
            // inherited by what lies beneath only, both inherited from above.
            { $"D:(A;ID;{ReadAndExecute};;;AC)(A;OICIIOID;GRGX;;;AC)", true, true },

            // One entry for the folder and what lies beneath it, generic or specific.
            { $"D:(A;OICI;{ReadAndExecute};;;AC)", true, true },
            { "D:(A;OICI;GA;;;AC)", true, true },
            { $"D:(A;OICI;{ReadAndExecute};;;{{capability}})", true, true },

            // The folder alone: a file reads, a directory's contents do not.
            { $"D:(A;;{ReadAndExecute};;;AC)", false, true },

            // What lies beneath alone, not the folder itself.
            { "D:(A;OICIIO;GRGX;;;AC)", false, false },

            // Not everything beneath: children only, files only, folders only.
            { $"D:(A;OICINP;{ReadAndExecute};;;AC)", false, true },
            { $"D:(A;CI;{ReadAndExecute};;;AC)", false, true },
            { $"D:(A;OI;{ReadAndExecute};;;AC)", false, true },

            // Read without execute, a denial, and an entry for someone else.
            { "D:(A;OICI;GR;;;AC)", false, false },
            { "D:(A;OICI;FR;;;AC)", false, false },
            { $"D:(D;OICI;{ReadAndExecute};;;AC)", false, false },
            { $"D:(A;OICI;{ReadAndExecute};;;BU)", false, false },
        };

        [Theory]
        [MemberData(nameof(AccessLists))]
        public void Only_an_access_list_that_lets_every_appcontainer_or_the_capability_read_and_execute_counts(string sddl, bool directory, bool file)
        {
            Assert.SkipUnless(OperatingSystem.IsWindows(), "The AppContainer is Windows's.");

            // Section: the same access list, as a directory's and as a file's.
            string list = sddl.Replace("{capability}", WindowsAppContainer.CapabilitySid.Value, StringComparison.Ordinal);
            var folder = new DirectorySecurity();
            folder.SetSecurityDescriptorSddlForm(list, AccessControlSections.Access);
            var single = new FileSecurity();
            single.SetSecurityDescriptorSddlForm(list, AccessControlSections.Access);

            Assert.Equal(directory, WindowsAppContainer.Grants(folder, directory: true));
            Assert.Equal(file, WindowsAppContainer.Grants(single, directory: false));
        }

        [Fact]
        public void A_file_that_already_lets_every_appcontainer_read_it_is_left_alone()
        {
            Assert.SkipUnless(OperatingSystem.IsWindows(), "The AppContainer is Windows's.");
            string path = Path.GetTempFileName();
            try
            {
                // Section: a file that grants every AppContainer read, as one under Program Files
                // does, gets no entry for the capability.
                var info = new FileInfo(path);
                FileSecurity security = info.GetAccessControl(AccessControlSections.Access);
                security.AddAccessRule(new FileSystemAccessRule(new SecurityIdentifier("S-1-15-2-1"), FileSystemRights.ReadAndExecute, AccessControlType.Allow));
                info.SetAccessControl(security);

                WindowsAppContainer.GrantReadAndExecute(path, directory: false);

                Assert.DoesNotContain(
                    info.GetAccessControl(AccessControlSections.Access).GetAccessRules(includeExplicit: true, includeInherited: true, typeof(SecurityIdentifier)).OfType<FileSystemAccessRule>(),
                    static rule => WindowsAppContainer.CapabilitySid.Equals(rule.IdentityReference));
            }
            finally
            {
                File.Delete(path);
            }
        }

        [Fact]
        public void A_directory_every_appcontainer_reads_in_full_is_told_apart()
        {
            Assert.SkipUnless(OperatingSystem.IsWindows(), "The AppContainer is Windows's.");
            DirectoryInfo folder = Directory.CreateTempSubdirectory("tellma-appcontainer-");
            try
            {
                // Section: a temporary directory grants no AppContainer anything; one that lets
                // every AppContainer read what lies beneath it, as Program Files does, reads in full.
                Assert.False(WindowsAppContainer.ReadsInFull(folder.FullName));
                DirectorySecurity security = folder.GetAccessControl(AccessControlSections.Access);
                security.AddAccessRule(new FileSystemAccessRule(
                    new SecurityIdentifier("S-1-15-2-1"), FileSystemRights.ReadAndExecute, InheritanceFlags.ContainerInherit | InheritanceFlags.ObjectInherit, PropagationFlags.None, AccessControlType.Allow));
                folder.SetAccessControl(security);
                Assert.True(WindowsAppContainer.ReadsInFull(folder.FullName));

                // Section: one that is not there reads nothing.
                Assert.False(WindowsAppContainer.ReadsInFull(Path.Combine(folder.FullName, "missing")));
            }
            finally
            {
                folder.Delete(recursive: true);
            }
        }

        [Fact]
        public void A_grant_the_account_may_not_make_on_the_installation_says_why_the_workers_cannot_be_confined()
        {
            Assert.SkipUnless(OperatingSystem.IsWindows(), "The AppContainer is Windows's.");
            string root = new PdfWorkerLauncher().Files.Root;

            // Section: the first grant, the installation's muxer, is refused: the answer names the
            // installation and the refusal.
            string? blocked = PdfWorkerLauncher.WindowsBlocker(static (_, _) => throw new UnauthorizedAccessException("Access is denied."));
            Assert.NotNull(blocked);
            Assert.Contains($"cannot read the .NET installation at {root}", blocked, StringComparison.Ordinal);
            Assert.Contains("Access is denied.", blocked, StringComparison.Ordinal);
        }

        [Fact]
        public void A_grant_the_account_may_not_make_on_the_hosts_own_files_says_why_the_workers_cannot_be_confined()
        {
            Assert.SkipUnless(OperatingSystem.IsWindows(), "The AppContainer is Windows's.");
            string root = new PdfWorkerLauncher().Files.Root;

            // Section: the installation is granted, the host's own files are not, as a build made by
            // another account is not: the answer names the first such file.
            string? blocked = PdfWorkerLauncher.WindowsBlocker((path, _) =>
            {
                if (!path.StartsWith(root, StringComparison.OrdinalIgnoreCase))
                {
                    throw new UnauthorizedAccessException("Access is denied.");
                }
            });
            Assert.NotNull(blocked);
            Assert.Contains("cannot read the host's file ", blocked, StringComparison.Ordinal);
            Assert.DoesNotContain(root, blocked, StringComparison.OrdinalIgnoreCase);
        }

        [Theory]
        [InlineData("io")]
        [InlineData("privilege")]
        [InlineData("operation")]
        [InlineData("support")]
        public void Every_failure_an_access_list_answers_with_is_a_reason_not_an_exception(string kind)
        {
            Assert.SkipUnless(OperatingSystem.IsWindows(), "The AppContainer is Windows's.");
            Exception failure = kind switch
            {
                "io" => new IOException("The device is not ready."),
                "privilege" => new PrivilegeNotHeldException("SeSecurityPrivilege"),
                "operation" => new InvalidOperationException("The method failed."),
                _ => new NotSupportedException("This volume holds no access lists."),
            };

            string? blocked = PdfWorkerLauncher.WindowsBlocker((_, _) => throw failure);
            Assert.NotNull(blocked);
            Assert.Contains(failure.Message, blocked, StringComparison.Ordinal);
        }

        [Fact]
        public void Where_every_grant_is_made_nothing_stands_in_the_way()
        {
            Assert.SkipUnless(OperatingSystem.IsWindows(), "The AppContainer is Windows's.");
            Assert.Null(PdfWorkerLauncher.WindowsBlocker(static (_, _) => { }));
        }
    }
}
