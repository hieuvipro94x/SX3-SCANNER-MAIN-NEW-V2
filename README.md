# SX3 Scanner

WPF / .NET Framework 4.8 desktop application, built for Windows x64.

## Build

Run `BUILD_FILE.cmd` from any directory. Install Visual Studio Build Tools with
.NET desktop build tools and the .NET Framework 4.8 targeting pack first.
The launcher restores NuGet packages, rebuilds Release x64, runs the console
tests, and creates a versioned directory and ZIP under `artifacts/V<version>/`.
Version comes from `Properties/AssemblyInfo.cs`. For unattended execution use
`BUILD_FILE.cmd -NoPause`.

This .NET Framework application ships with its DLLs, config and native SQLite
library; the ZIP is the single delivery archive. Keep its extracted files together.
The launcher fetches origin and compares all remote branches containing the SX3
project, choosing the highest AssemblyVersion, then newest commit date. It
fast-forwards before building when possible and reloads updated build scripts.
Uncommitted work is stashed and restored, including staged/new files. If restore
conflicts or local history diverges, it stops and retains the backup stash.
It does not reset or force-push. The current local branch name is retained.

BUILD requires confirmation. Successful builds are copied with SHA256 verification
to `\\192.168.10.150\public\NAM\SX3Scanner\V<version>\<build-folder>`.
The SX3 LAN folder is created if its parent is accessible. To change it, pass
`-LanRoot "\\server\share\SX3Scanner"`.
Commit and push require confirmations after build and LAN copy; cancelling keeps
the local build. Push targets the current branch of the SX3 origin repository.
After BUILD confirmation, each build increments the last version component:
`10.50.0 -> 10.50.1 -> 10.50.2`. AssemblyVersion, AssemblyFileVersion and
AssemblyInformationalVersion are updated together. Cancelling BUILD leaves the
version unchanged. A failed build retains its increment; retrying increments
again. ZIP, LAN folder and commit message use the new version. No automatic signing.
For a local build without Git/LAN/prompts, run
`powershell -NoProfile -ExecutionPolicy Bypass -File BuildTools\Build-Release.ps1`.

## Data

Data lives in `%LOCALAPPDATA%\SX3_SCANER`. Version 10.50.0 creates missing database
files on first run and preserves existing files. Builds do not bundle or
overwrite operational databases. Data backup/restore remains available.

Online software updates, version reporting and online announcements were removed.
