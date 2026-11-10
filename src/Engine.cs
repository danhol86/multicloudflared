using System.Security.Cryptography;
static class Engine
{
    // Pinned upstream release. Update version and SHA together after verifying GitHub's asset digest.
    public const string Version = "2026.10.7";
    public const string Sha256 = "2493dbc168923bb3e59e5e7716f71deced92992eea9acd6e9d74f84ac7751343";
    public static string InstallDirectory => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), "CloudflaredNew");
    public static string ManagedPath => Path.Combine(InstallDirectory, "engine", "cloudflared-" + Version + ".exe");
    public static void Ensure(string path)
    {
        bool managed = Path.GetFullPath(path).Equals(ManagedPath, StringComparison.OrdinalIgnoreCase);
        if (!managed)
        {
            if (!File.Exists(path)) throw new Exception("Custom engine not found. Use engine install to download the official engine, or engine PATH.");
            return;
        }
        Directory.CreateDirectory(Path.GetDirectoryName(ManagedPath)!);
        using var gate = new FileStream(Path.Combine(InstallDirectory, "engine", "download.lock"), FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None);
        if (File.Exists(path) && Hash(path).Equals(Sha256, StringComparison.OrdinalIgnoreCase)) return;
        string temp = path + "." + Guid.NewGuid().ToString("N") + ".download";
        try
        {
            Console.WriteLine("Downloading official Cloudflared " + Version + "...");
            using var client = new HttpClient { Timeout = TimeSpan.FromMinutes(5) };
            client.DefaultRequestHeaders.UserAgent.ParseAdd("MultiCloudflared/1.1");
            using var response = client.GetAsync("https://github.com/cloudflare/cloudflared/releases/download/" + Version + "/cloudflared-windows-amd64.exe", HttpCompletionOption.ResponseHeadersRead).GetAwaiter().GetResult();
            response.EnsureSuccessStatusCode();
            using (var stream = new FileStream(temp, FileMode.CreateNew, FileAccess.Write, FileShare.None))
                response.Content.CopyToAsync(stream).GetAwaiter().GetResult();
            if (!Hash(temp).Equals(Sha256, StringComparison.OrdinalIgnoreCase)) throw new Exception("Official engine checksum did not match. Download rejected.");
            File.Move(temp, path, true);
            Console.WriteLine("Official engine downloaded and SHA-256 verified.");
        }
        finally { if (File.Exists(temp)) File.Delete(temp); }
    }
    static string Hash(string path) { using var file = File.OpenRead(path); return Convert.ToHexString(SHA256.HashData(file)); }
}
