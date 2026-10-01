# RAM Razor ⚡

**Admin-only RAM cleaner & background-app terminator for Windows 10 / Windows 11.**

RAM Razor shows what is eating your memory, closes it, and purges the memory Windows refuses to give back — with a full activity log, a miner watchdog, a permanent blacklist and a user keep-list.

> v2 note: v1's RAM purge sent the wrong kernel commands (see "What was fixed in v2" below), so cleaning had almost no visible effect. v2 is a complete re-engineering of the clean engine — it now actually frees RAM.

---

## Download

| File | What it is |
|---|---|
| [`RAMRazor.exe`](../../releases/latest) | **Self-contained** (~40–60 MB). No .NET install needed. Download → right-click → *Run as administrator*. |
| [`RAMRazor-portable.exe`](../../releases/latest) | Tiny portable build (~0.3 MB). Needs the free [.NET 8 Desktop Runtime](https://dotnet.microsoft.com/download/dotnet/8.0/runtime) (**Desktop Runtime x64**). |

Every published build has passed an automated self-test (`RAMRazor.exe --selftest`) on a real Windows machine — see `selftest-result.txt` attached to the release.

**The app refuses to start without Administrator rights by design** — process control and kernel memory cleaning are impossible without elevation. It exits with error code 740 (elevation required).

---

## What RAM Razor does

### The two headline buttons

| Button | Behavior |
|---|---|
| **CLEAN RAM** | Pure memory purge with the intensity selected underneath it:<br>• **Light** — kernel working-set trim (all processes)<br>• **Deep** — + standby list purge, second empty+purge pass<br>• **Extreme** — + modified page list flush (heaviest, frees the most) |
| **SMART CLEAN** | The v2 flagship. **With nothing ticked** it closes *every process that is not needed to keep Windows alive*, then purges memory.<br>• **Standard scope** — closes all non-system apps and background junk, shell stays.<br>• **Nuclear scope** — closes EVERYTHING except the BSOD-critical set (see below) and your keep-list; the desktop shell (explorer.exe) is closed too and **restarted automatically**.<br>**With apps ticked** in the list — closes exactly those, then purges. |

Every stage of every clean is reported in the Activity log with its NTSTATUS — you can *see* what worked instead of guessing.

### Mass-close tools

- **CLOSE ALL APPS** — force-closes all non-system apps (system components protected), then auto-purges.
- **Close selected… / Close all but selected…** — pick targets from the full list (Apps or Tasks view), the rest is kept.
- **Force close selected (forever)** — puts the selected apps on the permanent **blacklist**: they are closed now and killed on sight every time they start again. Manage it in the **Blacklist** window.

### Miner watchdog

A background heuristic scanner (every 10–120 s, configurable) flags:

- known miner names/signatures (xmrig, cpuminer, ethminer, nbminer, …),
- windowless processes pegging CPU,
- processes holding Stratum-like TCP connections.

Findings pop up as warning cards with **Force close (forever)** (blacklist) and **Force delete** (close + quarantine the executable). Optionally enable **auto kill + blacklist** in Settings for zero-click response.

### Views & lists

- **Apps / Tasks** radio buttons: group processes per executable (an app = all its helper processes) or list every task individually.
- **Show system tasks** to see protected system components (read-only for system safety).
- Right-click any row: close, force close, blacklist forever, **protect from Smart Clean (keep-list)**, copy path.
- Live RAM gauge + **RAM history graph** + used/available/processes counters.

### Automation & settings (Settings dialog)

- Clean intensity + Smart Clean scope
- Nuclear: auto-restart explorer, optionally include session-0 service hosts
- Auto CLEAN RAM every 5–60 min, optionally only when RAM ≥ a threshold %
- Clean at startup / start minimized in tray
- Auto re-kill apps that re-open themselves after being closed
- Miner scan interval + auto-kill miners
- Activity log on/off (`%ProgramData%\RAMRazor\logs\`)
- **Keep-list** editor — apps Smart Clean must never close

### Tray

Closing the window minimizes to the notification tray. Tray menu: **Show / Clean RAM now / Smart clean now / Exit**.

---

## Safety model

**Never killed (Standard scope + Close All):** everything classified as system — kernel processes, session-0 service hosts, `C:\Windows` binaries, Defender, the shell, search, driver hosts, RAM Razor itself.

**Never killed (Nuclear scope) — the BSOD-critical set:**

```
System, Idle, Secure System, Registry, Memory Compression,
smss, csrss, wininit, winlogon, services, svchost, lsass,
dwm, fontdrvhost, RAM Razor itself
```

Everything else dies in nuclear mode — that is the point — but:

- your **keep-list** apps are always spared,
- killing services (session 0) is opt-in and off by default,
- explorer.exe is restarted automatically,
- everything is logged, and unkillable processes are reported in warning cards.

**Force delete** never destroys files: executables are moved to `C:\ProgramData\RAMRazor\quarantine\` (or scheduled for deletion at next reboot if locked).

---

## What was fixed in v2

v1 called `NtSetSystemInformation(SystemMemoryListInformation, …)` with the wrong command values. The real Windows enum is **0-based**:

| Command | v1 sent | v2 sends |
|---|---|---|
| `MemoryEmptyWorkingSets` | ❌ 3 (actually *FlushModifiedList* → rejected) | ✅ **2** |
| `MemoryPurgeStandbyList` | ❌ 5 (actually *PurgeLowPriorityStandbyList* → barely visible) | ✅ **4** |

So v1 "cleaned" without freeing anything. v2 also:

- enables all three required privileges up-front (`SeIncreaseQuotaPrivilege`, `SeProfileSingleProcessPrivilege`, `SeDebugPrivilege`),
- runs a **multi-pass pipeline** (trim → purge → per-process trim → trim+purge again) so pages pushed out by working-set trims are actually freed,
- reports **every stage** with its status,
- adds the **Smart Clean** mass-terminator, the keep-list, settings persistence, tray actions, RAM history graph, auto-clean with threshold, clean-at-startup, and miner auto-kill.

---

## Building from source

```bash
dotnet build RAMRazor/RAMRazor.csproj -c Release
dotnet publish RAMRazor/RAMRazor.csproj -c Release -r win-x64 --self-contained true -p:PublishSingleFile=true
```

GitHub Actions (`.github/workflows/build.yml`) builds, runs the self-test on a real Windows runner and attaches both exes to a Release on every `v*` tag.

Headless self-test (also runs in CI): `RAMRazor.exe --selftest`

## Credits

Techniques and inspiration from excellent open-source projects:

- [Mem Reduct](https://github.com/henrypp/memreduct) (henrypp) — standby list purge / working-set emptying via `NtSetSystemInformation`
- [WinMemoryCleaner](https://github.com/IgorMundstein/WinMemoryCleaner) (IgorMundstein) — aggressive combined clean passes
- [System Informer (Process Hacker)](https://github.com/winsiderss/systeminformer) — process enumeration and system-process classification ideas

RAM Razor is MIT-licensed. Use the nuclear scope at your own discretion — it does exactly what it says.
