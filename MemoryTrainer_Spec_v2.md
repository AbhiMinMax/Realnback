# MemoryTrainer — Complete Specification, Technical Guide & Development Plan (v2)

> **For Claude Code:** This document is your single source of truth. Build exactly what is described here. Do not add features not listed. Do not simplify features that are explicitly specified. Follow the architecture and development plan precisely. Work phase by phase — each phase must compile and run before proceeding to the next.

---

## 1. What This App Does

MemoryTrainer is a portable Windows desktop application that trains memory encoding and retrieval. The user configures one or more duration cycles (e.g. 15 min, 1 hour, 1 month). For each cycle, the app silently takes a screenshot at a scheduled random time within a configurable waiting window, plus additional proactive screenshots at user-defined decoy offsets around that time. After the exact duration elapses, the app prompts the user to recall what was on screen. The recall can involve free text entry, a recognition task (select the correct screenshot from options), or both — configured independently per duration cycle. The user evaluates their own free recall; recognition is evaluated automatically. Results are stored indefinitely. Cycles repeat automatically. The app is portable (not installed), survives shutdown, and restores in-progress sessions on relaunch.

---

## 2. Core Concepts & Terminology

| Term | Meaning |
|---|---|
| **Session** | A run started by the user with a set of configured duration cycles. Persists across shutdowns if multiday durations are present. |
| **SessionCycle** | One duration entry within a session (e.g. the "15 min cycle"). Each SessionCycle runs independently and loops continuously. |
| **Cycle** | One iteration of a SessionCycle — from start to evaluation. |
| **Waiting Window** | The time range after a cycle starts within which the main screenshot is taken at a random moment. |
| **Decoy Offset** | A user-configured time offset (positive or negative, in minutes) relative to the main screenshot time T, at which a decoy screenshot is proactively captured. |
| **T** | The scheduled time for the main recall screenshot within a cycle, calculated at cycle start. |
| **Free Recall** | The text-based recall mode where user writes what they remember before seeing options. |
| **Recognition Recall** | The screenshot-selection mode where user picks the correct screenshot from a set of options. |
| **Missed Cycle** | A cycle whose screenshot time passed while the app was closed. Logged but not evaluated. |

---

## 3. Full Feature Specification

### 3.1 Session Configuration Screen

The default screen on launch (when no active session exists).

**Duration Cycle Entries (repeatable, unlimited)**

User adds as many SessionCycles as desired. Each entry has:

- `Duration` — hours, days, and/or minutes. Supports any length from 1 minute to several months.
- `Waiting Window` — duration in minutes. Screenshot is taken at a random moment within this window from cycle start. Must be strictly less than the duration.
- `Drift` — optional ±N minutes applied to the duration on each repeat cycle. Default 0. Actual duration per cycle = base duration + random value in [-drift, +drift]. Minimum clamped to 1 minute.
- `Decoy Offsets` — user-defined list of time offsets (in minutes, positive or negative) relative to T at which decoy screenshots are proactively taken. User adds/removes offsets freely per SessionCycle. Example: -5, +5, -15, -30.
- `Free Recall` — toggle on/off. Default on.
- `Recognition Recall` — toggle on/off. Default on.
- Remove button per entry.

**Validation:**
- At least one SessionCycle required.
- Waiting window must be < duration.
- At least one of Free Recall or Recognition Recall must be on per entry.
- Start button disabled until all entries are valid.

**Session Name** — optional text field. Auto-generated if blank: "Session — {date} {time}".

**Start Session button** — begins all SessionCycles simultaneously.

**View History button** — navigates to History screen.

**Settings button** — opens Settings panel (see 3.8).

---

### 3.2 SessionCycle Behaviour

Each SessionCycle runs as an independent loop.

**On cycle start:**
1. Calculate T = cycle_start_time + random value within [0, WaitingWindow].
2. Schedule main screenshot capture at time T.
3. For each configured decoy offset O: schedule a decoy screenshot capture at T + O. If T + O is in the past at scheduling time, skip that decoy (log as unavailable).
4. Wait until T, capture main screenshot, save to disk, record in DB.
5. Wait until cycle_start_time + actual_duration (with drift applied).
6. Add prompt to the global conflict queue.
7. When prompt is shown and evaluated, immediately begin next cycle.
8. Apply drift to compute next cycle's actual duration.

**All times are stored as absolute UTC timestamps in DB, not as relative countdowns.** This is what enables restore after shutdown.

---

### 3.3 Screenshot Capture

- Full screen, primary monitor only.
- Saved to: `{AppFolder}\data\screenshots\{sessionId}\{cycleId}_{type}_{timestamp}.png`
  - type: `main` or `decoy`
- User never sees any indication that a screenshot is being taken.
- No notification, no tray flash, no sound.
- App folder = directory containing the .exe (portable app, not AppData).

---

### 3.4 Conflict Queue

When two or more SessionCycle prompts become due at the same time (within the same second):
- All are added to a global FIFO queue ordered by duration descending (longest first).
- Only one recall prompt window is open at a time.
- When user completes evaluation and closes a prompt, next item in queue is shown immediately.
- Nothing is ever skipped.

---

### 3.5 Recall Prompt Window

