# CLI reference

Run management commands in an Administrator terminal. The documented download is cloudflared.exe. cloudflarednew.exe is the same manager under its historical installed filename.

| Command | Behaviour |
| --- | --- |
| help / --help | Help without elevation |
| version / --version | Manager version |
| service install [TOKEN] | Ensure engine/service exists and runs; optionally add/update/enable tunnel by account + tunnel ID |
| service upgrade | Install this downloaded manager version, preserving configuration; rollback old executable if service start fails |
| service start / stop / restart / status | Control the manager and all its workers |
| service uninstall | Stop workers and remove service; retain files and settings; repeated uninstall is safe |
| add NAME | Add entry; securely prompt for token |
| add NAME --token-stdin | Read token from standard input |
| add NAME --token-file PATH | Read token file; source file is not deleted |
| token NAME | Prompt for replacement token |
| remove NAME | Remove local entry; hot reload stops its worker |
| start NAME | Enable entry |
| stop NAME | Disable entry, retained across reboots |
| restart NAME | Enable/restart this worker only |
| status / list | Desired configuration and latest worker states |
| logs NAME | Last 80 lines of worker log |
| engine install | Download/verify pinned upstream engine and use it |
| engine PATH | Use custom official engine at absolute path |
| run | Foreground manager; Ctrl+C stops workers |
| --data PATH | Data directory override (any command); installation saves it in the service command |

Names accept 1–48 letters, digits, underscores or hyphens. service install TOKEN uses the full tunnel UUID as its name. Configured friendly names are preserved when the same identity is installed again.

Only one Windows manager service is installed, containing multiple tunnel workers. Foreground managers can use separate --data directories. A file lock excludes two managers using the same directory.

Settings are reloaded each second. Failed workers retry with exponential backoff up to 60 seconds. Worker processes belong to a kill-on-close Windows Job Object; manager termination cleans them up.

Connected status is log-based registration evidence, not a live readiness or application probe.
