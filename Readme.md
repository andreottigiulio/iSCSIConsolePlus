iSCSIConsolePlus (unofficial fork)
==================================

This is a personal, unofficial fork of
[Tal Aloni's iSCSI Console](https://github.com/TalAloni/iSCSIConsole), built on
his iSCSI library. It is not affiliated with, endorsed by, or supported by the
original author.

It adds support for one specific loopback scenario: a VHD stored on an SMB share
of a NAS without iSCSI support, exported on 127.0.0.1 and consumed by Windows'
own initiator on the same PC. The additions are:

* Persistent settings (portable `ISCSIConsole.settings.ini`, fallback to
  `%APPDATA%\iSCSIConsole`) and a command line (`/vhd`, `/iqn`, `/listen`,
  `/portal`, `/port`, `/setup`, `/save`, `/headless ...`).
* Two tabs, Target (Server) and Windows Initiator (Client), with two independent
  auto-starts: a Startup shortcut for the target and a highest-privilege logon
  task for the initiator.
* A non-blocking target restore at startup: the window is usable immediately,
  the status bar reports "Connecting to target ...", and if the VHD is not
  available yet the app retries every N seconds (persisted as `RestoreRetry`
  and `RestoreRetrySeconds`, configurable in the Target tab).
* A short-lived elevated helper for MSiSCSI and disk operations, while the
  process serving the VHD stays non-elevated (an elevated token cannot see the
  user's SMB session).
* Connect, mount, disconnect/unmount with size-matched disk selection, and a
  button that opens `iscsicpl.exe`.
* A notification-area icon with an optional "minimize to tray on close" mode
  (persisted as `MinimizeToTray`); the tray menu can reopen or close the app.
* A "Dynamically expanding" checkbox in Create Virtual Disk.

Building from source
--------------------
The repository ships a build script that downloads its own toolchain, so no
Visual Studio, no .NET SDK installation and no changes to `PATH` are needed.

### Requirements

* Windows 10 or 11, x64.
* Windows PowerShell 5.1 (installed by default).
* An internet connection on the first run: the script downloads the .NET SDK
  and ILMerge, then restores the NuGet packages.
* About 1 GB of free space inside the repository for `.tools\`.

### Quick start

1. Get the sources, either with `git clone` or "Code → Download ZIP" on GitHub.
2. Open a command prompt in the repository root and run:

   ```
   build.cmd
   ```

   From PowerShell you can call the script directly:

   ```
   powershell -NoProfile -File build.ps1
   ```

3. When it finishes, the results are in `dist\`:

   | File | Description |
   | --- | --- |
   | `dist\ISCSIConsole.exe` | Single merged executable, no DLLs needed next to it. |
   | `dist\ISCSIConsole_<version>_<tfm>.zip` | Distributable archive with a blank `ISCSIConsole.settings.ini`. |

   The script prints the SHA256 of the archive at the end.

The first run takes a few minutes because of the downloads; later runs reuse
`.tools\` and only compile.

### What the script does

1. Resolves the latest .NET SDK of the 8.0 channel from the official release
   metadata, downloads it and verifies its SHA-512.
2. Downloads the ILMerge NuGet package (3.0.41 by default).
3. Restores the NuGet packages into `.tools\nuget-packages` with the
   repository's `NuGet.config`.
4. Builds `ISCSIConsole\ISCSIConsole.csproj` for `net472` (Release).
5. Merges `ISCSIConsole.exe` with `ISCSI.dll`, `Utilities.dll`,
   `DiskAccessLibrary.dll` and `DiskAccessLibrary.Win32.dll` via ILMerge.
6. Unless `-NoPackage` is used, stages the merged exe plus the blank settings
   file and creates the zip.

Everything the script downloads lives in `.tools\`, which is ignored by git;
deleting that folder removes the toolchain completely.

### Options

| Option | Default | Purpose |
| --- | --- | --- |
| `-TargetFramework` | `net472` | Target framework to build. `net20`, `net40` and `net472` are accepted; only `net472` is tested. |
| `-Configuration` | `Release` | Build configuration. |
| `-Clean` | off | Cleans previous build outputs and `dist\` before building. |
| `-NoPackage` | off | Skips the zip, only produces `dist\ISCSIConsole.exe`. |
| `-SdkVersion` | latest of the channel | Pins an exact .NET SDK version, e.g. `-SdkVersion 8.0.427`. |
| `-SdkChannel` | `8.0` | Release channel used to resolve the SDK. |
| `-IlMergeVersion` | `3.0.41` | ILMerge version downloaded from NuGet. |

Examples:

```
build.cmd -Clean
build.cmd -NoPackage
build.cmd -TargetFramework net472 -SdkVersion 8.0.427
```

### Troubleshooting

* **Script blocked by the execution policy**: the default policy of some
  machines refuses local scripts. Use the `build.cmd` wrapper, or allow local
  scripts for your user with
  `Set-ExecutionPolicy -Scope CurrentUser RemoteSigned` (no administrator
  rights required).
* **NuGet restore errors**: the build needs `nuget.org`; the repository's
  `NuGet.config` clears inherited sources on purpose, so a custom global
  configuration cannot break the restore.
* **Corrupted toolchain**: delete `.tools\` and run the script again; the
  downloads are verified and repeated from scratch.
* **The script fails extracting the SDK**: it uses the `tar.exe` shipped with
  Windows (`%SystemRoot%\System32\tar.exe`). If that file is missing, it falls
  back to PowerShell's `Expand-Archive`.
* **Build fails only for `net20`/`net40`**: those targets are inherited from
  upstream and are not tested here; use `net472`.

Requirements: Windows, .NET Framework 4.7.2 and PowerShell 5.1 with the iSCSI
and Storage modules. Only the net472 target is built and tested here. The
implementation was made with AI assistance; design and testing were done by the
repository owner.

License: LGPL v3, same as the original project (see `License.txt`). Original
copyright notices are kept. If you redistribute binaries, provide the
corresponding source as the license requires.

The original project remains the reference for the target-only demo; this fork
exists because these additions were out of scope there.

About iSCSI Console:
====================
iSCSI Console is a Free, Open Source, User-Mode iSCSI Target Server written in C#.  
iSCSI Console is cross-platform, portable and requires no installation.  
iSCSI Console can serve physical and virtual disks to multiple clients.  

About the iSCSI library:
========================
The iSCSI library utilized by iSCSI Console was designed to give developers an easy way to serve block storage via iSCSI.  
Any storage object you wish to share needs to implement the abstract Disk class, and the library will take care of the rest.  
The library was written with extensibility in mind, and was designed to fit multitude of projects.  
iSCSI Console is merely a demo project that exposes some of the capabilities of this library.  

A NuGet package of the library [is available](https://www.nuget.org/packages/ISCSI/).

Notes:
------
In addition to a full fledged iSCSI Target server implementation, the iSCSI library also contain a very basic iSCSI initiator implementation.

What this program can do:
===================================
1. Serve virtual disks (VHD / VMDK / IMG).
2. Serve physical disks.
3. Serve basic volumes as disks.
4. Serve dynamic volumes as disks.
5. Create VHDs.
6. Can run under Windows PE using Mono.
7. Can run under Linux / OSX using Mono (use the release targeting .NET Framework 4.7.2)

![iSCSI Console UI](ISCSIConsole_UI.png)

Contact:
========
If you have any question, feel free to contact me.  
Tal Aloni <tal.aloni.il@gmail.com>