A separate always-on-top window that cannot be minimised. The X/close button is disabled — user must complete evaluation to dismiss.

**Header:** "What were you doing [X duration] ago?" e.g. "What were you doing 15 minutes ago?" or "What were you doing 2 days ago?"

---

#### 3.5.1 Free Recall (when enabled for this SessionCycle)

- Large multi-line scrollable text area.
- Instruction text: "Describe what you were doing AND what specific content was visible on screen."
- User types their recall before seeing any screenshots.
- "Verify" button — only appears after user has typed at least one character.
- Clicking Verify locks the text area (no further editing) and reveals the evaluation section below.

**Free Recall Evaluation (shown after Verify):**
- Clickable link labelled "Open Screenshot" — opens the main screenshot in default Windows image viewer via Process.Start. This is the only way user sees the screenshot.
- Three buttons: **Correct** / **Partial** / **Wrong**
  - Correct = right screen/app AND specific content recalled accurately
  - Partial = context right but content missed, or vice versa
  - Wrong = neither
- Tooltip/subtext showing these definitions.
- Clicking any button saves the free recall result and text.

---

#### 3.5.2 Recognition Recall (when enabled for this SessionCycle)

Shown below free recall section (if both on), or standalone (if free recall off).

- Instruction text: "Which of these was your screen [X duration] ago?"
- Shows N clickable links labelled by timestamp (e.g. "Screenshot A — 10:32:15", "Screenshot B — 10:28:44" etc). Each link opens that screenshot in default Windows image viewer via Process.Start.
- One of the N options is the correct main screenshot; the rest are decoys.
- Decoy pool: screenshots captured at the user-defined decoy offset times for this cycle. If a decoy offset screenshot is unavailable (was skipped or app was closed), substitute with a randomly selected screenshot from any available file in the screenshots folder.
- Minimum options shown: 2 (correct + at least 1 decoy). If no decoys at all are available, recognition section is skipped for this prompt and logged as unavailable.
- Options are shuffled randomly before display.
- User clicks "Select" next to their chosen option (or the link itself acts as select after a confirm step).
- Result: automatically Correct if main screenshot selected, Wrong otherwise. No Partial in recognition.
- Result saved immediately.

---

#### 3.5.3 Prompt Completion

- If both recall modes are on: both must be completed before the window can be dismissed.
- "Done" button appears only after all active recall modes are evaluated.
- Clicking Done closes the window. If items are queued, next prompt opens immediately.

---

### 3.6 Evaluations Are Always Separate

- Free recall result (Correct/Partial/Wrong + recall text) is one record.
- Recognition result (Correct/Wrong) is a separate record.
- They are stored independently, displayed independently in history, and aggregated independently.
- They are never combined into a single score.
- If a mode was off for a cycle, that record simply doesn't exist for that cycle — no nulls shown in history, just absent.

---

### 3.7 Pause / Resume / Stop

**Pause:**
- Available in tray menu and main window during active session.
- Freezes all SessionCycle timers at their exact elapsed point.
- All scheduled screenshot captures are cancelled and rescheduled on resume with remaining time.
- Screenshots already taken are preserved.
- Decoy screenshots already taken are preserved.
- Decoy screenshots not yet taken are rescheduled on resume with remaining time.
- Session state written to DB as Paused.

**Resume:**
- Continues all timers from their frozen point.
- Reschedules all pending captures.

**Stop:**
- Available in tray menu and main window.
- Confirmation dialog: "Stop session? All scores are saved."
- On confirm: all cycles cancelled, session marked complete in DB, app returns to config screen.
- Session can be stopped at any time including mid-cycle. Any in-progress cycle is marked as incomplete (not missed — incomplete is a separate status).

---

### 3.8 System Tray

- Tray icon always present while app is running.
- Closing main window hides it to tray — does not exit app.
- Double-clicking tray icon shows and focuses main window.

**Right-click tray menu:**
- Status line: "Active — 4 cycles running" / "Paused" / "No active session"
- Pause / Resume (toggles, greyed out if no session)
- Stop Session (greyed out if no session)
- Open App
- Quit

---

### 3.9 Session Persistence & Restore

