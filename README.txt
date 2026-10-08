NETSIM 1.0.1 — NETWORK CONDITION SIMULATOR

STARTUP FIX
Fixed an ArgumentOutOfRangeException when creating the delay control: the
maximum is now set before assigning its initial value of 125 ms.
Unexpected errors now write a diagnostic log under %LOCALAPPDATA%\NetSim\Logs.

A portable Windows desktop proxy inspired by the supplied TMnetsim screenshot.
Requires Windows with .NET Framework 4.5 or newer (4.8 recommended).
No installer, third-party libraries, network driver, or administrator access.

QUICK START
1. Extract the complete ZIP into a folder. Double-click NetSim.exe or Launch.cmd.
2. Choose TCP or UDP. Leave Listen IP at 127.0.0.1 for local applications.
3. Enter an unused listening port, then the real target host and port.
4. Set delay, jitter, and loss separately for each direction, then click Start.
5. Configure your client to connect to the listening IP and port.
6. Click Stop before changing settings. Closing the app also stops forwarding.

Example matching the screenshot:
  Protocol: TCP
  Listen IP: 127.0.0.1     Listening port: 8443
  Target host: 172.16.0.35  Target port: 443
  Client -> Target: Gaussian, delay 125 ms, jitter 4 ms, loss 0%
  Target -> Client: Gaussian, delay 125 ms, jitter 5 ms, loss 0%
The target must exist on your network. The app defaults to localhost rather than
assuming that the screenshot's private address is reachable.

HOW TO ROUTE TRAFFIC
This is a port-forwarding proxy. It affects only clients pointed at its listener;
it does not intercept the whole computer's traffic or change firewall settings.
TCP and UDP runs are separate. You can open another instance on a different
listening port for another destination or protocol.

For HTTPS, the original hostname must still be used for TLS SNI and certificate
verification. Opening https://localhost:8443 can cause a certificate-name error.
For a command-line example, substitute your real hostname:
  curl --connect-to example.com:443:127.0.0.1:8443 https://example.com/
Set the app's target to example.com:443. NetSim relays TLS bytes without decrypting
them and does not act as an HTTP CONNECT proxy. Apps without an endpoint-routing
option may require external routing or DNS configuration.

LISTENING ADDRESS
127.0.0.1 accepts connections only from this computer. A specific LAN IP, or
0.0.0.0 for all IPv4 interfaces, allows other devices to use the proxy if permitted
by Windows Firewall. NetSim creates no firewall rules. For IPv6, use ::1 or :: and
an IPv6-resolvable destination. Keep the target separate from the listener.

SIMULATION MODEL
Fixed: base delay, ignoring jitter.
Uniform: base delay plus a random value from -jitter to +jitter.
Gaussian: base delay plus normally distributed jitter; jitter is the standard
deviation. Negative sampled delay becomes zero; sampled delay is capped at 120 s.
Loss is an independent probability for each received unit in each direction.

TCP
- Units are socket read chunks (up to 16 KiB), NOT IP packets.
- Selected loss events add one configured TCP retry delay. Bytes are preserved.
- 100% loss means every chunk gets that extra delay; it is not a TCP outage.
- TCP always preserves byte order and supports half-close.
- Reads, delays, and writes are sequential in each direction. This introduces
  backpressure and can limit throughput. It is a practical application-level
  impairment model, not a recreation of TCP retransmission or congestion control.
- Genuine TCP packet loss/reordering needs an OS-level packet tool or driver.

UDP
- Units are complete datagrams. Loss really discards the selected datagrams.
- Each client endpoint gets its own upstream socket, supporting multiple clients.
- Preserve UDP order waits for earlier datagrams in the same direction. Turning
  it off lets variable delay reorder datagrams.
- Idle client mappings expire after two minutes with no pending datagrams.
- UDP source ports change through the proxy. Broadcast/multicast discovery and
  protocols with embedded endpoint addresses are outside this app's scope.

STATISTICS AND LIMITS
Arrows indicate client-to-target (up) and target-to-client (down).
Received/Sent are payload bytes, Units are received chunks or datagrams, and
Loss counts simulated TCP retry events or dropped UDP datagrams.
Overflow counts datagrams discarded because the per-client queue is full.
The app limits active clients to 128 and queued UDP datagrams to 256 per client.
Recent closed rows are pruned after 500 rows; totals cover retained rows only.
Statistics refresh every 0.5 seconds. Each Start begins a fresh run.
There is no bandwidth throttle setting; the current rate is measured throughput.

CAPTURE
Enable Capture traffic before Start and choose a NEW .tsv filename.
Existing files are never overwritten. Format is hex or escaped UTF-8 text.
Each record includes UTC time, connection, direction, action, byte count, and up
to 4 KiB of payload. Capture stops at roughly 25 MB; forwarding continues.
TCP records forwarded chunks; UDP records forwarding, simulated drops, and queue
overflow. These TSV files are not PCAP files. Captures can contain application
data; encrypted TLS remains encrypted. Text mode may replace non-UTF-8 bytes.

BUILD AND SOURCE
Engine.cs contains the TCP/UDP forwarding engine.
App.cs contains the Windows Forms interface.
Build.ps1 rebuilds NetSim.exe using the Windows .NET Framework C# compiler.
Run the script according to your normal PowerShell script policy. No policy
changes or security exclusions are needed by the app or build script.
Tests.cs contains the local loopback integration suite.

VERIFICATION — 24 SEPTEMBER 2026
The application and integration-test source compiled successfully.
The earlier process-only smoke check did not detect a startup exception. For
1.0.1, the exception was reproduced and its exact stack trace recorded. After
the fix, the actual application created and showed its main window, rendered a
window image, and exited normally. The rendered layout was visually inspected.
Windows security blocked and removed the compiled integration-test runner as
potentially unwanted software. Network integration tests therefore DID NOT RUN;
delay, loss, ordering, and connection lifecycle are not runtime-verified.
The block was not bypassed, and no security settings were changed. This package
is an unsigned prototype, not a signed or production-validated product.

TROUBLESHOOTING
Cannot start: check the event message, listening IP, and whether the port is in
use. Connection errors: confirm the target is running and reachable. No traffic:
make sure the client uses NetSim's listening endpoint and the correct protocol.
If Windows security blocks a file, review the detection under your organization's
normal security process; do not disable protection to run it.

