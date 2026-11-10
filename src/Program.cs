using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Security.Principal;
using System.ServiceProcess;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;

static class App
{
    public const string ServiceName = "CloudflaredNew";
    public static string Root = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData), ServiceName);
    public static readonly JsonSerializerOptions Json = new() { WriteIndented = true };
    public static string ConfigPath => Path.Combine(Root, "tunnels.json");
    static int Main(string[] args)
    {
        try
        {
            var list = args.ToList();
            int pos = list.IndexOf("--data");
            if (pos >= 0) { Root = Path.GetFullPath(list[pos + 1]); list.RemoveRange(pos, 2); }
            args = list.ToArray();
            string cmd = args.FirstOrDefault()?.ToLowerInvariant() ?? "help";
            if (cmd is "help" or "--help" or "-h") { Help(); return 0; }
            if (cmd is "version" or "--version" or "-v") { Console.WriteLine("MultiCloudflared " + typeof(App).Assembly.GetName().Version!.ToString(3)); return 0; }
            if (!new WindowsPrincipal(WindowsIdentity.GetCurrent()).IsInRole(WindowsBuiltInRole.Administrator))
                throw new Exception("Open Terminal / PowerShell as Administrator to manage tunnels.");
            EnsureRoot();
            switch (cmd)
            {
                case "service-host": ServiceBase.Run(new ManagerService()); break;
                case "run":
                    Engine.Ensure(Read().Engine);
                    using (var stop = new CancellationTokenSource())
                    {
                        Console.CancelKeyPress += (_, e) => { e.Cancel = true; stop.Cancel(); };
                        Console.WriteLine("Managing tunnels; Ctrl+C stops all workers. Configuration reloads automatically.");
                        new Supervisor().Run(stop.Token).GetAwaiter().GetResult();
                    }
                    break;
                case "add":
                    Require(args, 2, "add NAME [--token-stdin | --token-file PATH]");
                    string token;
                    if (args.Contains("--token-stdin")) token = CleanToken(Console.In.ReadToEnd());
                    else if (Array.IndexOf(args, "--token-file") is int i && i >= 0) token = CleanToken(File.ReadAllText(args[i + 1]));
                    else { Console.Write("Tunnel token (hidden): "); token = ReadSecret(); }
                    ValidateToken(token);
                    Modify(c => {
                        CheckName(args[1]);
                        if (c.Tunnels.Any(t => t.Name.Equals(args[1], StringComparison.OrdinalIgnoreCase))) throw new Exception("Name already exists. Use token NAME to rotate its token.");
                        c.Tunnels.Add(new Tunnel { Name = args[1], Secret = Encrypt(token) });
                    });
                    Console.WriteLine($"Added {args[1]}. It starts automatically when the manager is running."); break;
                case "token":
                    Require(args, 2, "token NAME");
                    Console.Write("New tunnel token (hidden): "); string replacement = ReadSecret(); ValidateToken(replacement);
                    Modify(c => Find(c, args[1]).Secret = Encrypt(replacement));
                    Console.WriteLine("Token replaced; worker will restart."); break;
                case "remove":
                    Require(args, 2, "remove NAME"); Modify(c => c.Tunnels.Remove(Find(c, args[1])));
                    Console.WriteLine("Removed locally. Cloudflare tunnel and DNS records are unchanged."); break;
                case "start": case "stop": case "restart":
                    Require(args, 2, cmd + " NAME");
                    Modify(c => { var t = Find(c, args[1]); t.Enabled = cmd != "stop"; if (cmd == "restart") t.Revision++; });
                    Console.WriteLine("Saved. Running manager applies changes within two seconds."); break;
                case "engine":
                    Require(args, 2, "engine PATH | engine install");
                    if (args[1] is "install" or "upgrade") { Engine.Ensure(Engine.ManagedPath); Modify(c => c.Engine = Engine.ManagedPath); Console.WriteLine("Official engine ready."); break; }
                    string exe = Path.GetFullPath(args[1]);
                    if (!File.Exists(exe) || !exe.EndsWith(".exe", StringComparison.OrdinalIgnoreCase)) throw new Exception("An existing cloudflared.exe path is required.");
                    Modify(c => c.Engine = exe); Console.WriteLine("Cloudflared executable updated."); break;
                case "list": case "status": Status(); break;
                case "logs":
                    Require(args, 2, "logs NAME"); CheckName(args[1]);
                    string log = Path.Combine(Root, "logs", args[1] + ".log");
                    if (File.Exists(log)) foreach (string line in File.ReadLines(log).TakeLast(80)) Console.WriteLine(line);
                    else Console.WriteLine("No log yet."); break;
                case "service": Service(args); break;
                default: throw new Exception("Unknown command. Run cloudflarednew help.");
            }
            return 0;
        }
        catch (Exception e) { Console.Error.WriteLine("Error: " + e.Message); return 1; }
    }
    static void Help() => Console.WriteLine("""
MultiCloudflared — multiple tunnels, one manager service
Run management commands from an Administrator terminal.

  add NAME                 Prompt securely for a Cloudflare tunnel token
  add NAME --token-stdin   Read token from standard input
  add NAME --token-file P  Read token from an existing file (not deleted)
  token NAME               Replace a tunnel token using a hidden prompt
  remove NAME              Remove local worker, leaving Cloudflare/DNS intact
  list / status            Show configured tunnels and worker state
  start NAME / stop NAME   Enable / disable a tunnel
  restart NAME             Enable and restart one worker
  logs NAME                Last 80 lines of a tunnel's log
  engine PATH              Set a custom official cloudflared.exe path
  engine install           Download/verify the bundled-version official engine
  run                      Run all enabled tunnels in this console
  service install [TOKEN]  Add/update a tunnel and ensure the service is running
  service upgrade          Install a newly downloaded manager; preserve tunnels
  service start|stop|restart|status|uninstall
  --data PATH              Use an isolated data directory (also saved on install)

Default data: C:\ProgramData\CloudflaredNew
Workers use the official Cloudflare engine. Each distinct tunnel needs a worker.
Tokens are DPAPI machine-encrypted on disk; data is restricted to SYSTEM/Admins.
No tokens are placed in worker command lines. Service and console are exclusive.
""");
    static void Require(string[] a, int count, string usage) { if (a.Length < count) throw new Exception("Usage: cloudflarednew " + usage); }
    static void CheckName(string n) { if (!Regex.IsMatch(n, @"^[a-zA-Z0-9][a-zA-Z0-9_-]{0,47}$")) throw new Exception("Use 1–48 letters, numbers, hyphens or underscores for a name."); }
    static string CleanToken(string value) => value.Trim().Trim('\uFEFF').Trim();
    static string ReadSecret()
    {
        if (Console.IsInputRedirected) return CleanToken(Console.ReadLine() ?? "");
        var b = new StringBuilder();
        while (true) { var k = Console.ReadKey(true); if (k.Key == ConsoleKey.Enter) break; if (k.Key == ConsoleKey.Backspace) { if (b.Length > 0) b.Length--; } else if (!char.IsControl(k.KeyChar)) b.Append(k.KeyChar); }
        Console.WriteLine(); return CleanToken(b.ToString());
    }
    public static void ValidateToken(string token)
    {
        try
        {
            using var d = JsonDocument.Parse(Convert.FromBase64String(token));
            foreach (string key in new[] { "a", "t", "s" })
                if (string.IsNullOrWhiteSpace(d.RootElement.GetProperty(key).GetString())) throw new Exception();
            if (!Guid.TryParse(d.RootElement.GetProperty("t").GetString(), out _)) throw new Exception();
        }
        catch { throw new Exception("Invalid tunnel token. Paste only the eyJ... token from Cloudflare's connector command."); }
    }
    public static string Encrypt(string value) => Convert.ToBase64String(ProtectedData.Protect(Encoding.UTF8.GetBytes(value), null, DataProtectionScope.LocalMachine));
    public static string Decrypt(string value) => Encoding.UTF8.GetString(ProtectedData.Unprotect(Convert.FromBase64String(value), null, DataProtectionScope.LocalMachine));
    static Tunnel Find(Config c, string name) => c.Tunnels.FirstOrDefault(t => t.Name.Equals(name, StringComparison.OrdinalIgnoreCase)) ?? throw new Exception("Unknown tunnel: " + name);
    static void EnsureRoot()
    {
        Directory.CreateDirectory(Root);
        // Replace inherited access before writing any credentials.
        var security = new System.Security.AccessControl.DirectorySecurity();
        security.SetAccessRuleProtection(true, false);
        foreach (string sid in new[] { "S-1-5-18", "S-1-5-32-544" })
            security.AddAccessRule(new System.Security.AccessControl.FileSystemAccessRule(
                new SecurityIdentifier(sid), System.Security.AccessControl.FileSystemRights.FullControl,
                System.Security.AccessControl.InheritanceFlags.ContainerInherit | System.Security.AccessControl.InheritanceFlags.ObjectInherit,
                System.Security.AccessControl.PropagationFlags.None, System.Security.AccessControl.AccessControlType.Allow));
        new DirectoryInfo(Root).SetAccessControl(security);
        Directory.CreateDirectory(Path.Combine(Root, "logs"));
    }
    public static Config Read() => File.Exists(ConfigPath) ? JsonSerializer.Deserialize<Config>(File.ReadAllText(ConfigPath), Json)! : new();
    public static void Atomic(string path, object value)
    {
        string tmp = path + "." + Guid.NewGuid().ToString("N") + ".tmp";
        try { File.WriteAllText(tmp, JsonSerializer.Serialize(value, Json)); File.Move(tmp, path, true); }
        finally { if (File.Exists(tmp)) File.Delete(tmp); }
    }
    static void Modify(Action<Config> action)
    {
        using var gate = LockFile("config.lock");
        var c = Read(); action(c); Atomic(ConfigPath, c);
    }
    public static FileStream LockFile(string name)
    {
        for (int i = 0; ; i++)
        {
            try { return new FileStream(Path.Combine(Root, name), FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None); }
            catch (IOException) { if (i >= 20) throw new Exception("Manager or configuration is already in use."); Thread.Sleep(100); }
        }
    }
    static void Status()
    {
        var c = Read();
        Console.WriteLine("Engine: " + c.Engine);
        Console.WriteLine("Data:   " + Root);
        Snapshot? s = null;
        try { s = JsonSerializer.Deserialize<Snapshot>(File.ReadAllText(Path.Combine(Root, "status.json"))); } catch { }
        bool live = s != null && DateTimeOffset.UtcNow - s.Updated < TimeSpan.FromSeconds(10);
        Console.WriteLine("Manager: " + (live ? "running" : "stopped (or status stale)"));
        Console.WriteLine($"{"NAME",-25} {"ENABLED",-8} {"WORKER",-18} {"PID",-8} RESTARTS");
        foreach (var t in c.Tunnels)
        {
            var w = live ? s!.Workers.FirstOrDefault(w => w.Name == t.Name) : null;
            Console.WriteLine($"{t.Name,-25} {t.Enabled,-8} {w?.State ?? (t.Enabled ? "waiting" : "disabled"),-18} {w?.Pid,-8} {w?.Restarts}");
        }
        if (c.Tunnels.Count == 0) Console.WriteLine("No tunnels configured. Use: cloudflarednew add NAME");
        Console.WriteLine("Worker running means process alive; 'connected' means Cloudflare registered a connection.");
    }
    static void Sc(params string[] args)
    {
        var psi = new ProcessStartInfo("sc.exe") { UseShellExecute = false };
        foreach (var a in args) psi.ArgumentList.Add(a);
        using var p = Process.Start(psi)!; p.WaitForExit();
        if (p.ExitCode != 0) throw new Exception("Windows service command failed: " + p.ExitCode);
    }
    static string TokenIdentity(string token)
    {
        ValidateToken(token);
        using var doc = JsonDocument.Parse(Convert.FromBase64String(token));
        return doc.RootElement.GetProperty("a").GetString() + "/" +
            Guid.Parse(doc.RootElement.GetProperty("t").GetString()!).ToString("D");
    }
    static void InstallToken(string token)
    {
        string identity = TokenIdentity(token);
        string name = "";
        Modify(c =>
        {
            var existing = c.Tunnels.FirstOrDefault(t => TokenIdentity(Decrypt(t.Secret)) == identity);
            if (existing != null)
            {
                name = existing.Name;
                // Repeating an unchanged installation must not interrupt connections.
                if (Decrypt(existing.Secret) != token) existing.Secret = Encrypt(token);
                existing.Enabled = true;
            }
            else
            {
                name = identity.Split('/')[1];
                if (c.Tunnels.Any(t => t.Name.Equals(name, StringComparison.OrdinalIgnoreCase)))
                    throw new Exception("A different tunnel already uses the generated name.");
                c.Tunnels.Add(new Tunnel { Name = name, Secret = Encrypt(token) });
            }
        });
        Console.WriteLine("Tunnel configured: " + name);
    }
    static void EnsureStarted(ServiceController svc)
    {
        svc.Refresh();
        if (svc.Status == ServiceControllerStatus.StopPending)
            svc.WaitForStatus(ServiceControllerStatus.Stopped, TimeSpan.FromSeconds(30));
        svc.Refresh();
        if (svc.Status == ServiceControllerStatus.Stopped) svc.Start();
        svc.WaitForStatus(ServiceControllerStatus.Running, TimeSpan.FromSeconds(30));
    }
    static void Service(string[] args)
    {
        Require(args, 2, "service install|start|stop|restart|status|uninstall");
        if (args[1] is "install" or "upgrade")
        {
            bool upgrading = args[1] == "upgrade";
            if (args.Length > (upgrading ? 2 : 3)) throw new Exception("Usage: service install [TOKEN] | service upgrade");
            string? installToken = args.Length == 3 ? CleanToken(args[2]) : null;
            if (installToken != null) ValidateToken(installToken);
            using (var serviceKey = Microsoft.Win32.Registry.LocalMachine.OpenSubKey(
                @"SYSTEM\CurrentControlSet\Services\" + ServiceName))
            {
                if (serviceKey != null)
                {
                    string imagePath = serviceKey.GetValue("ImagePath") as string ?? "";
                    var match = Regex.Match(imagePath, "--data\\s+\"([^\"]+)\"");
                    if (!match.Success || !Path.GetFullPath(match.Groups[1].Value).TrimEnd('\\')
                        .Equals(Root.TrimEnd('\\'), StringComparison.OrdinalIgnoreCase))
                        throw new Exception("Existing manager service uses a different data directory. Use its --data path.");
                    if (upgrading && Read().Engine.StartsWith(Path.Combine(Engine.InstallDirectory, "engine") + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase))
                    {
                        Engine.Ensure(Engine.ManagedPath);
                        Modify(c => c.Engine = Engine.ManagedPath);
                    }
                    Engine.Ensure(Read().Engine);
                    if (installToken != null) InstallToken(installToken);
                    using var existingService = new ServiceController(ServiceName);
                    if (upgrading) UpgradeManager(existingService);
                    else EnsureStarted(existingService);
                    Console.WriteLine("Manager service is running; tunnel changes apply automatically.");
                    return;
                }
            }
            Engine.Ensure(Read().Engine);
            if (installToken != null) InstallToken(installToken);
            // Reparse points / writable executable locations are controlled by the administrator.
            string self = Environment.ProcessPath!;
            if (Path.GetFileNameWithoutExtension(self).Equals("dotnet", StringComparison.OrdinalIgnoreCase)) throw new Exception("Install using the published cloudflarednew.exe.");
            string installDir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), "CloudflaredNew");
            Directory.CreateDirectory(installDir);
            string installed = Path.Combine(installDir, "cloudflarednew.exe");
            if (!self.Equals(installed, StringComparison.OrdinalIgnoreCase)) File.Copy(self, installed, true);
            string bin = $"\"{installed}\" service-host --data \"{Root}\"";
            Sc("create", ServiceName, "binPath=", bin, "start=", "delayed-auto", "DisplayName=", "CloudflaredNew — Multi Tunnel Manager");
            Sc("description", ServiceName, "Manages multiple independent Cloudflare tunnel workers.");
            Sc("failure", ServiceName, "reset=", "86400", "actions=", "restart/5000/restart/15000/restart/60000");
            using var createdService = new ServiceController(ServiceName);
            EnsureStarted(createdService);
            Console.WriteLine("Manager service installed and running.");
            return;
        }
        using var svc = new ServiceController(ServiceName);
        switch (args[1])
        {
            case "status": Console.WriteLine(svc.Status); break;
            case "start": Engine.Ensure(Read().Engine); EnsureStarted(svc); break;
            case "stop": StopService(svc); break;
            case "restart": StopService(svc); svc.Start(); svc.WaitForStatus(ServiceControllerStatus.Running, TimeSpan.FromSeconds(30)); break;
            case "uninstall":
                try { _ = svc.Status; }
                catch (InvalidOperationException e) when (e.InnerException is System.ComponentModel.Win32Exception w && w.NativeErrorCode == 1060)
                { Console.WriteLine("Service is already uninstalled; configuration retained."); break; }
                StopService(svc); Sc("delete", ServiceName); Console.WriteLine("Service removed; configuration retained."); break;
            default: throw new Exception("Unknown service command.");
        }
    }
    static void UpgradeManager(ServiceController svc)
    {
        string destination = Path.Combine(Engine.InstallDirectory, "cloudflarednew.exe");
        string candidate = Environment.ProcessPath!;
        if (candidate.Equals(destination, StringComparison.OrdinalIgnoreCase))
        { EnsureStarted(svc); Console.WriteLine("Run service upgrade from the newly downloaded executable to replace the installed version."); return; }
        string backup = destination + ".previous";
        StopService(svc);
        File.Copy(destination, backup, true);
        try
        {
            File.Copy(candidate, destination, true);
            EnsureStarted(svc);
            File.Delete(backup);
            Console.WriteLine("Installed manager upgraded. Tunnel configuration preserved.");
        }
        catch
        {
            try { StopService(svc); File.Copy(backup, destination, true); EnsureStarted(svc); } catch { }
            throw;
        }
    }
    static void StopService(ServiceController svc)
    {
        if (svc.Status != ServiceControllerStatus.Stopped) { svc.Stop(); svc.WaitForStatus(ServiceControllerStatus.Stopped, TimeSpan.FromSeconds(30)); }
    }
}
sealed class Config
{
    public string Engine { get; set; } = global::Engine.ManagedPath;
    public List<Tunnel> Tunnels { get; set; } = [];
}
sealed class Tunnel
{
    public string Name { get; set; } = "";
    public string Secret { get; set; } = "";
    public bool Enabled { get; set; } = true;
    public int Revision { get; set; }
}
sealed class Snapshot
{
    public DateTimeOffset Updated { get; set; } = DateTimeOffset.UtcNow;
    public List<WorkerState> Workers { get; set; } = [];
}
sealed class WorkerState
{
    public string Name { get; set; } = "";
    public string State { get; set; } = "";
    public int? Pid { get; set; }
    public int Restarts { get; set; }
}
sealed class ManagerService : ServiceBase
{
    CancellationTokenSource? stop;
    Task? run;
    public ManagerService() { ServiceName = App.ServiceName; CanStop = true; CanShutdown = true; }
    protected override void OnStart(string[] args)
    {
        stop = new();
        run = new Supervisor().Run(stop.Token);
        _ = run.ContinueWith(t => { if (t.IsFaulted) Environment.Exit(1); }, TaskScheduler.Default);
    }
    protected override void OnStop() { stop?.Cancel(); run?.GetAwaiter().GetResult(); stop?.Dispose(); }
    protected override void OnShutdown() => OnStop();
}
sealed class Supervisor
{
    readonly Dictionary<string, Worker> workers = new(StringComparer.OrdinalIgnoreCase);
    public async Task Run(CancellationToken ct)
    {
        using var exclusive = App.LockFile("manager.lock");
        using var job = new Job();
        try
        {
            while (!ct.IsCancellationRequested)
            {
                try
                {
                    var cfg = App.Read();
                    foreach (var name in workers.Keys.ToArray())
                    {
                        var t = cfg.Tunnels.FirstOrDefault(t => t.Name.Equals(name, StringComparison.OrdinalIgnoreCase) && t.Enabled);
                        if (t == null || workers[name].Signature != Signature(cfg, t))
                        { workers[name].Dispose(); workers.Remove(name); }
                    }
                    foreach (var t in cfg.Tunnels.Where(t => t.Enabled))
                    {
                        if (!workers.TryGetValue(t.Name, out var w))
                            workers[t.Name] = w = new Worker(t, cfg.Engine, Signature(cfg, t), job);
                        w.Tick();
                    }
                    App.Atomic(Path.Combine(App.Root, "status.json"), new Snapshot { Workers = workers.Values.Select(w => w.Status()).ToList() });
                }
                catch (Exception e) { ManagerLog(e.Message); }
                try { await Task.Delay(1000, ct); } catch (OperationCanceledException) { }
            }
        }
        finally
        {
            foreach (var w in workers.Values) w.Dispose();
            App.Atomic(Path.Combine(App.Root, "status.json"), new Snapshot { Updated = DateTimeOffset.MinValue });
        }
    }
    static string Signature(Config c, Tunnel t) => c.Engine + "|" + t.Secret + "|" + t.Revision;
    static void ManagerLog(string msg)
    {
        var path = Path.Combine(App.Root, "logs", "manager.log");
        if (File.Exists(path) && new FileInfo(path).Length > 2_000_000) File.Move(path, path + ".1", true);
        File.AppendAllText(path, $"{DateTimeOffset.Now:O} {msg}{Environment.NewLine}");
    }
}
sealed class Worker : IDisposable
{
    readonly Tunnel tunnel;
    readonly string engine;
    readonly Job job;
    readonly object logGate = new();
    Process? p;
    string token = "";
    DateTimeOffset next = DateTimeOffset.MinValue, started;
    int failures, restarts;
    volatile bool connected;
    bool disposed;
    public string Signature { get; }
    public Worker(Tunnel t, string e, string signature, Job j) { tunnel = t; engine = e; Signature = signature; job = j; }
    public void Tick()
    {
        if (p != null)
        {
            if (!p.HasExited) return;
            Log($"Worker exited with code {p.ExitCode}.");
            p.WaitForExit(); p.Dispose(); p = null; connected = false;
            if (DateTimeOffset.UtcNow - started > TimeSpan.FromMinutes(2)) failures = 0;
            Delay();
        }
        if (DateTimeOffset.UtcNow < next) return;
        try
        {
            token = App.Decrypt(tunnel.Secret); App.ValidateToken(token);
            var psi = new ProcessStartInfo(engine) { UseShellExecute = false, CreateNoWindow = true, RedirectStandardError = true, RedirectStandardOutput = true };
            foreach (var a in new[] { "tunnel", "--no-autoupdate", "--metrics", "127.0.0.1:0", "run" }) psi.ArgumentList.Add(a);
            psi.Environment["TUNNEL_TOKEN"] = token;
            psi.Environment.Remove("TUNNEL_TOKEN_FILE");
            p = new Process { StartInfo = psi };
            p.OutputDataReceived += (_, a) => Output(a.Data);
            p.ErrorDataReceived += (_, a) => Output(a.Data);
            p.Start();
            try { job.Add(p); } catch { p.Kill(true); p.WaitForExit(); throw; }
            p.BeginOutputReadLine(); p.BeginErrorReadLine();
            started = DateTimeOffset.UtcNow; restarts++;
            Log("Worker started. Token supplied privately via process environment.");
        }
        catch (Exception e)
        {
            if (p != null) { try { if (!p.HasExited) p.Kill(true); } catch { } p.Dispose(); p = null; }
            Log("Could not start worker: " + e.Message); Delay();
        }
    }
    void Delay() { failures++; next = DateTimeOffset.UtcNow.AddSeconds(Math.Min(60, Math.Pow(2, Math.Min(failures, 6)))); }
    void Output(string? line)
    {
        if (line == null) return;
        if (line.Contains("Registered tunnel connection", StringComparison.OrdinalIgnoreCase)) connected = true;
        // This is last registration evidence, not an HTTP availability probe.
        if (line.Contains("Unregistered tunnel connection", StringComparison.OrdinalIgnoreCase) || line.Contains("connection terminated", StringComparison.OrdinalIgnoreCase)) connected = false;
        Log(line);
    }
    void Log(string line)
    {
        lock (logGate)
        {
            if (disposed) return;
            if (token.Length > 0) line = line.Replace(token, "[REDACTED]", StringComparison.Ordinal);
            line = Regex.Replace(line, @"eyJ[A-Za-z0-9+/=_-]{40,}", "[REDACTED]");
            string path = Path.Combine(App.Root, "logs", tunnel.Name + ".log");
            if (File.Exists(path) && new FileInfo(path).Length > 2_000_000) File.Move(path, path + ".1", true);
            File.AppendAllText(path, $"{DateTimeOffset.Now:O} {line}{Environment.NewLine}");
        }
    }
    public WorkerState Status() => new() { Name = tunnel.Name, State = p != null && !p.HasExited ? (connected ? "connected" : "running") : "retrying", Pid = p != null && !p.HasExited ? p.Id : null, Restarts = Math.Max(0, restarts - 1) };
    public void Dispose()
    {
        if (p != null)
        {
            try { if (!p.HasExited) { p.Kill(true); p.WaitForExit(5000); } } catch { }
            p.Dispose(); p = null;
        }
        lock (logGate) disposed = true;
        token = "";
    }
}
// Kill all workers if the manager crashes or is forcibly terminated.
sealed class Job : IDisposable
{
    readonly IntPtr handle;
    public Job()
    {
        handle = CreateJobObject(IntPtr.Zero, null);
        if (handle == IntPtr.Zero) throw new System.ComponentModel.Win32Exception();
        var info = new ExtendedLimits { Basic = new BasicLimits { LimitFlags = 0x2000 } };
        int size = Marshal.SizeOf<ExtendedLimits>();
        IntPtr ptr = Marshal.AllocHGlobal(size);
        try
        {
            Marshal.StructureToPtr(info, ptr, false);
            if (!SetInformationJobObject(handle, 9, ptr, (uint)size)) throw new System.ComponentModel.Win32Exception();
        }
        catch { CloseHandle(handle); throw; }
        finally { Marshal.FreeHGlobal(ptr); }
    }
    public void Add(Process p) { if (!AssignProcessToJobObject(handle, p.Handle)) throw new System.ComponentModel.Win32Exception(); }
    public void Dispose() => CloseHandle(handle);
    [StructLayout(LayoutKind.Sequential)] struct BasicLimits { public long PerProcessUserTimeLimit, PerJobUserTimeLimit; public uint LimitFlags; public UIntPtr MinimumWorkingSetSize, MaximumWorkingSetSize; public uint ActiveProcessLimit; public UIntPtr Affinity; public uint PriorityClass, SchedulingClass; }
    [StructLayout(LayoutKind.Sequential)] struct IoCounters { public ulong ReadOperationCount, WriteOperationCount, OtherOperationCount, ReadTransferCount, WriteTransferCount, OtherTransferCount; }
    [StructLayout(LayoutKind.Sequential)] struct ExtendedLimits { public BasicLimits Basic; public IoCounters IoInfo; public UIntPtr ProcessMemoryLimit, JobMemoryLimit, PeakProcessMemoryUsed, PeakJobMemoryUsed; }
    [DllImport("kernel32.dll", CharSet=CharSet.Unicode, SetLastError=true)] static extern IntPtr CreateJobObject(IntPtr attributes, string? name);
    [DllImport("kernel32.dll", SetLastError=true)] static extern bool SetInformationJobObject(IntPtr h, int infoClass, IntPtr info, uint length);
    [DllImport("kernel32.dll", SetLastError=true)] static extern bool AssignProcessToJobObject(IntPtr h, IntPtr process);
    [DllImport("kernel32.dll")] static extern bool CloseHandle(IntPtr h);
}