**The app is portable** — runs from its own folder, stores all data in a `data\` subfolder next to the .exe. Nothing written to AppData or registry.

**Real-time persistence:** Every state change is written to DB immediately. No data is held only in memory. This includes:
- Cycle start time
- Scheduled screenshot time T
- Scheduled decoy times
- Screenshot captured (path + timestamp)
- Prompt shown time
- Evaluation results

**On launch, app checks for incomplete sessions in DB.**

If found, restore dialog is shown: "You have an active session from [date]. Resume it?"
- Yes → restore
- No → session marked as abandoned (treated like a Stop), app goes to config screen

**Restore logic per SessionCycle state:**

| State at shutdown | Restore behaviour |
|---|---|
| Waiting — T still in the future | Reschedule screenshot for remaining wait time |
| Waiting — T passed while closed | Mark cycle as Missed, log reason "App was closed", immediately start fresh cycle |
| Screenshot taken, prompt not yet due | If due time still in future: wait remaining time. If due time has passed: add to prompt queue immediately |
| Prompt was due or overdue | Add to prompt queue immediately on restore |
| Decoy scheduled, not yet taken, time still future | Reschedule for remaining wait |
| Decoy scheduled, time passed while closed | Mark that decoy as unavailable, use random fallback if needed at prompt time |

**Missed cycles:**
- Status: Missed
- Reason stored: "App was closed"
- No recall prompt shown
- No evaluation recorded
- Excluded from all score aggregations (Correct %, streaks, etc.)
- Visible in history only when "Show Missed" filter is explicitly enabled (off by default)

---

### 3.10 Screenshot Cleanup

- Runs after each successful evaluation (both modes completed, or only active mode completed).
- Cleanup scope is per SessionCycle independently.
- Rule: delete all screenshot files (main and decoy) for this SessionCycle whose timestamp < T − (4 × actual_duration_of_current_cycle).
- Missed cycles do not trigger cleanup.
- Incomplete cycles (stopped mid-cycle) do not trigger cleanup.
- The `data\screenshots\` folder itself is never deleted automatically.

**Manual cleanup:**
- "Delete All Screenshots" button in Settings.
- Confirmation dialog: "This will delete all screenshot files. Score history is not affected."
- Deletes entire `data\screenshots\` folder contents recursively.

---

### 3.11 History Screen

Accessible from config screen and tray menu.

#### Filters (always visible at top)

- **Date range:** Day / Week / Month / Year presets + custom date range picker
- **Duration slot:** multi-select dropdown populated from all distinct duration values ever used across all sessions. Duration slots matched by time value only (15 min in session 1 = 15 min in session 3).
- **Session:** optional filter to one specific session
- **Show Missed:** toggle, off by default
- **Recall mode:** All / Free Recall only / Recognition only
- **Sort by:** Date (default desc), Duration, Result

#### Aggregation Panel

Shown above the detail list. Granularity adapts to date filter:
- Day → per-hour breakdown
- Week → per-day breakdown
- Month → per-week breakdown
- Year → per-month breakdown
- Custom range → auto-selected granularity

Metrics per aggregation period, shown separately for Free Recall and Recognition:
- Total attempts
- Correct %
- Partial % (Free Recall only)
- Wrong %
- Streak: current consecutive Correct count

Bar/line chart (LiveCharts) showing accuracy trend over the aggregation periods. Free Recall and Recognition shown as separate series on same chart.

#### Detail List

Table of individual CycleRecords matching current filters. Columns:
- Date/Time of screenshot
- Session name
- Duration slot (e.g. "15 min")
- Cycle number within session
- Free Recall result (Correct/Partial/Wrong, or "—" if mode was off)
- Recognition result (Correct/Wrong, or "—" if mode was off)
- Free Recall text (truncated, click row to expand)
- Missed indicator (only visible when Show Missed is on)

Expanding a row shows:
- Full recall text
- No image link (screenshots may be deleted — never show image links in history)

#### Session Management

- Session list panel (sidebar or dropdown)
- Delete Session button — deletes score records and any remaining screenshot files for that session. Confirmation required.

---

### 3.12 Settings

- Delete All Screenshots button (with confirmation)
- Default session name format (text template)
- No other settings — all configuration is per-session at session creation time

---

## 4. Technical Stack

| Layer | Technology |
|---|---|
| Language | C# 12 |
| Framework | .NET 9 |
| UI Framework | WPF (Windows Presentation Foundation) |
| UI Pattern | MVVM using CommunityToolkit.Mvvm |
| Database | SQLite via Microsoft.Data.Sqlite |
| ORM | Dapper |
| Screenshots | System.Drawing.Common (Graphics.CopyFromScreen) |
| Charts | LiveChartsCore.SkiaSharpView.WPF |
| Scheduling | System.Threading.PeriodicTimer + Task-based async |
| Packaging | Single-file self-contained portable .exe |
| Target OS | Windows 10 / 11 |
| Target Framework | net9.0-windows |
| Storage location | `{exe folder}\data\` |

**NuGet packages:**
```
CommunityToolkit.Mvvm
Microsoft.Data.Sqlite
Dapper
System.Drawing.Common
LiveChartsCore.SkiaSharpView.WPF
```

---

## 5. Project Structure

```
MemoryTrainer/
├── MemoryTrainer.csproj
├── App.xaml
├── App.xaml.cs
│
├── Models/
│   ├── SessionModel.cs
│   ├── SessionCycleConfig.cs
│   ├── CycleRecord.cs
│   ├── ScreenshotRecord.cs
│   ├── FreeRecallResult.cs
│   ├── RecognitionResult.cs
│   ├── DecoyOffset.cs
│   └── Enums.cs               (CycleStatus, EvaluationResult, RecallMode)
│
├── ViewModels/
│   ├── MainViewModel.cs
│   ├── ConfigViewModel.cs
│   ├── SessionCycleConfigViewModel.cs
│   ├── ActiveSessionViewModel.cs
│   ├── CycleStatusViewModel.cs
│   ├── RecallPromptViewModel.cs
│   ├── HistoryViewModel.cs
│   └── SettingsViewModel.cs
│
├── Views/
│   ├── MainWindow.xaml / .cs
│   ├── ConfigView.xaml / .cs
│   ├── ActiveSessionView.xaml / .cs
│   ├── RecallPromptWindow.xaml / .cs
│   ├── HistoryView.xaml / .cs
│   └── SettingsView.xaml / .cs
│
├── Services/
│   ├── SessionEngine.cs
│   ├── CycleRunner.cs
│   ├── ScreenshotService.cs
│   ├── DatabaseService.cs
│   ├── TrayService.cs
│   └── CleanupService.cs
│
├── Helpers/
│   ├── TimeFormatter.cs
│   └── PathHelper.cs
│
└── Assets/
    └── tray_icon.ico
