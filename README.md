# NetSim for Windows

Portable Windows desktop app for simulating poor network connectivity through a TCP or UDP forwarding proxy.

## Download and run

Download **NetSim-Windows-1.0.1.zip** from [Releases](https://github.com/pfish3r/netsim-windows/releases/latest), extract it, then open **NetSim.exe**. No installation or administrator rights are required. Windows with .NET Framework 4.5 or newer is required; 4.8 is recommended.

## Features

- TCP and UDP port forwarding to a chosen host and port.
- Independent delay, jitter, and loss settings in both directions.
- Fixed, uniform, or Gaussian delay distributions.
- Optional UDP ordering, live traffic statistics, and hex/text capture.
- Version 1.0.1 fixes the startup exception in the delay control and adds error logging.

**TCP loss is modelled as an extra retry delay per selected read chunk; it does not drop real TCP packets. UDP loss discards actual datagrams.** Only traffic directed through the proxy is affected.

## Quick start

1. Choose TCP or UDP.
2. Set a listening address and unused port, such as `127.0.0.1:8443`.
3. Enter the real destination host and port.
4. Configure delay, jitter, and loss, then click **Start**.
5. Point the client application at the listening endpoint.
6. Click **Stop** before changing settings.

See [README.txt](README.txt) for HTTPS hostname handling, capture format, simulation details, and limits.

## Build from source

Run `./Build.ps1` in PowerShell under your normal script policy. The script uses the .NET Framework C# compiler supplied with Windows and produces `NetSim.exe` in the repository directory.

- `App.cs`: Windows Forms interface and error logging.
- `Engine.cs`: TCP/UDP forwarding and simulation engine.
- `Tests.cs`: loopback integration test source; excluded from the application build.
- `Build.ps1`: application build script.
- `Launch.cmd`: double-click launcher after building.

The downloadable ZIP retains its original `Source/` and `Tests/` folder layout and includes its matching build script.

## Verification status

The source compiled successfully. After the startup fix, the application created and showed its main window, rendered successfully, and exited normally. The rendered layout was inspected.

Windows security blocked the compiled integration-test runner, so network integration tests **have not passed or been runtime-verified**. Delay, loss, ordering, and connection lifecycle remain unverified. This is an unsigned prototype. No security settings were changed as part of development.
