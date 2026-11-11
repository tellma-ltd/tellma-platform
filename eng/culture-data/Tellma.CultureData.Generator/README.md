# Tellma.CultureData.Generator

Writes `src/core/Tellma.Core/Localization/CultureData.txt`: the date and number formats of every
language and region pair in the language catalogue, which `Tellma.Core` embeds and applies to the
culture a request is negotiated to. Because the formats ship with the platform, server text
(messages, emails, exports) reads the same on every host, whatever ICU release that host runs.

The tool is a thin console wrapper. The reading, cleaning and writing live in `Tellma.Core`
(`CultureDataSource`, `CultureDataFile`), which the runtime and its tests share; the tool only
runs them on a chosen host and writes the file.

## When to run it

Rarely. Run it when:

- the language catalogue gains a language or a region (`LanguageCatalogData`),
- the platform moves its pin to a newer CLDR release, or
- a correction is added to or removed from `CultureDataSource.Corrections`.

Never run it on a development machine directly: the output is this host's ICU data, and the point
of the file is that it comes from one known release.

## How to run it

Run it inside the image whose ICU release the platform pins, from the repository root. The image
is named by its digest as well as its tag: the tag floats to newer builds, and the digest finds
the one build the data came from again. The repository is mounted read-only and copied inside the
container, so the build never writes `bin` or `obj` folders owned by another OS into the working
tree.

```bash
docker run --rm \
  -v "$PWD:/repo:ro" \
  -v "$PWD/src/core/Tellma.Core/Localization:/out" \
  mcr.microsoft.com/dotnet/sdk:10.0-resolute@sha256:2107242a0f56885d09f8a536ac300f6cb1a2b3f6d40605e5ff81bbd83ad91e3f \
  bash -c 'mkdir /work && cd /repo \
    && tar --exclude=bin --exclude=obj -cf - Directory.Build.props Directory.Build.targets Directory.Packages.props global.json .editorconfig src/core eng/culture-data \
       | tar -xf - -C /work \
    && cd /work \
    && dotnet run --project eng/culture-data/Tellma.CultureData.Generator -- /out/CultureData.txt "CLDR 48 (ICU 78.2), mcr.microsoft.com/dotnet/sdk:10.0-resolute@sha256:2107242a0f56885d09f8a536ac300f6cb1a2b3f6d40605e5ff81bbd83ad91e3f"'
```

In Git Bash on Windows, prefix the command with `MSYS_NO_PATHCONV=1` so the container paths are
passed through unchanged. In PowerShell, replace `$PWD` with `${PWD}`.

The two arguments are the output path and a description of the source, written into the file's
header for a reader: the CLDR and ICU releases, and the image with its digest. The tool also
records the host's ICU fingerprint in the header.

When the pin moves to another image, take its digest from
`docker image inspect --format '{{index .RepoDigests 0}}' <image>` and change it in both places of
the command above.

## After running it

1. Read the diff. Every changed line is a change in what users see; a newer CLDR release can
   regress a format (CLDR 48 turned Oromo's and Tigrinya's numeric date month-first), and such a
   change belongs in `CultureDataSource.Corrections`, not in the file.
2. Never edit the file by hand. `CultureDataFileTests` regenerates the data on a host whose ICU
   fingerprint matches the header and fails when the file differs, so a hand edit does not survive
   the Linux test run on the pinned image. On any other host that test skips, unless the
   environment variable `TELLMA_REQUIRE_PINNED_ICU` is `1`, which turns the skip into a failure.
   The Linux leg of the pull-request build sets it, because its runner carries the pinned ICU
   release; if a new pin's release differs from that runner's, run that leg in the pinned image
   instead of dropping the variable.
3. Update the expected strings of `NegotiatedCulturesTests` and `CalendarGoldenTests` that the new
   data changes, checking each by hand.