```

---

## 6. Data Models

### Enums
```csharp
public enum CycleStatus
{
    Pending,        // not yet started
    WaitingForScreenshot,
    ScreenshotTaken,
    PromptQueued,
    PromptShown,
    Completed,
    Missed,         // app was closed during waiting
    Incomplete      // session stopped before completion
}

public enum EvaluationResult
{
    Correct = 0,
    Partial = 1,    // free recall only
    Wrong = 2
}

public enum RecallMode
{
    Free = 0,
    Recognition = 1
}
```

### SessionModel
```csharp
public class SessionModel
{
    public int Id { get; set; }
    public string Name { get; set; }
    public DateTime StartedAtUtc { get; set; }
    public DateTime? EndedAtUtc { get; set; }
    public bool IsCompleted { get; set; }
    public bool IsPaused { get; set; }
    public DateTime? PausedAtUtc { get; set; }
}
```

### SessionCycleConfig
```csharp
public class SessionCycleConfig
{
    public int Id { get; set; }
    public int SessionId { get; set; }
    public long BaseDurationTicks { get; set; }       // TimeSpan.Ticks
    public long WaitingWindowTicks { get; set; }
    public int DriftMinutes { get; set; }
    public bool FreeRecallEnabled { get; set; }
    public bool RecognitionEnabled { get; set; }
    // Decoy offsets stored in separate table, linked by SessionCycleConfigId
}
```

### DecoyOffset
```csharp
public class DecoyOffset
{
    public int Id { get; set; }
    public int SessionCycleConfigId { get; set; }
    public int OffsetMinutes { get; set; }   // negative = before T, positive = after T
}
```

### CycleRecord
```csharp
public class CycleRecord
{
    public int Id { get; set; }
    public int SessionCycleConfigId { get; set; }
    public int CycleNumber { get; set; }             // 1-based per SessionCycle
    public long ActualDurationTicks { get; set; }    // after drift applied
    public DateTime CycleStartUtc { get; set; }
    public DateTime ScheduledScreenshotUtc { get; set; }   // T
    public DateTime? ScreenshotTakenUtc { get; set; }
    public DateTime? PromptDueUtc { get; set; }
    public DateTime? PromptShownUtc { get; set; }
    public DateTime? CompletedUtc { get; set; }
    public CycleStatus Status { get; set; }
    public string? MissedReason { get; set; }
}
```

### ScreenshotRecord
```csharp
public class ScreenshotRecord
{
    public int Id { get; set; }
    public int CycleRecordId { get; set; }
    public string FilePath { get; set; }
    public DateTime TakenAtUtc { get; set; }
    public bool IsMain { get; set; }         // false = decoy
    public int? OffsetMinutes { get; set; }  // null for main screenshot
    public bool IsDeleted { get; set; }      // soft-delete flag set after file removed
}
```

### FreeRecallResult
```csharp
public class FreeRecallResult
{
    public int Id { get; set; }
    public int CycleRecordId { get; set; }
    public string RecallText { get; set; }
    public EvaluationResult Result { get; set; }
    public DateTime EvaluatedAtUtc { get; set; }
}
```

### RecognitionResult
```csharp
public class RecognitionResult
{
    public int Id { get; set; }
    public int CycleRecordId { get; set; }
    public int SelectedScreenshotRecordId { get; set; }
    public int CorrectScreenshotRecordId { get; set; }
    public bool IsCorrect { get; set; }
    public DateTime EvaluatedAtUtc { get; set; }
}
```

---

## 7. Database Schema

**Location:** `{exe folder}\data\memorytrainer.db`

```sql
CREATE TABLE Sessions (
    Id          INTEGER PRIMARY KEY AUTOINCREMENT,
    Name        TEXT NOT NULL,
    StartedAtUtc TEXT NOT NULL,
    EndedAtUtc  TEXT,
    IsCompleted INTEGER NOT NULL DEFAULT 0,
    IsPaused    INTEGER NOT NULL DEFAULT 0,
    PausedAtUtc TEXT
);

CREATE TABLE SessionCycleConfigs (
    Id                  INTEGER PRIMARY KEY AUTOINCREMENT,
    SessionId           INTEGER NOT NULL,
    BaseDurationTicks   INTEGER NOT NULL,
    WaitingWindowTicks  INTEGER NOT NULL,
    DriftMinutes        INTEGER NOT NULL DEFAULT 0,
    FreeRecallEnabled   INTEGER NOT NULL DEFAULT 1,
    RecognitionEnabled  INTEGER NOT NULL DEFAULT 1,
    FOREIGN KEY (SessionId) REFERENCES Sessions(Id)
);

