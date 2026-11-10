# Verification — 10 November 2026

- Release self-contained Windows x64 publish succeeded.
- Two isolated simulated workers registered concurrently.
- Tokens encrypted in persisted configuration; fake token emitted by fixture redacted from logs.
- Worker command lines contained no tokens.
- Stop, start, restart and remove hot-reloaded independently.
- Force-killed worker automatically restarted.
- Force-killed manager left no child worker running (Windows Job Object cleanup).
- Temporary Windows service started as LocalSystem, decrypted the existing tunnel token and registered a real Cloudflare connection.
- Service restart and uninstall succeeded.
- Original Cloudflared service remained running with its original PID.
- Temporary live connector and test data removed.

Two distinct live tunnel credentials were not available; multi-worker orchestration was tested with fixtures, and real network authentication with one existing tunnel's temporary replica. Connected status is registration evidence, not application-level health.

## Compatibility command verification
- Both cloudflarednew.exe and cloudflared.exe aliases rebuilt.
- service install TOKEN created and started the manager service using the supplied tunnel credential.
- Real Cloudflare connection registered.
- Repeated service install with the same token preserved the worker PID and did not duplicate the entry.
- Existing multi-worker integration checks passed again.
- Original Cloudflared service remains running.
