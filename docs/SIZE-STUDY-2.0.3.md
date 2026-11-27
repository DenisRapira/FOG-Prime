# Package Size Experiment - 2026-11-27

Local candidate experiment, not a published release or a network benchmark.
Compare extracted directories, not an extracted FOG folder with another
product's ZIP/download. The user subsequently clarified that "Flowers" means
Flowseal/zapret-discord-youtube. Its official assets were inspected below;
equivalent network behavior, automation and acceptance are not asserted.

## Flowseal Comparison

Official GitHub release assets were fetched into memory, checked against the
GitHub API asset SHA-256, and inspected as ZIP entries. No archive was extracted
and no downloaded script or binary was executed. Expanded bytes are the sum
of file lengths in the ZIP, not NTFS allocation or a particular user's folder.

| Official asset | ZIP bytes | Expanded bytes | Files |
| --- | ---: | ---: | ---: |
| 1.10.4 stable | 1518649 | 4061394 | 59 |
| 2.0.0-rc prerelease | 1790668 | 4681243 | 69 |

That is about 1.45/3.87 MiB for stable ZIP/contents and 1.71/4.46 MiB for RC.
Both contain the Cygwin/WinDivert/native Engine binaries and batch scripts,
not two self-contained .NET GUI/Agent applications. The RC adds the newer
Engine and strategy files. Much of FOG's size difference is deployment/host
overhead, not additional packet-processing functionality. A local 50 MB folder
cannot be explained from the repository link alone.

Verified ZIP digests:

- 1.10.4: `291af673bd0238506f9548e0e94ca0abf244000f4c9a06960544fb01c5f97b34`
- 2.0.0-rc: `224718c9c1a6c4210f8f46f8d4d97a60d1fff31729ff30079aa779a6ea0385ff`

Sources: [stable release](https://github.com/Flowseal/zapret-discord-youtube/releases/tag/1.10.4),
[RC release](https://github.com/Flowseal/zapret-discord-youtube/releases/tag/2.0.0-rc).

## Result

| Measurement | Uncompressed icon candidate | Compressed candidate |
| --- | ---: | ---: |
| Entire package, bytes | 221218253 | 107781477 |
| Entire package, MiB | 210.97 | 102.79 |
| FOG Prime.exe, bytes | 141090412 | 65308925 |
| app/FOG.Agent.exe, bytes | 76076848 | 38421525 |
| Files | 33 | 33 |

The extracted package is about 51.3% smaller. The same managed sources,
native driver, Engine, profile rules, payload assets and notices were retained.
UI and Agent each contain a self-contained .NET runtime; moving files under
`app/` does not remove this duplication. No claim is made about ZIP sizes,
temporary extraction/cache sizes or total installed disk use after launch.

Method: SDK `EnableCompressionInSingleFile=true` for both published EXEs,
before normal signing/hash embedding. No UPX, encryption, obfuscation, native
driver alteration, Native AOT or unsupported WPF trimming was used.

## Important Tradeoff

One sequential sample per variant, five-second warm-up, ten-second idle
window, no connection autostart or native Engine. Windows caches/background
activity were not controlled. These measurements cover UI and Agent only,
**excluding WebView2 child processes**, so they are not total app memory.

| Sample | Uncompressed | Compressed |
| --- | ---: | ---: |
| Launcher to Agent process detection, ms | 2203.9 | 2304.4 |
| UI working set, MiB | 117.86 | 218.82 |
| Agent working set, MiB | 60.04 | 98.07 |
| UI private bytes, MiB | 69.24 | 132.28 |
| Agent private bytes, MiB | 22.00 | 45.86 |
| UI CPU during sample, ms | 93.75 | 62.50 |
| Agent CPU during sample, ms | 140.625 | 140.625 |

Startup timing uses a 250 ms process-detection poll, not first rendered frame,
authenticated IPC or successful service connection. The 100 ms difference is
not a demonstrated startup regression or improvement. CPU figures are one
short sample, not evidence of a general performance gain. Increased memory
is enough reason not to promote compression merely on its disk-size result.

## Decision and Checks

`build/package.ps1 -CompressBundles` is available for experiments but **off by
default**. The existing uncompressed icon candidate remains available; neither
was uploaded or promoted to stable. Keep version-specific comparison artifacts
separate and never overwrite previously accepted release assets.

The compressed candidate passed exact package layout, nine-size/EXE icon
checks, authenticated Agent lifecycle and modified-file rejection tests. Its
actual UI and nested Agent started in no-autostart mode and exited; no FOG
processes remained after checks. This does not verify live voice, games or
driver behavior. No VPN, DNS, routes, antivirus or certificate settings changed.

For a larger architectural improvement, compare removing the WPF host while
retaining WebView2/HTML against a shared private runtime. Either needs a
prototype, privilege/dependency-integrity design and complete regression
testing. Requiring a globally installed .NET runtime would reduce the package
but changes the current install-free user promise; it is not a free equivalent
optimization.

Primary documentation:
[Microsoft single-file compression](https://learn.microsoft.com/en-us/dotnet/core/deploying/single-file/overview#compress-assemblies-in-single-file-apps).
Microsoft explicitly recommends measuring both size and startup cost.