CREATE TABLE DecoyOffsets (
    Id                      INTEGER PRIMARY KEY AUTOINCREMENT,
    SessionCycleConfigId    INTEGER NOT NULL,
    OffsetMinutes           INTEGER NOT NULL,
    FOREIGN KEY (SessionCycleConfigId) REFERENCES SessionCycleConfigs(Id)
);

CREATE TABLE CycleRecords (
    Id                      INTEGER PRIMARY KEY AUTOINCREMENT,
    SessionCycleConfigId    INTEGER NOT NULL,
    CycleNumber             INTEGER NOT NULL,
    ActualDurationTicks     INTEGER NOT NULL,
    CycleStartUtc           TEXT NOT NULL,
    ScheduledScreenshotUtc  TEXT NOT NULL,
    ScreenshotTakenUtc      TEXT,
    PromptDueUtc            TEXT,
    PromptShownUtc          TEXT,
    CompletedUtc            TEXT,
    Status                  INTEGER NOT NULL DEFAULT 0,
    MissedReason            TEXT,
    FOREIGN KEY (SessionCycleConfigId) REFERENCES SessionCycleConfigs(Id)
);

CREATE TABLE ScreenshotRecords (
    Id              INTEGER PRIMARY KEY AUTOINCREMENT,
    CycleRecordId   INTEGER NOT NULL,
    FilePath        TEXT NOT NULL,
    TakenAtUtc      TEXT NOT NULL,
    IsMain          INTEGER NOT NULL DEFAULT 0,
    OffsetMinutes   INTEGER,
    IsDeleted       INTEGER NOT NULL DEFAULT 0,
    FOREIGN KEY (CycleRecordId) REFERENCES CycleRecords(Id)
);

CREATE TABLE FreeRecallResults (
    Id              INTEGER PRIMARY KEY AUTOINCREMENT,
    CycleRecordId   INTEGER NOT NULL UNIQUE,
    RecallText      TEXT NOT NULL,
    Result          INTEGER NOT NULL,
    EvaluatedAtUtc  TEXT NOT NULL,
    FOREIGN KEY (CycleRecordId) REFERENCES CycleRecords(Id)
);

CREATE TABLE RecognitionResults (
    Id                          INTEGER PRIMARY KEY AUTOINCREMENT,
    CycleRecordId               INTEGER NOT NULL UNIQUE,
    SelectedScreenshotRecordId  INTEGER NOT NULL,
    CorrectScreenshotRecordId   INTEGER NOT NULL,
    IsCorrect                   INTEGER NOT NULL,
    EvaluatedAtUtc              TEXT NOT NULL,
    FOREIGN KEY (CycleRecordId) REFERENCES CycleRecords(Id)
);

CREATE INDEX idx_cyclerecords_configid ON CycleRecords(SessionCycleConfigId);
CREATE INDEX idx_cyclerecords_status ON CycleRecords(Status);
CREATE INDEX idx_screenshots_cyclerecordid ON ScreenshotRecords(CycleRecordId);
CREATE INDEX idx_freerecall_cyclerecordid ON FreeRecallResults(CycleRecordId);
CREATE INDEX idx_recognition_cyclerecordid ON RecognitionResults(CycleRecordId);
```

---

## 8. Service Architecture

### 8.1 SessionEngine

Owns and coordinates all running SessionCycles for the active session.

```
Responsibilities:
- On StartSession(sessionConfig): persist session + configs to DB, instantiate one CycleRunner per SessionCycleConfig, start all runners
- Maintain global conflict queue (ConcurrentQueue<CycleRecord>)
- One RecallPromptWindow open at a time — when closed, dequeue next if present
- OnPause: call Pause() on all CycleRunners, update DB
- OnResume: call Resume() on all CycleRunners, update DB
- OnStop: cancel all CycleRunners, mark session complete in DB
- OnRestore(sessionId): load session + all cycle states from DB, reconstruct CycleRunners in correct state
```

### 8.2 CycleRunner

One instance per SessionCycleConfig. Runs an async loop.

```
State machine per cycle iteration:
  1. PENDING → compute T, compute drift for this cycle, compute PromptDueUtc
     → persist CycleRecord to DB with Status=WaitingForScreenshot
  2. Wait until T (using absolute UTC, not countdown — supports restore)
     → capture main screenshot → persist ScreenshotRecord → update CycleRecord.ScreenshotTakenUtc, Status=ScreenshotTaken
  3. At each scheduled decoy time: capture decoy screenshot → persist ScreenshotRecord
     (concurrent with step 2–4, independent tasks per offset)
  4. Wait until PromptDueUtc
     → update CycleRecord.Status=PromptQueued
     → call SessionEngine.EnqueuePrompt(cycleRecord)
  5. When evaluation completes (callback from RecallPromptViewModel):
     → persist results → update Status=Completed → call CleanupService → start next iteration

Pause implementation:
  - CancellationToken passed to all waits
  - On pause: cancel token, record remaining time for each wait as (target_utc - DateTime.UtcNow)
  - On resume: new token, restart waits with recorded remaining durations

