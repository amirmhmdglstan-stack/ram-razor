# RAM Razor 🪒

**An admin-only RAM cleaner & background-app terminator for Windows 10 / Windows 11.**
One click closes every unnecessary app until only protected Windows system tasks remain — and it keeps watching.

![platform](https://img.shields.io/badge/platform-Windows%2010%20%7C%2011-blue) ![runtime](https://img.shields.io/badge/.NET-8.0%20WinForms-purple) ![license](https://img.shields.io/badge/license-MIT-green) ![ci](https://img.shields.io/badge/CI-GitHub%20Actions%20(build%20%2B%20self--test)-success)

---

## What it does

RAM Razor scans every running process, groups them into real **apps** (all processes of one executable = one app), protects everything Windows needs to survive, and gives you brutal one-click cleanup tools:

| Feature | Description |
|---|---|
| **CLOSE ALL APPS** | Force-closes **every** non-system app (full process tree). Only protected Windows system tasks survive. A watchdog then watches for 25 s and notes every app that **re-opens itself**, with an optional *auto re-kill* mode. |
| **Live RAM gauge** | Circular gauge + used/total/available + usage %. |
| **CLEAN RAM** | Working-set trim + optional **standby-list purge** (deep clean) using the same native technique as Mem Reduct. Shows exactly **how many MB / % of total RAM were freed**. Auto-clean every 10 min is optional. |
| **Close selected…** | Opens a checklist of all running apps/tasks → close what you tick. |
| **Close all but selected…** | Opens the same checklist → you pick the survivors, everything else dies. |
| **Force close selected (forever)** | Sends the ticked apps to the permanent **Blacklist**: closed now, killed on sight forever. |
| **Blacklist manager** | Remove entries (allow the app again) or clear the whole list. |
| **Miner detection** | Heuristic background **crypto-miner scanner**: known miner names, sustained high CPU with no window, live connections to mining-pool (Stratum) ports. Fires a warning card with **Force close (forever)** and **Force delete** buttons per app. |
| **Apps ↔ Tasks views** | *Apps* mode shows one row per app with all its related processes merged; *Tasks* mode lists every process individually. System tasks are hidden by default (toggle available). |
| **Activity log** | Every closed app, every failure (with reason), every re-opener, every clean and miner hit — in the app and in `%ProgramData%\RAMRazor\logs\app-YYYYMMDD.log`. |
| **Admin gate** | Without Administrator access the app **refuses to start** (error + exit code 740) — by design. |
| **Tray mode** | Minimizes to the system tray; double-click the tray icon to restore. |

Extra: failed-to-close or self-re-opening apps automatically pop up the warning card, so "the apps that can't be closed (open automatically)" are always surfaced with their force buttons.

---

## Download & run

Go to **[Releases](../../releases)** and pick:

| File | Size | Needs |
|---|---|---|
| `RAMRazor.exe` | ~40 MB | Nothing. .NET is embedded. **Just download and run.** |
| `RAMRazor-portable.exe` | ~1 MB | [.NET 8 Desktop Runtime](https://dotnet.microsoft.com/download/dotnet/8.0/runtime) (x64) |

> **Always start it with *Run as administrator*.**
> Without admin rights RAM Razor shows an error and does not open — it cannot do its job un-elevated.

### Quick start
1. Right-click the exe → **Run as administrator**.
2. Watch the gauge: it shows your live RAM usage %.
3. Press **CLOSE ALL APPS** when things get heavy — or tick apps in the list and use the selective buttons.
4. Press **CLEAN RAM** to squeeze out working-set + standby memory. The label under the gauge tells you exactly what was freed.
5. Check **Activity log** to see everything that was closed, everything that refused to die, and anything that tried to come back.

### Notes on safety
- Protected processes: kernel, services (session 0), `explorer`, shell hosts, Defender, audio, GPU driver containers, etc. — see `RAMRazor/Core/SystemWhitelist.cs`.
- *Force delete* never erases blindly: the executable is moved into `%ProgramData%\RAMRazor\quarantine\`; only if the file is locked it is scheduled for deletion at next reboot (Windows `MoveFileEx`).
- Everything destructive asks for confirmation first.

---

## Building from source

Requirements: **.NET 8 SDK** (any OS for compiling; a Windows machine for running).

```bash
# plain build
dotnet build RAMRazor/RAMRazor.csproj -c Release

# the exe you probably want: single file, no runtime needed
dotnet publish RAMRazor/RAMRazor.csproj -c Release -r win-x64 --self-contained true \
  -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true \
  -p:EnableCompressionInSingleFile=true -o out

# tiny portable variant (needs .NET 8 Desktop Runtime on the target PC)
dotnet publish RAMRazor/RAMRazor.csproj -c Release -r win-x64 --self-contained false \
  -p:PublishSingleFile=true -o out
```

### CI/CD (GitHub Actions)
`.github/workflows/build.yml` runs on every push to `main` and on every `v*` tag:

1. Builds in `Release`.
2. Publishes **both** exe variants (self-contained + portable).
3. **Actually runs the built exe** in headless self-test mode (`RAMRazor.exe --selftest`) on the Windows runner and fails the pipeline if any check fails — the report is uploaded as `selftest-result.txt` and attached to releases.
4. Tag pushes (`v1.0.0`, …) additionally publish a GitHub **Release** with the exes attached.

The self-test covers: system-whitelist classification, miner heuristics (positive + negative), blacklist persistence round-trip, log service round-trip, memory-statistics P/Invoke, live process enumeration, **spawning a real child process and force-closing it through the killer pipeline**, TCP-table parsing (miner plumbing), and privilege acquisition (tolerant on non-elevated runners).

### Local logic tests (works on Linux/macOS too)
```bash
dotnet run --project tests/TestLogic
```

---

## Project layout

```
RAMRazor/
  Program.cs               entry point, admin gate, single-instance, self-test dispatch
  SelfTest.cs              headless CI self-test (--selftest)
  Core/
    AppModels.cs           ProcInfo / AppGroup / KillReport / CleanResult / AlertItem
    SystemWhitelist.cs     protected Windows system tasks
    ProcessEnumerator.cs   snapshot + CPU deltas + app grouping (Apps/Tasks views)
    ProcessKiller.cs       graceful → taskkill /T /F → Kill(tree); quarantine helpers
    MemoryService.cs       RAM stats, working-set trim, standby purge, clean %
    MinerDetector.cs       miner heuristics + live TCP pool-port detection
    BlacklistStore.cs      persistent force-close-forever list (JSON)
    LogService.cs          activity log (file + in-app + events)
    Watchdog.cs            re-opener detection / auto re-kill / blacklist enforcement
    LogicSelfCheck.cs      shared platform-neutral test battery
  UI/
    MainForm(.Actions).cs  dashboard, gauge, list, all buttons & flows
    RamGauge.cs            circular usage gauge
    ProcessSelectForm.cs   "close selected" / "keep selected" checklist
    BlacklistForm.cs       blacklist manager
    MinerAlertForm.cs      warning card (miners / unkillable / re-openers)
    LogForm.cs             activity log viewer
    Theme.cs               dark theme helpers
```

---

## Credits — similar open-source projects that inspired this

RAM Razor is an original implementation, but it deliberately borrows proven *techniques* from these excellent open-source projects:

| Project | What was borrowed |
|---|---|
| **[Mem Reduct](https://github.com/henrypp/memreduct)** (Henry++, GPL-3.0) | The native memory-cleaning approach: `NtSetSystemInformation(SystemMemoryListInformation)` with `MemoryEmptyWorkingSets` + `MemoryPurgeStandbyList` and the required token privileges. |
| **[WinMemoryCleaner](https://github.com/IgorMundstein/WinMemoryCleaner)** (C#) | Confirmation that this technique is solid from managed C# code; privilege handling pattern. |
| **[System Informer (Process Hacker)](https://github.com/SystemInformer/SystemInformer)** (GPL-3.0) | Process-tree termination strategy, working-set trimming per process, general process-management UX ideas. |
| **[Sysinternals Process Explorer](https://learn.microsoft.com/sysinternals/downloads/process-explorer)** (free, not OSS) | The "group by executable" mental model behind the Apps view. |
| Miner-detection heuristics | Community knowledge of Stratum pool ports (3333/4444/5555/7777/14444/…) and known miner binary names (XMRig, NBMiner, T-Rex, …). |

No source code was copied from any of them — only public Windows API knowledge and ideas. Thank you, open source!

## License

MIT — see [LICENSE](LICENSE).
