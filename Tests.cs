using System;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Sockets;
using System.Diagnostics;
using System.Threading.Tasks;
using NetSim;
class Tests {
 static int count;
 static void Check(bool pass, string name) { if (!pass) throw new Exception("FAIL: " + name); Console.WriteLine("PASS: " + name); count++; }
 static int Port() { var s = new TcpListener(IPAddress.Loopback, 0); s.Start(); int p = ((IPEndPoint)s.LocalEndpoint).Port; s.Stop(); return p; }
 static Settings Config(int target, string protocol) { return new Settings { ListenPort = Port(), TargetPort = target, Protocol = protocol }; }
 static async Task Timeout(Task t) { if (await Task.WhenAny(t, Task.Delay(5000)) != t) throw new Exception("Timed out"); await t; }
 static async Task TcpTest(int delay, double loss) {
  var echo = new TcpListener(IPAddress.Loopback, 0); echo.Start(); int port = ((IPEndPoint)echo.LocalEndpoint).Port;
  var config = Config(port, "TCP"); config.Up.Delay = config.Down.Delay = delay; config.Up.Loss = config.Down.Loss = loss; config.Up.Retry = config.Down.Retry = 80;
  string path = Path.GetFullPath("work/capture-" + Guid.NewGuid() + ".tsv"); config.CapturePath = path;
  using (var engine = new Engine(config)) {
   await engine.StartAsync();
   Task server = Task.Run(async () => { using (var c = await echo.AcceptTcpClientAsync()) { var m = new MemoryStream(); await c.GetStream().CopyToAsync(m); byte[] data = m.ToArray(); await c.GetStream().WriteAsync(data, 0, data.Length); c.Client.Shutdown(SocketShutdown.Send); } });
   byte[] payload = Enumerable.Range(0, 4096).Select(i => (byte)(i % 251)).ToArray(); var sw = Stopwatch.StartNew();
   using (var client = new TcpClient()) { await client.ConnectAsync(IPAddress.Loopback, config.ListenPort); await client.GetStream().WriteAsync(payload, 0, payload.Length); client.Client.Shutdown(SocketShutdown.Send); var result = new MemoryStream(); await Timeout(client.GetStream().CopyToAsync(result)); Check(payload.SequenceEqual(result.ToArray()), "TCP binary integrity and half-close, delay=" + delay + ", loss=" + loss); }
   await Timeout(server); Check(sw.ElapsedMilliseconds >= delay * 2 + (loss == 100 ? 160 : 0) - 15, "TCP bidirectional delay/retry latency");
   await Timeout(engine.StopAsync()); Check(File.ReadAllText(path).Contains("Client→Target"), "Capture written");
   using (var again = new Engine(config = Config(port, "TCP"))) { await again.StartAsync(); await Timeout(again.StopAsync()); }
  }
  echo.Stop();
 }
 static async Task UdpTest(bool dropUp, bool dropDown) {
  using (var echo = new UdpClient(new IPEndPoint(IPAddress.Loopback, 0))) {
   var config = Config(((IPEndPoint)echo.Client.LocalEndPoint).Port, "UDP"); config.Up.Loss = dropUp ? 100 : 0; config.Down.Loss = dropDown ? 100 : 0; config.Up.Delay = config.Down.Delay = 40;
   using (var engine = new Engine(config)) using (var client = new UdpClient(new IPEndPoint(IPAddress.Loopback, 0))) {
    await engine.StartAsync(); client.Connect(IPAddress.Loopback, config.ListenPort); byte[] payload = { 0, 10, 255, 100 }; var sw = Stopwatch.StartNew(); await client.SendAsync(payload, payload.Length);
    if (!dropUp) { var got = await echo.ReceiveAsync(); Check(got.Buffer.SequenceEqual(payload), "UDP upstream intact"); await echo.SendAsync(got.Buffer, got.Buffer.Length, got.RemoteEndPoint); }
    Task<UdpReceiveResult> reply = client.ReceiveAsync(); Task done = await Task.WhenAny(reply, Task.Delay(250));
    if (dropUp || dropDown) Check(done != reply, "UDP 100% loss " + (dropUp ? "upstream" : "downstream"));
    else { Check(done == reply && reply.Result.Buffer.SequenceEqual(payload), "UDP round trip"); Check(sw.ElapsedMilliseconds >= 65, "UDP bidirectional delay"); }
    var flow = engine.Flows.Values.Single(); Check(dropUp ? flow.UpLoss == 1 : dropDown ? flow.DownLoss == 1 : flow.UpTx == 4 && flow.DownTx == 4, "UDP counters");
    await Timeout(engine.StopAsync());
    using (var again = new Engine(config)) { await again.StartAsync(); await Timeout(again.StopAsync()); Check(true, "UDP same-port restart"); }
   }
  }
 }
 static async Task OrderTest() {
  using (var target = new UdpClient(new IPEndPoint(IPAddress.Loopback, 0))) {
   var config = Config(((IPEndPoint)target.Client.LocalEndPoint).Port, "UDP"); config.Up.Delay = 70; config.Up.Jitter = 60; config.Up.Distribution = "Uniform";
   using (var engine = new Engine(config)) using (var client = new UdpClient()) {
    await engine.StartAsync(); client.Connect(IPAddress.Loopback, config.ListenPort);
    for (int i = 0; i < 20; i++) await client.SendAsync(new byte[] { (byte)i }, 1);
    for (int i = 0; i < 20; i++) { var packet = await target.ReceiveAsync(); if (packet.Buffer[0] != i) throw new Exception("UDP order broken"); }
    Check(true, "UDP order preserved with jitter"); await engine.StopAsync();
   }
  }
 }
 static async Task StopTest() {
  var target = new TcpListener(IPAddress.Loopback, 0); target.Start(); var config = Config(((IPEndPoint)target.LocalEndpoint).Port, "TCP"); config.Up.Delay = 60000;
  using (var engine = new Engine(config)) using (var client = new TcpClient()) {
   await engine.StartAsync(); await client.ConnectAsync(IPAddress.Loopback, config.ListenPort); using (var accepted = await target.AcceptTcpClientAsync()) {
    await client.GetStream().WriteAsync(new byte[] { 1 }, 0, 1); await Task.Delay(50); var sw = Stopwatch.StartNew(); await Timeout(engine.StopAsync()); Check(sw.ElapsedMilliseconds < 1000, "Stop cancels long TCP delay promptly");
   }
   using (var again = new Engine(config)) { await again.StartAsync(); await again.StopAsync(); Check(true, "TCP same-port restart"); }
  }
  target.Stop();
 }
 static async Task Run() { await TcpTest(0, 0); await TcpTest(60, 0); await TcpTest(0, 100); await UdpTest(false, false); await UdpTest(true, false); await UdpTest(false, true); await OrderTest(); await StopTest(); Console.WriteLine(count + " checks passed."); }
 static int Main() { try { Run().GetAwaiter().GetResult(); return 0; } catch (Exception ex) { Console.WriteLine(ex); return 1; } }
}