Restore implementation:
  - Load CycleRecord from DB
  - Determine state from Status field
  - Resume from correct point using stored UTC timestamps
  - If ScheduledScreenshotUtc < DateTime.UtcNow and Status=WaitingForScreenshot → mark Missed, start new cycle
  - If PromptDueUtc < DateTime.UtcNow and Status=ScreenshotTaken → enqueue prompt immediately
```

### 8.3 ScreenshotService

```csharp
public class ScreenshotService
{
    private readonly string _screenshotsBasePath;

    public string Capture(int sessionId, int cycleRecordId, bool isMain, int? offsetMinutes)
    {
        var filename = $"{cycleRecordId}_{(isMain ? "main" : $"decoy_{offsetMinutes}")}_{DateTime.UtcNow:yyyyMMddHHmmss}.png";
        var dir = Path.Combine(_screenshotsBasePath, sessionId.ToString());
        Directory.CreateDirectory(dir);
        var fullPath = Path.Combine(dir, filename);

        var screen = System.Windows.Forms.Screen.PrimaryScreen!.Bounds;
        using var bmp = new System.Drawing.Bitmap(screen.Width, screen.Height);
        using var g = System.Drawing.Graphics.FromImage(bmp);
        g.CopyFromScreen(screen.Left, screen.Top, 0, 0, screen.Size);
        bmp.Save(fullPath, System.Drawing.Imaging.ImageFormat.Png);
        return fullPath;
    }
}
```

### 8.4 DatabaseService

Wraps all Dapper queries. Methods include:

```
Session: CreateSession, GetIncompleteSession, UpdateSessionPaused, CompleteSession
SessionCycleConfig: CreateConfig, GetConfigsBySession, GetDecoyOffsets
CycleRecord: CreateCycleRecord, UpdateCycleRecord, GetCycleRecord, GetCyclesByConfig
ScreenshotRecord: CreateScreenshotRecord, GetScreenshotsByCycle, GetAllScreenshotPaths, MarkScreenshotDeleted, GetRandomAvailableScreenshot
FreeRecallResult: CreateFreeRecallResult, GetFreeRecallByCycle
RecognitionResult: CreateRecognitionResult, GetRecognitionByCycle
History queries: GetFilteredCycleRecords(filters), GetAggregatedStats(filters)
```

All methods are async. Use `using var connection = new SqliteConnection(_connectionString)` per call.

### 8.5 CleanupService

```
OnEvaluationComplete(cycleRecord, actualDurationTicks):
  cutoffUtc = cycleRecord.ScreenshotTakenUtc - TimeSpan.FromTicks(4 * actualDurationTicks)
  screenshots = DatabaseService.GetScreenshotsByCycleConfig(cycleRecord.SessionCycleConfigId)
                .Where(s => s.TakenAtUtc < cutoffUtc && !s.IsDeleted)
  foreach screenshot:
    if File.Exists(screenshot.FilePath): File.Delete(screenshot.FilePath)
    DatabaseService.MarkScreenshotDeleted(screenshot.Id)
