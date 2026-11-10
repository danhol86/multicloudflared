# CloudflaredNew

Windows .NET 10 CLI and ServiceBase supervisor around the official Cloudflare executable. Do not rewrite Cloudflare protocols or claim one worker handles multiple tunnel identities.

- Build: powershell -ExecutionPolicy Bypass -File build.ps1
- Integration tests: dotnet build tests/FakeEngine -c Release, then elevated powershell -ExecutionPolicy Bypass -File tests/Integration.ps1.
- LiveService.ps1 creates a temporary replica from this machine's existing Cloudflared service token; requires explicit authorization for a live check. It never prints the token and removes its temporary service/data. Do not run if CloudflaredNew is already installed.
- Never log, commit, or print actual tokens, raw service command lines, private configs, or worker environments.
- Preserve the original Cloudflared service; migration is a separate operation.
- Persistent state is ProgramData/CloudflaredNew, DPAPI LocalMachine encrypted with SYSTEM/Administrators-only ACLs.
- Service installation copies the manager into Program Files. The upstream engine remains external and shared.
- Verify independent worker restart, config reload, crash cleanup, token redaction, and LocalSystem decryption after relevant changes.
- Worker connected state reflects registration logs, not end-to-end application health.
- Never store credentials in this project. Keep tests isolated with --data.

## Public releases
- Repository: https://github.com/danhol86/multicloudflared; main branch.
- Public app name MultiCloudflared; keep CloudflaredNew service/data/install names for compatibility.
- Fresh installs must bootstrap the pinned, SHA-256-verified upstream engine; never depend on the author's installation.
- GitHub Actions builds/tests main and pull requests; matching vX.Y.Z tags publish ZIP, EXE and checksums after tests pass.
- Package: scripts/Package.ps1. Preserve .NET license notices in release ZIPs.
- Lifecycle tests require an empty machine (no pre-existing manager service).
- Keep Install/Update/Uninstall scripts and README commands consistent with actual binary behaviour.
