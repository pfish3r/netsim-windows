using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace NetSim {
 public sealed class Policy {
  public int Delay, Jitter, Retry = 200;
  public double Loss;
  public string Distribution = "Gaussian";
 }
 public sealed class Settings {
  public string Bind = "127.0.0.1", Host = "127.0.0.1", Protocol = "TCP", CapturePath;
  public int ListenPort = 8443, TargetPort = 443;
  public bool PreserveOrder = true, CaptureHex = true;
  public Policy Up = new Policy(), Down = new Policy();
 }
 public sealed class Flow {
  public int Id; public string Client, State = "Connected";
  public long UpRx, UpTx, DownRx, DownTx, UpUnits, DownUnits, UpLoss, DownLoss, Overflow;
  internal TcpClient Tcp; internal UdpClient Udp;
  internal readonly object Gate = new object();
  internal Task UpTail = Task.FromResult(0), DownTail = Task.FromResult(0);
  internal int Pending;
  internal long LastActivity = DateTime.UtcNow.Ticks;
 }
 public sealed class Engine : IDisposable {
  public readonly ConcurrentDictionary<int, Flow> Flows = new ConcurrentDictionary<int, Flow>();
  public readonly ConcurrentQueue<string> Events = new ConcurrentQueue<string>();
  readonly ConcurrentDictionary<string, Flow> udpFlows = new ConcurrentDictionary<string, Flow>();
  readonly ConcurrentDictionary<int, Task> tasks = new ConcurrentDictionary<int, Task>();
  readonly CancellationTokenSource stop = new CancellationTokenSource();
  readonly Random random = new Random(); readonly object rngLock = new object(), captureLock = new object();
  readonly SemaphoreSlim sendLock = new SemaphoreSlim(1, 1);
  TcpListener tcp; UdpClient udp; IPEndPoint destination; StreamWriter capture;
  int nextId, nextTask; long captured; bool captureLimit;
  public Settings Config { get; private set; }
  public bool Running { get; private set; }
  public Engine(Settings settings) { Config = settings; }
  public async Task StartAsync() {
   Validate(Config);
   IPAddress bind = IPAddress.Parse(Config.Bind);
   IPAddress[] addresses = await Dns.GetHostAddressesAsync(Config.Host);
   IPAddress target = Array.Find(addresses, a => a.AddressFamily == bind.AddressFamily);
   if (target == null) throw new ArgumentException("Target and listening address must use the same IP version.");
   destination = new IPEndPoint(target, Config.TargetPort);
   if (Config.ListenPort == Config.TargetPort && (target.Equals(bind) || (IPAddress.IsLoopback(target) && (IPAddress.IsLoopback(bind) || bind.Equals(IPAddress.Any) || bind.Equals(IPAddress.IPv6Any)))))
    throw new ArgumentException("The target points back to the listening port. Choose another port.");
   try {
    if (!String.IsNullOrEmpty(Config.CapturePath)) {
     capture = new StreamWriter(new FileStream(Config.CapturePath, FileMode.CreateNew, FileAccess.Write, FileShare.Read), Encoding.UTF8);
     capture.WriteLine("UTC\tConnection\tDirection\tAction\tBytes\tPayload (" + (Config.CaptureHex ? "hex" : "UTF-8 escaped") + ")"); capture.Flush();
    }
    if (Config.Protocol == "TCP") { tcp = new TcpListener(bind, Config.ListenPort); tcp.Start(128); Running = true; Track(AcceptLoop()); }
    else { udp = new UdpClient(new IPEndPoint(bind, Config.ListenPort)); Running = true; Track(UdpLoop()); Track(SweepLoop()); }
    Log("Listening on " + Config.Bind + ":" + Config.ListenPort + " → " + destination + " (" + Config.Protocol + ")");
   } catch { Dispose(); throw; }
  }
  public static void Validate(Settings s) {
   IPAddress ip;
   if (!IPAddress.TryParse(s.Bind, out ip)) throw new ArgumentException("Enter a numeric listening IP address, such as 127.0.0.1.");
   if (String.IsNullOrWhiteSpace(s.Host)) throw new ArgumentException("Enter a target host or IP address.");
   if (s.ListenPort < 1 || s.ListenPort > 65535 || s.TargetPort < 1 || s.TargetPort > 65535) throw new ArgumentException("Ports must be between 1 and 65535.");
   if (s.Protocol != "TCP" && s.Protocol != "UDP") throw new ArgumentException("Choose TCP or UDP.");
   foreach (Policy p in new[] { s.Up, s.Down }) {
    if (p == null || p.Delay < 0 || p.Delay > 60000 || p.Jitter < 0 || p.Jitter > 60000 || Double.IsNaN(p.Loss) || p.Loss < 0 || p.Loss > 100 || p.Retry < 1 || p.Retry > 60000) throw new ArgumentException("Delay/jitter: 0–60,000 ms; loss: 0–100%; retry: 1–60,000 ms.");
    if (p.Distribution != "Gaussian" && p.Distribution != "Uniform" && p.Distribution != "Fixed") throw new ArgumentException("Unknown delay distribution.");
   }
  }
  void Log(string message) { Events.Enqueue(DateTime.Now.ToString("HH:mm:ss") + "  " + message); while (Events.Count > 500) { string ignored; Events.TryDequeue(out ignored); } }
  void Track(Task task) { int id = Interlocked.Increment(ref nextTask); tasks[id] = task; task.ContinueWith(t => { Task ignored; tasks.TryRemove(id, out ignored); if (t.IsFaulted) Log("Operation failed: " + t.Exception.GetBaseException().Message); }, TaskScheduler.Default); }
  int DelayFor(Policy p, out bool loss) {
   lock (rngLock) {
    loss = random.NextDouble() * 100 < p.Loss;
    double d = p.Delay;
    if (p.Distribution == "Uniform") d += (random.NextDouble() * 2 - 1) * p.Jitter;
    if (p.Distribution == "Gaussian") d += Math.Sqrt(-2 * Math.Log(1 - random.NextDouble())) * Math.Cos(2 * Math.PI * random.NextDouble()) * p.Jitter;
    return (int)Math.Min(120000, Math.Max(0, d));
   }
  }
  void Received(Flow f, bool up, int length) { Interlocked.Exchange(ref f.LastActivity, DateTime.UtcNow.Ticks); if (up) { Interlocked.Add(ref f.UpRx, length); Interlocked.Increment(ref f.UpUnits); } else { Interlocked.Add(ref f.DownRx, length); Interlocked.Increment(ref f.DownUnits); } }
  void Sent(Flow f, bool up, int length) { if (up) Interlocked.Add(ref f.UpTx, length); else Interlocked.Add(ref f.DownTx, length); }
  void Lost(Flow f, bool up) { if (up) Interlocked.Increment(ref f.UpLoss); else Interlocked.Increment(ref f.DownLoss); }
  void Capture(Flow f, bool up, string action, byte[] bytes, int count) {
   lock (captureLock) {
    if (capture == null || captureLimit) return;
    try {
     // Limit disk growth; truncate individual payloads to keep the UI responsive.
     if (captured >= 25 * 1024 * 1024) { captureLimit = true; Log("Capture reached 25 MB and has stopped. Forwarding continues."); return; }
     int size = Math.Min(count, 4096);
     string payload = Config.CaptureHex ? BitConverter.ToString(bytes, 0, size).Replace("-", " ") : Encoding.UTF8.GetString(bytes, 0, size).Replace("\\", "\\\\").Replace("\r", "\\r").Replace("\n", "\\n").Replace("\t", "\\t").Replace("\0", "\\0");
     string line = DateTime.UtcNow.ToString("o") + "\t" + f.Id + "\t" + (up ? "Client→Target" : "Target→Client") + "\t" + action + "\t" + count + "\t" + payload + (size < count ? " [truncated]" : "");
     capture.WriteLine(line); capture.Flush(); captured += Encoding.UTF8.GetByteCount(line) + 2;
    } catch (Exception ex) { captureLimit = true; Log("Capture stopped: " + ex.Message); }
   }
  }
  async Task AcceptLoop() {
   while (!stop.IsCancellationRequested) {
    try {
     TcpClient client = await tcp.AcceptTcpClientAsync();
     if (ActiveCount() >= 128) { client.Close(); Log("Connection limit reached (128)."); continue; }
     Flow f = new Flow { Id = Interlocked.Increment(ref nextId), Client = client.Client.RemoteEndPoint.ToString(), Tcp = client, State = "Connecting" };
     Flows[f.Id] = f; Prune(); Track(TcpSession(f));
    } catch (Exception ex) { if (!stop.IsCancellationRequested) Log("Accept failed: " + ex.Message); break; }
   }
  }
  int ActiveCount() { int count = 0; foreach (Flow f in Flows.Values) if (f.State == "Connected" || f.State == "Connecting") count++; return count; }
  void Prune() { if (Flows.Count <= 500) return; foreach (Flow f in Flows.Values) if (f.State != "Connected" && f.State != "Connecting") { Flow ignored; Flows.TryRemove(f.Id, out ignored); if (Flows.Count <= 400) break; } }
  async Task TcpSession(Flow f) {
   using (TcpClient remote = new TcpClient(destination.AddressFamily))
   using (stop.Token.Register(() => { f.Tcp.Close(); remote.Close(); })) {
    try {
     Task connecting = remote.ConnectAsync(destination.Address, destination.Port);
     if (await Task.WhenAny(connecting, Task.Delay(10000, stop.Token)) != connecting) { remote.Close(); try { await connecting; } catch {} throw new TimeoutException("Target connection timed out."); }
     await connecting; f.Tcp.NoDelay = true; remote.NoDelay = true; f.State = "Connected";
     Task a = TcpPump(f, f.Tcp, remote, true), b = TcpPump(f, remote, f.Tcp, false);
     // One failed direction must unblock the other. Clean EOF is a half-close.
     Task first = await Task.WhenAny(a, b);
     if (first.IsFaulted || first.IsCanceled) { f.Tcp.Close(); remote.Close(); }
     await Task.WhenAll(a, b); f.State = "Closed";
    } catch (Exception ex) { f.State = stop.IsCancellationRequested ? "Stopped" : "Error"; if (!stop.IsCancellationRequested) Log("Connection " + f.Id + ": " + ex.Message); }
    finally { f.Tcp.Close(); }
   }
  }
  async Task TcpPump(Flow f, TcpClient source, TcpClient target, bool up) {
   byte[] buffer = new byte[16384]; Policy policy = up ? Config.Up : Config.Down;
   NetworkStream input = source.GetStream(), output = target.GetStream();
   while (!stop.IsCancellationRequested) {
    int n = await input.ReadAsync(buffer, 0, buffer.Length, stop.Token);
    if (n == 0) { try { target.Client.Shutdown(SocketShutdown.Send); } catch (SocketException) {} return; }
    Received(f, up, n); bool loss; int delay = DelayFor(policy, out loss);
    if (loss) { Lost(f, up); delay += policy.Retry; }
    await Task.Delay(delay, stop.Token); await output.WriteAsync(buffer, 0, n, stop.Token);
    Sent(f, up, n); Capture(f, up, loss ? "retry-delay" : "forward", buffer, n);
   }
  }
  async Task UdpLoop() {
   while (!stop.IsCancellationRequested) {
    try {
     UdpReceiveResult packet = await udp.ReceiveAsync(); string key = packet.RemoteEndPoint.ToString(); Flow f;
     if (!udpFlows.TryGetValue(key, out f)) {
      if (udpFlows.Count >= 128) { Log("UDP client limit reached (128)."); continue; }
      UdpClient remote = new UdpClient(destination.AddressFamily); remote.Connect(destination);
      f = new Flow { Id = Interlocked.Increment(ref nextId), Client = key, Udp = remote };
      udpFlows[key] = f; Flows[f.Id] = f; Prune(); Track(UdpReplies(f, packet.RemoteEndPoint));
     }
     Schedule(f, true, packet.Buffer, packet.RemoteEndPoint);
    } catch (Exception ex) { if (!stop.IsCancellationRequested) Log("UDP listener failed: " + ex.Message); break; }
   }
  }
  async Task UdpReplies(Flow f, IPEndPoint client) {
   try { while (!stop.IsCancellationRequested) { UdpReceiveResult packet = await f.Udp.ReceiveAsync(); Schedule(f, false, packet.Buffer, client); } }
   catch (Exception ex) { if (!stop.IsCancellationRequested && f.State == "Connected") Log("UDP " + f.Id + ": " + ex.Message); }
   finally { f.State = stop.IsCancellationRequested ? "Stopped" : "Closed"; Flow ignored; udpFlows.TryRemove(f.Client, out ignored); f.Udp.Close(); }
  }
  void Schedule(Flow f, bool up, byte[] bytes, IPEndPoint client) {
   Received(f, up, bytes.Length);
   lock (f.Gate) {
    if (f.Pending >= 256) { Interlocked.Increment(ref f.Overflow); Capture(f, up, "queue-overflow", bytes, bytes.Length); return; }
    f.Pending++; bool loss; int delay = DelayFor(up ? Config.Up : Config.Down, out loss);
    Task previous = Config.PreserveOrder ? (up ? f.UpTail : f.DownTail) : Task.FromResult(0);
    // Start the delay now; ordered packets wait only for earlier due packets.
    Task due = Task.Delay(delay, stop.Token);
    Task next = ForwardDatagram(f, up, bytes, client, previous, due, loss);
    if (up) f.UpTail = next; else f.DownTail = next; Track(next);
   }
  }
  async Task ForwardDatagram(Flow f, bool up, byte[] bytes, IPEndPoint client, Task previous, Task due, bool loss) {
   try {
    await previous; await due; stop.Token.ThrowIfCancellationRequested();
    if (loss) { Lost(f, up); Capture(f, up, "drop", bytes, bytes.Length); return; }
    if (up) await f.Udp.SendAsync(bytes, bytes.Length);
    else { await sendLock.WaitAsync(stop.Token); try { await udp.SendAsync(bytes, bytes.Length, client); } finally { sendLock.Release(); } }
    Sent(f, up, bytes.Length); Capture(f, up, "forward", bytes, bytes.Length);
   } catch (Exception ex) { if (!stop.IsCancellationRequested && f.State == "Connected") Log("UDP send " + f.Id + ": " + ex.Message); }
   finally { lock (f.Gate) f.Pending--; }
  }
  async Task SweepLoop() {
   try { while (!stop.IsCancellationRequested) {
    await Task.Delay(10000, stop.Token);
    foreach (Flow f in udpFlows.Values) if (DateTime.UtcNow.Ticks - Interlocked.Read(ref f.LastActivity) > TimeSpan.FromMinutes(2).Ticks) {
     lock (f.Gate) { if (f.Pending != 0) continue; f.State = "Idle timeout"; f.Udp.Close(); }
    }
   } } catch (OperationCanceledException) {}
  }
  public async Task StopAsync() { Dispose(); Task[] pending = new List<Task>(tasks.Values).ToArray(); await Task.WhenAll(pending); }
  public void Dispose() {
   if (!stop.IsCancellationRequested) stop.Cancel(); Running = false;
   if (tcp != null) tcp.Stop(); if (udp != null) udp.Close();
   foreach (Flow f in Flows.Values) { if (f.Tcp != null) f.Tcp.Close(); if (f.Udp != null) f.Udp.Close(); if (f.State == "Connected" || f.State == "Connecting") f.State = "Stopped"; }
   lock (captureLock) { if (capture != null) { capture.Dispose(); capture = null; } }
  }
 }
}