```

### 8.6 TrayService

```
- System.Windows.Forms.NotifyIcon
- Icon: loaded from Assets/tray_icon.ico embedded resource
- Context menu rebuilt on every right-click (reads current session state from SessionEngine)
- Double-click: MainWindow.Show() + Activate()
- MainWindow.Closing: e.Cancel = true; MainWindow.Hide()
- Quit menu item: SessionEngine.Stop() if active, then Application.Current.Shutdown()
- Dispose NotifyIcon on app exit
```

---

## 9. UI Screens Detail

### 9.1 Config Screen

- Title bar: "MemoryTrainer"
- Top right: History button, Settings button
- "Duration Cycles" section header
- Scrollable list of SessionCycle entry rows. Each row:
  - Duration: two spinners (days + hours + minutes combined — use a custom input or three separate integer inputs)
  - Waiting Window: integer spinner (minutes) with label
  - Drift: integer spinner (minutes, default 0)
  - Free Recall toggle (labelled "Free Recall")
  - Recognition toggle (labelled "Recognition")
  - "Decoy Offsets" expandable sub-row: list of offset entries (integer field in minutes, +/- sign selector, Remove button per entry, Add Offset button)
  - Remove cycle button (X) on right
- "Add Duration Cycle" button below list
- Session Name text input
- "Start Session" primary button (bottom, full width, disabled when invalid)

### 9.2 Active Session Screen

Replaces config view content in main window after Start is clicked.

- Session name + start time
- Scrollable list of cycle status cards, one per SessionCycle:
  - Duration label (e.g. "15 min cycle")
  - Current cycle number (e.g. "Cycle 4")
  - Status description (e.g. "Waiting for screenshot — 3m 22s remaining", "Screenshot taken — prompt in 11m 48s", "Prompt queued")
  - Countdown timer (updates every second via DispatcherTimer)
- Queued prompts indicator: "X prompt(s) waiting"
- Pause / Resume button
- Stop Session button

### 9.3 Recall Prompt Window

- Separate Window, Topmost=True, WindowStyle=ToolWindow, ResizeMode=CanResize
- No X button (set in code-behind: hide close button via WinAPI or intercept Closing event)
- Header: "What were you doing {formatted duration} ago?"
- If Free Recall enabled:
  - Multi-line TextBox, scrollable, min height 120px
  - Instructional subtext below
  - "Verify" button (disabled until text entered, disappears after clicked)
  - After Verify: lock text box, show "Open Screenshot" link + Correct/Partial/Wrong buttons
- If Recognition enabled:
  - "Which of these was your screen {duration} ago?" label
  - List of N options, each: label (e.g. "Option A — {time}") + "Open" link + "Select" button
  - After selection: show result (tick/cross) next to selected option
- "Done" button: appears only when all active modes evaluated, closes window

### 9.4 History Screen

- Back button (top left)
- Filter bar (top): date preset buttons, duration slot multi-select, session dropdown, show missed toggle, recall mode filter, sort dropdown
- Aggregation panel: metric cards (total, Correct %, Partial %, Wrong %, streak) + LiveCharts bar chart
  - Two series: Free Recall accuracy, Recognition accuracy
- Detail table below: virtualized (VirtualizingStackPanel) for performance with large datasets
- Expandable rows: full recall text (no image links ever)
- Delete Session button in session dropdown or sidebar

### 9.5 Settings Screen

- "Delete All Screenshots" button
  - On click: confirmation dialog → delete `data\screenshots\` folder contents → show success message
- Default session name template field

---

## 10. App Entry Point & Lifecycle

```csharp
// App.xaml.cs
protected override void OnStartup(StartupEventArgs e)
{
    base.OnStartup(e);

    // 1. Ensure data directory exists next to exe
    PathHelper.EnsureDataDirectories();

    // 2. Initialise database (create tables if not exist)
    var db = new DatabaseService(PathHelper.DatabasePath);
    db.InitialiseSchema();

    // 3. Build service instances (manual DI, no container needed)
    var screenshotService = new ScreenshotService(PathHelper.ScreenshotsPath);
    var cleanupService = new CleanupService(db);
    var engine = new SessionEngine(db, screenshotService, cleanupService);

    // 4. Create and show main window
    var mainVm = new MainViewModel(engine, db);
    var mainWindow = new MainWindow { DataContext = mainVm };
    MainWindow = mainWindow;

    // 5. Initialise tray
    var trayService = new TrayService(engine, mainWindow);

    // 6. Check for incomplete session
    var incompleteSession = db.GetIncompleteSession();
    if (incompleteSession != null)
        mainVm.OfferSessionRestore(incompleteSession);
    else
        mainVm.ShowConfig();

    mainWindow.Show();
}

protected override void OnExit(ExitEventArgs e)
{
    // Tray dispose, engine stop if running
    base.OnExit(e);
}
```

### PathHelper
```csharp
public static class PathHelper
{
    public static string AppFolder => AppContext.BaseDirectory;
    public static string DataFolder => Path.Combine(AppFolder, "data");
    public static string DatabasePath => Path.Combine(DataFolder, "memorytrainer.db");
    public static string ScreenshotsPath => Path.Combine(DataFolder, "screenshots");

    public static void EnsureDataDirectories()
    {
        Directory.CreateDirectory(DataFolder);
        Directory.CreateDirectory(ScreenshotsPath);
    }
}
```

---

## 11. .csproj

```xml
<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup>
    <OutputType>WinExe</OutputType>
    <TargetFramework>net9.0-windows</TargetFramework>
    <UseWPF>true</UseWPF>
    <UseWindowsForms>true</UseWindowsForms>
    <Nullable>enable</Nullable>
    <ImplicitUsings>enable</ImplicitUsings>
    <ApplicationIcon>Assets\tray_icon.ico</ApplicationIcon>
    <AssemblyName>MemoryTrainer</AssemblyName>
    <RootNamespace>MemoryTrainer</RootNamespace>
    <AllowUnsafeBlocks>false</AllowUnsafeBlocks>
  </PropertyGroup>

  <ItemGroup>
    <PackageReference Include="CommunityToolkit.Mvvm" Version="8.*" />
    <PackageReference Include="Microsoft.Data.Sqlite" Version="9.*" />
    <PackageReference Include="Dapper" Version="2.*" />
    <PackageReference Include="System.Drawing.Common" Version="9.*" />
    <PackageReference Include="LiveChartsCore.SkiaSharpView.WPF" Version="2.*" />
  </ItemGroup>

  <ItemGroup>
    <Resource Include="Assets\tray_icon.ico" />
  </ItemGroup>
