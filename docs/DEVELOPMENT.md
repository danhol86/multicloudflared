# Development and release

.NET 10 SDK, Windows x64 and Administrator PowerShell are required for integration/service tests.

- build.ps1 publishes a self-contained compressed executable.
- scripts/Package.ps1 creates the release ZIP, standalone executable and checksums.
- tests/Integration.ps1 exercises multiple workers, hot reload, retries, job cleanup and secret redaction using a fake engine.
- tests/ServiceLifecycle.ps1 tests install/add/rotate/upgrade/uninstall against fake workers. It refuses to run if an existing CloudflaredNew service is installed.
- tests/LiveService.ps1 is an optional machine-specific live replica check. It is never run in CI and requires an existing original Cloudflared service plus explicit permission to use its token.

Release process:
1. Change Version in src/cloudflarednew.csproj.
2. When updating the official engine, change both Version and Sha256 in src/Engine.cs after checking the official GitHub release asset digest.
3. Commit and push main.
4. Create and push a matching vX.Y.Z tag.
5. The GitHub release workflow builds, tests and publishes the ZIP, EXE and checksums. Ordinary main/PR builds only upload artifacts.

GitHub Actions uses hosted Windows runners. Release publication needs only the repository's automatic GITHUB_TOKEN with contents:write for the publish job. No tunnel token or account secret belongs in the repository, workflow or release package.

Do not replace the original Cloudflared service, silently migrate user tunnels, print registry service command lines or persist tokens in source/test artifacts. Tests should use isolated --data directories and fake credentials. Read AGENTS.md before changes.

The Windows service and installation directory retain the CloudflaredNew name for compatibility.
