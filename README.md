# BorisRay for Windows

Personal Windows 11 x64 client for BorisHub, using native Windows Forms and the official unmodified sing-box 1.14.3 core. Independent client; no affiliation with SagerNet or Amnezia. No subscription or server administrator credential is included.

Features: VLESS Reality/TCP and Hysteria2/UDP, explicit country profiles, process executable/folder rules, domain rules with matching DNS policy, local connection inspection, bounded HTTPS throughput/latency probe, and manual BorisHub update checks. Subscriptions use Windows CurrentUser DPAPI. The system tunnel needs elevation; this version requests UAC at launch.

See [Russian installation instructions](README.txt). This initial release still requires a real Windows 11 network acceptance test; native CI rendering and `sing-box check` do not prove successful TUN connectivity.

## Build and checks

On Windows x64 with .NET SDK 10.0.402 and PowerShell 7:

```powershell
./Scripts/build.ps1 -Capture
```

The script downloads the official core release with pinned archive, executable and DLL SHA256 checks. It runs the portable parser/routing tests, validates four synthetic configurations with the Windows core, publishes a self-contained client and captures the actual WinForms controls at desktop/compact sizes. The isolated capture executable cannot start a VPN. Artifacts contain only synthetic `.example` data and expire after three days in Actions.

Portable tests can also run on macOS/Linux with `dotnet run --project tests/BorisRay.Tests.csproj -c Release -- config-checks`.

The core's full corresponding source at tag v1.14.3, commit `7054cac5124465ffdb145870d7c01885566711b1`, is supplied in the release source ZIP. [Upstream source](https://github.com/SagerNet/sing-box/tree/v1.14.3), [official release](https://github.com/SagerNet/sing-box/releases/tag/v1.14.3). See [LICENSES.txt](LICENSES.txt) for dependencies and source availability.