</Project>
```

---

## 12. Development Plan

Build in this exact order. Each phase must compile and run before starting the next.

### Phase 1 — Foundation & Storage
1. Create solution, project, .csproj as specified
2. Implement PathHelper
3. Create all model classes and enums
4. Implement DatabaseService with full schema creation and all CRUD methods
5. Write a console-style smoke test in OnStartup: create a session, add a config, add a cycle record, query it back, print to debug output
6. Implement ScreenshotService
7. Smoke test screenshot: call Capture(), verify file exists and is a valid PNG

### Phase 2 — CycleRunner & SessionEngine (no UI)
1. Implement CycleRunner as a standalone async class with state machine
2. Implement single cycle: wait → screenshot → wait → raise PromptReady event
3. Add decoy scheduling alongside main screenshot wait
4. Add Pause/Resume with remaining-time tracking
5. Add drift calculation
6. Implement SessionEngine: start multiple CycleRunners, conflict queue, dequeue on evaluation
7. Test with two cycles (1 min and 2 min), verify events fire in correct order with correct conflict queue behaviour
8. Implement Restore logic in CycleRunner: all 4 state cases
9. Implement CleanupService
10. Test cleanup: verify files deleted after evaluation, files within retention window preserved

### Phase 3 — Config UI
1. Build MainWindow with navigation region (ContentControl bound to current view)
2. Build ConfigView with dynamic SessionCycle entry list
3. Implement SessionCycleConfigViewModel with validation
4. Decoy offset sub-list per cycle entry (expandable)
5. All toggles, spinners, validation
6. Wire Start button: persist to DB via DatabaseService, hand off to SessionEngine, navigate to ActiveSessionView

### Phase 4 — Active Session UI
1. Build ActiveSessionView
2. CycleStatusViewModel per running cycle with live countdown (DispatcherTimer, 1s interval)
3. Status descriptions driven by CycleRunner state changes via events
4. Queued prompt count indicator
5. Pause/Resume button wired to SessionEngine
6. Stop button with confirmation dialog

### Phase 5 — Recall Prompt Window
1. Build RecallPromptWindow (separate Window, Topmost)
2. Suppress close button (intercept WM_SYSCOMMAND in code-behind)
3. Free recall section: text input, Verify button, post-verify lock + link + evaluation buttons
4. Recognition section: option list with Open links and Select buttons
5. Decoy assembly logic in RecallPromptViewModel: query pool, pick by offset proximity, random fallback for gaps
6. Done button logic
7. Wire evaluation callbacks to SessionEngine → DatabaseService → CleanupService

### Phase 6 — System Tray
1. Implement TrayService
2. Window close → hide to tray
3. Tray menu dynamic state
4. All menu actions wired

### Phase 7 — Session Restore
1. On launch: query for incomplete session
2. Restore dialog
3. Reconstruct SessionEngine + CycleRunners from DB state
4. Navigate directly to ActiveSessionView on restore
5. Test: start session, kill process, relaunch, verify correct restore for each state case

### Phase 8 — History Screen
1. Build HistoryView with filter bar
2. DatabaseService history query with all filters applied in SQL (not in-memory)
3. Aggregation query with time-bucket grouping
4. LiveCharts chart with two series
5. Detail table with row expansion
6. Missed filter (off by default)
7. Delete session functionality

### Phase 9 — Settings Screen
1. Build SettingsView
2. Delete All Screenshots with confirmation
3. Navigate to/from settings

### Phase 10 — Polish & Packaging
1. Consistent WPF styling throughout (clean, minimal, dark or light — pick one and apply globally via App.xaml ResourceDictionary)
2. All error cases handled gracefully (missing screenshot file, DB locked, write permission error)
3. Error logging to `data\error.log` for unhandled exceptions
4. Edge case: decoy offset fires before cycle start (negative offset too large) — skip silently
5. Edge case: two SessionCycles with same duration — treated independently, both run
6. Edge case: Stop pressed while prompt window is open — close prompt window first, then stop
7. Publish command:
   ```
   dotnet publish -c Release -r win-x64 --self-contained true -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true
   ```
8. Test on a clean Windows 10/11 machine with no .NET installed — verify single exe runs, creates data folder, all features work

---

## 13. Edge Cases Reference

| Scenario | Handling |
|---|---|
| Both Free Recall and Recognition off on a cycle entry | Config validation blocks Start — invalid state |
| Waiting window ≥ duration | Config validation blocks Start |
| Drift causes duration ≤ 0 | Clamp actual duration to 1 minute minimum |
| Decoy offset fires before cycle start time | Skip that decoy silently, mark as unavailable |
| No decoys available at recognition time | Pick randomly from any file in screenshots folder |
| No screenshots at all available | Skip recognition section for this prompt, log as unavailable |
| Two cycles with identical durations | Run completely independently, no special handling |
| Stop pressed while prompt window open | Close prompt window (mark cycle incomplete), then stop session |
| Screenshot file missing at prompt time | Show "Open Screenshot" link as disabled with label "File unavailable" |
| DB locked (concurrent write) | Retry 3 times with 50ms delay, then log error and continue |
| App crash mid-session | On next launch, incomplete session detected via IsCompleted=0, restore offered |
| Multiday duration, app closed and reopened many times | Each launch checks UTC timestamps — correct remaining time always computed |
| Session with only multiday durations — user force-quits Windows | Same as normal closure — restore on next launch |
| Recognition: selected screenshot file deleted before prompt fires | Substitute with random available screenshot for that option slot |
| History query with no results | Show empty state message, no chart rendered |

---

## 14. What NOT to Build

- No cloud sync or remote storage
- No user accounts or profiles
- No audio or visual alerts other than the prompt window coming to foreground
- No OCR or automatic content analysis of screenshots
- No installer — portable exe only
- No auto-update
- No sharing or export features
- No network access of any kind

---

*End of specification — v2*
