# Setup and troubleshooting

## Before you start

Use Windows x64 with administrator access and an existing Cloudflare account. Install your application locally first and verify its origin URL works. MultiCloudflared connects existing tunnels; Cloudflare remains responsible for tunnel creation, routing and DNS.

The downloaded ZIP contains:
- cloudflared.exe: standalone manager, including its .NET runtime.
- Install.cmd / Install.ps1: install service and Start menu shortcut.
- Manage.cmd / Manage.ps1: menu for adding/removing/starting/stopping tunnels.
- Update.cmd: download and apply the latest manager release.
- Uninstall.cmd / Uninstall.ps1: remove the app, optionally purge settings.
- README and license notices.

## Cloudflare-style setup

In Cloudflare, create a Cloudflared tunnel, choose Windows/64-bit, and copy the token from its connector command. From Administrator PowerShell in your extracted/download folder:

```powershell
.\cloudflared.exe service install <TOKEN>
.\cloudflared.exe status
```

Repeat service install with each independent tunnel token. It works if the service is already installed. The UUID becomes the tunnel's local name.

If you prefer a readable name:
```powershell
.\cloudflared.exe add home
.\cloudflared.exe service install
```

Paste the token at the hidden prompt. Closing the management window does not stop the service.

## Routing

Configure each tunnel's published application/hostname in Cloudflare. Set its service URL to the address the Windows machine can reach, such as http://localhost:3000. A running connector does not create hostname routes automatically.

## Existing Cloudflared users

MultiCloudflared uses a separate service name. It does not import, disable or delete the original Cloudflared service. Using the same tunnel token for both creates two connectors (replicas). Only stop the old service yourself after confirming the new connector and routes work if you intend to migrate.

## Network / downloads

First installation downloads the pinned official Windows amd64 engine from Cloudflare's GitHub release and verifies its SHA-256 against the value included in the app. GitHub access is required for this step and for Update.cmd. An existing managed engine is reused if its checksum matches.

For restricted/offline machines, separately obtain the official engine and set an absolute path before installing the service:
```powershell
.\cloudflared.exe engine "C:\Tools\cloudflared-official.exe"
.\cloudflared.exe service install <TOKEN>
```

Do not point engine PATH at this manager's EXE. The manager is not a tunnel protocol implementation.

## Problems

| Symptom | What to check |
| --- | --- |
| Access denied | Use Administrator PowerShell or the CMD launchers and approve UAC. |
| PowerShell says command not found | Use .\cloudflared.exe or the full path to the downloaded manager. |
| Service already exists from the official binary | You invoked the upstream executable rather than this project's EXE. |
| Download/checksum failure | Check access to GitHub; rerun installation. Unverified engine files are not executed. |
| Tunnel is retrying | Use logs NAME. Verify the token is current and your network permits Cloudflare Tunnel traffic. |
| Connected but website fails | Check published hostname/DNS, origin URL and whether the app is running. |
| Disabled | Use start NAME. Start the manager with service start if it is stopped. |
| Wrong configuration directory | Match the --data directory used when installing the service. |
| Moving to another PC | Re-enter tokens; DPAPI-encrypted configuration is bound to the original machine. |

Worker logs and manager.log are under C:\ProgramData\CloudflaredNew\logs. Each rotates at about 2 MB plus one backup. Do not include tokens in GitHub issues.

## Removal and reinstall

service uninstall removes only the manager service and stops its workers. Uninstall.cmd also removes installed files/shortcut. Both keep encrypted configuration. Uninstall.ps1 -Purge explicitly removes the default data directory too. Custom data directories are retained for manual review.

Remote Cloudflare tunnels and DNS entries are never deleted by local uninstall. Reinstalling with the same machine's retained data reconnects enabled tunnels.
