using Dapper;
using MemoryTrainer.Models;
using Microsoft.Data.Sqlite;

namespace MemoryTrainer.Services;

public class DatabaseService
{
    private readonly string _connectionString;

    public DatabaseService(string dbPath)
    {
        _connectionString = $"Data Source={dbPath}";
    }

    private SqliteConnection OpenConnection() => new SqliteConnection(_connectionString);

    public void InitialiseSchema()
    {
        using var conn = OpenConnection();
        conn.Open();
        conn.Execute(@"
            CREATE TABLE IF NOT EXISTS Sessions (
                Id          INTEGER PRIMARY KEY AUTOINCREMENT,
                Name        TEXT NOT NULL,
                StartedAtUtc TEXT NOT NULL,
                EndedAtUtc  TEXT,
                IsCompleted INTEGER NOT NULL DEFAULT 0,
                IsPaused    INTEGER NOT NULL DEFAULT 0,
                PausedAtUtc TEXT
            );

            CREATE TABLE IF NOT EXISTS SessionCycleConfigs (
                Id                  INTEGER PRIMARY KEY AUTOINCREMENT,
                SessionId           INTEGER NOT NULL,
                BaseDurationTicks   INTEGER NOT NULL,
                WaitingWindowTicks  INTEGER NOT NULL,
                DriftMinutes        INTEGER NOT NULL DEFAULT 0,
                FreeRecallEnabled   INTEGER NOT NULL DEFAULT 1,
                RecognitionEnabled  INTEGER NOT NULL DEFAULT 1,
                AudioEnabled        INTEGER NOT NULL DEFAULT 0,
                CameraEnabled       INTEGER NOT NULL DEFAULT 0,
                FOREIGN KEY (SessionId) REFERENCES Sessions(Id)
            );

            CREATE TABLE IF NOT EXISTS DecoyOffsets (
                Id                      INTEGER PRIMARY KEY AUTOINCREMENT,
                SessionCycleConfigId    INTEGER NOT NULL,
                OffsetMinutes           INTEGER NOT NULL,
                FOREIGN KEY (SessionCycleConfigId) REFERENCES SessionCycleConfigs(Id)
            );

            CREATE TABLE IF NOT EXISTS CycleRecords (
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

            CREATE TABLE IF NOT EXISTS ScreenshotRecords (
                Id              INTEGER PRIMARY KEY AUTOINCREMENT,
                CycleRecordId   INTEGER NOT NULL,
                FilePath        TEXT NOT NULL,
                TakenAtUtc      TEXT NOT NULL,
                IsMain          INTEGER NOT NULL DEFAULT 0,
                OffsetMinutes   INTEGER,
                IsDeleted       INTEGER NOT NULL DEFAULT 0,
                FOREIGN KEY (CycleRecordId) REFERENCES CycleRecords(Id)
            );

            CREATE TABLE IF NOT EXISTS CaptureRecords (
                Id                  INTEGER PRIMARY KEY AUTOINCREMENT,
                CycleRecordId       INTEGER NOT NULL,
                Type                INTEGER NOT NULL,
                IsMain              INTEGER NOT NULL DEFAULT 1,
                DecoyOffsetMinutes  INTEGER,
                FilePath            TEXT,
                TakenAtUtc          TEXT,
                Availability        INTEGER NOT NULL DEFAULT 0,
                IsDeleted           INTEGER NOT NULL DEFAULT 0,
                FOREIGN KEY (CycleRecordId) REFERENCES CycleRecords(Id)
            );

            CREATE TABLE IF NOT EXISTS FreeRecallResults (
                Id              INTEGER PRIMARY KEY AUTOINCREMENT,
                CycleRecordId   INTEGER NOT NULL UNIQUE,
                RecallText      TEXT NOT NULL,
                Result          INTEGER NOT NULL,
                EvaluatedAtUtc  TEXT NOT NULL,
                FOREIGN KEY (CycleRecordId) REFERENCES CycleRecords(Id)
            );

            CREATE TABLE IF NOT EXISTS RecognitionResults (
                Id                          INTEGER PRIMARY KEY AUTOINCREMENT,
                CycleRecordId               INTEGER NOT NULL UNIQUE,
                SelectedScreenshotRecordId  INTEGER NOT NULL,
                CorrectScreenshotRecordId   INTEGER NOT NULL,
                IsCorrect                   INTEGER NOT NULL,
                EvaluatedAtUtc              TEXT NOT NULL,
                FOREIGN KEY (CycleRecordId) REFERENCES CycleRecords(Id)
            );

            CREATE TABLE IF NOT EXISTS AudioRecallResults (
                Id              INTEGER PRIMARY KEY AUTOINCREMENT,
                CycleRecordId   INTEGER NOT NULL UNIQUE,
                RecallText      TEXT NOT NULL,
                Result          INTEGER NOT NULL,
                EvaluatedAtUtc  TEXT NOT NULL,
                FOREIGN KEY (CycleRecordId) REFERENCES CycleRecords(Id)
            );

            CREATE TABLE IF NOT EXISTS CameraRecallResults (
                Id              INTEGER PRIMARY KEY AUTOINCREMENT,
                CycleRecordId   INTEGER NOT NULL UNIQUE,
                RecallText      TEXT NOT NULL,
                Result          INTEGER NOT NULL,
                EvaluatedAtUtc  TEXT NOT NULL,
                FOREIGN KEY (CycleRecordId) REFERENCES CycleRecords(Id)
            );

            CREATE INDEX IF NOT EXISTS idx_cyclerecords_configid ON CycleRecords(SessionCycleConfigId);
            CREATE INDEX IF NOT EXISTS idx_cyclerecords_status ON CycleRecords(Status);
            CREATE INDEX IF NOT EXISTS idx_screenshots_cyclerecordid ON ScreenshotRecords(CycleRecordId);
            CREATE INDEX IF NOT EXISTS idx_capturerecords_cyclerecordid ON CaptureRecords(CycleRecordId);
            CREATE INDEX IF NOT EXISTS idx_freerecall_cyclerecordid ON FreeRecallResults(CycleRecordId);
            CREATE INDEX IF NOT EXISTS idx_recognition_cyclerecordid ON RecognitionResults(CycleRecordId);
            CREATE INDEX IF NOT EXISTS idx_audiorecall_cyclerecordid ON AudioRecallResults(CycleRecordId);
            CREATE INDEX IF NOT EXISTS idx_camerarecall_cyclerecordid ON CameraRecallResults(CycleRecordId);
        ");

        // Migrations for existing DBs — ignore if column already exists
        TryMigrate(conn, "ALTER TABLE SessionCycleConfigs ADD COLUMN AudioEnabled INTEGER NOT NULL DEFAULT 0");
        TryMigrate(conn, "ALTER TABLE SessionCycleConfigs ADD COLUMN CameraEnabled INTEGER NOT NULL DEFAULT 0");
    }

    private static void TryMigrate(SqliteConnection conn, string sql)
    {
        try { conn.Execute(sql); }
        catch (SqliteException) { }
    }

    private async Task<T> RetryAsync<T>(Func<Task<T>> action)
    {
        for (int attempt = 0; attempt < 3; attempt++)
        {
            try { return await action(); }
            catch (SqliteException ex) when (attempt < 2)
            {
                System.Diagnostics.Debug.WriteLine($"[DatabaseService] DB retry {attempt + 1}: {ex.Message}");
                await Task.Delay(50);
            }
        }
        return await action();
    }

    private async Task RetryAsync(Func<Task> action)
    {
        await RetryAsync(async () => { await action(); return 0; });
    }

    // ── Sessions ──

    public async Task<int> CreateSessionAsync(SessionModel session)
    {
        return await RetryAsync(async () =>
        {
            using var conn = OpenConnection();
            return await conn.ExecuteScalarAsync<int>(@"
                INSERT INTO Sessions (Name, StartedAtUtc, IsCompleted, IsPaused)
                VALUES (@Name, @StartedAtUtc, 0, 0);
                SELECT last_insert_rowid();",
                new { session.Name, StartedAtUtc = session.StartedAtUtc.ToString("O") });
        });
    }

    public async Task<SessionModel?> GetIncompleteSessionAsync()
    {
        using var conn = OpenConnection();
        var row = await conn.QueryFirstOrDefaultAsync(
            "SELECT * FROM Sessions WHERE IsCompleted = 0 ORDER BY StartedAtUtc DESC LIMIT 1");
        if (row == null) return null;
        return MapSession(row);
    }

    public async Task UpdateSessionPausedAsync(int sessionId, bool paused)
    {
        await RetryAsync(async () =>
        {
            using var conn = OpenConnection();
            await conn.ExecuteAsync(@"
                UPDATE Sessions SET IsPaused = @Paused, PausedAtUtc = @PausedAt WHERE Id = @Id",
                new { Id = sessionId, Paused = paused ? 1 : 0, PausedAt = paused ? DateTime.UtcNow.ToString("O") : null as string });
        });
    }

    public async Task CompleteSessionAsync(int sessionId)
    {
        await RetryAsync(async () =>
        {
            using var conn = OpenConnection();
            await conn.ExecuteAsync(@"
                UPDATE Sessions SET IsCompleted = 1, EndedAtUtc = @EndedAt WHERE Id = @Id",
                new { Id = sessionId, EndedAt = DateTime.UtcNow.ToString("O") });
        });
    }

    public async Task AbandonSessionAsync(int sessionId)
    {
        await CompleteSessionAsync(sessionId);
    }

    public async Task<List<SessionModel>> GetAllSessionsAsync()
    {
        using var conn = OpenConnection();
        var rows = await conn.QueryAsync("SELECT * FROM Sessions ORDER BY StartedAtUtc DESC");
        return rows.Select(r => (SessionModel)MapSession(r)).ToList();
    }

    public async Task<SessionModel?> GetSessionAsync(int sessionId)
    {
        using var conn = OpenConnection();
        var row = await conn.QueryFirstOrDefaultAsync("SELECT * FROM Sessions WHERE Id = @Id", new { Id = sessionId });
        if (row == null) return null;
        return MapSession(row);
    }

    public async Task DeleteSessionAsync(int sessionId)
    {
        await RetryAsync(async () =>
        {
            using var conn = OpenConnection();
            var cycleIds = @"SELECT Id FROM CycleRecords WHERE SessionCycleConfigId IN
                             (SELECT Id FROM SessionCycleConfigs WHERE SessionId = @Id)";
            await conn.ExecuteAsync($"DELETE FROM AudioRecallResults WHERE CycleRecordId IN ({cycleIds})", new { Id = sessionId });
            await conn.ExecuteAsync($"DELETE FROM CameraRecallResults WHERE CycleRecordId IN ({cycleIds})", new { Id = sessionId });
            await conn.ExecuteAsync($"DELETE FROM RecognitionResults WHERE CycleRecordId IN ({cycleIds})", new { Id = sessionId });
            await conn.ExecuteAsync($"DELETE FROM FreeRecallResults WHERE CycleRecordId IN ({cycleIds})", new { Id = sessionId });
            await conn.ExecuteAsync($"DELETE FROM CaptureRecords WHERE CycleRecordId IN ({cycleIds})", new { Id = sessionId });
            await conn.ExecuteAsync($"DELETE FROM ScreenshotRecords WHERE CycleRecordId IN ({cycleIds})", new { Id = sessionId });
            await conn.ExecuteAsync($"DELETE FROM CycleRecords WHERE SessionCycleConfigId IN (SELECT Id FROM SessionCycleConfigs WHERE SessionId = @Id)", new { Id = sessionId });
            await conn.ExecuteAsync("DELETE FROM DecoyOffsets WHERE SessionCycleConfigId IN (SELECT Id FROM SessionCycleConfigs WHERE SessionId = @Id)", new { Id = sessionId });
            await conn.ExecuteAsync("DELETE FROM SessionCycleConfigs WHERE SessionId = @Id", new { Id = sessionId });
            await conn.ExecuteAsync("DELETE FROM Sessions WHERE Id = @Id", new { Id = sessionId });
        });
    }

    private static SessionModel MapSession(dynamic row)
    {
        return new SessionModel
        {
            Id = (int)row.Id,
            Name = (string)row.Name,
            StartedAtUtc = DateTime.Parse((string)row.StartedAtUtc, null, System.Globalization.DateTimeStyles.RoundtripKind),
            EndedAtUtc = row.EndedAtUtc != null ? DateTime.Parse((string)row.EndedAtUtc, null, System.Globalization.DateTimeStyles.RoundtripKind) : null,
            IsCompleted = (long)row.IsCompleted == 1,
            IsPaused = (long)row.IsPaused == 1,
            PausedAtUtc = row.PausedAtUtc != null ? DateTime.Parse((string)row.PausedAtUtc, null, System.Globalization.DateTimeStyles.RoundtripKind) : null,
        };
    }

    // ── SessionCycleConfigs ──

    public async Task<int> CreateConfigAsync(SessionCycleConfig config)
    {
        return await RetryAsync(async () =>
        {
            using var conn = OpenConnection();
            return await conn.ExecuteScalarAsync<int>(@"
                INSERT INTO SessionCycleConfigs
                    (SessionId, BaseDurationTicks, WaitingWindowTicks, DriftMinutes,
                     FreeRecallEnabled, RecognitionEnabled, AudioEnabled, CameraEnabled)
                VALUES
                    (@SessionId, @BaseDurationTicks, @WaitingWindowTicks, @DriftMinutes,
                     @FreeRecallEnabled, @RecognitionEnabled, @AudioEnabled, @CameraEnabled);
                SELECT last_insert_rowid();",
                new
                {
                    config.SessionId,
                    config.BaseDurationTicks,
                    config.WaitingWindowTicks,
                    config.DriftMinutes,
                    FreeRecallEnabled = config.FreeRecallEnabled ? 1 : 0,
                    RecognitionEnabled = config.RecognitionEnabled ? 1 : 0,
                    AudioEnabled = config.AudioEnabled ? 1 : 0,
                    CameraEnabled = config.CameraEnabled ? 1 : 0,
                });
        });
    }

    public async Task<List<SessionCycleConfig>> GetConfigsBySessionAsync(int sessionId)
    {
        using var conn = OpenConnection();
        var rows = await conn.QueryAsync("SELECT * FROM SessionCycleConfigs WHERE SessionId = @SessionId", new { SessionId = sessionId });
        return rows.Select(r => new SessionCycleConfig
        {
            Id = (int)r.Id,
            SessionId = (int)r.SessionId,
            BaseDurationTicks = (long)r.BaseDurationTicks,
            WaitingWindowTicks = (long)r.WaitingWindowTicks,
            DriftMinutes = (int)r.DriftMinutes,
            FreeRecallEnabled = (long)r.FreeRecallEnabled == 1,
            RecognitionEnabled = (long)r.RecognitionEnabled == 1,
            AudioEnabled = (long)r.AudioEnabled == 1,
            CameraEnabled = (long)r.CameraEnabled == 1,
        }).ToList();
    }

    public async Task<List<DecoyOffset>> GetDecoyOffsetsAsync(int configId)
    {
        using var conn = OpenConnection();
        var rows = await conn.QueryAsync("SELECT * FROM DecoyOffsets WHERE SessionCycleConfigId = @ConfigId", new { ConfigId = configId });
        return rows.Select(r => new DecoyOffset
        {
            Id = (int)r.Id,
            SessionCycleConfigId = (int)r.SessionCycleConfigId,
            OffsetMinutes = (int)r.OffsetMinutes,
        }).ToList();
    }

    public async Task CreateDecoyOffsetAsync(DecoyOffset offset)
    {
        await RetryAsync(async () =>
        {
            using var conn = OpenConnection();
            await conn.ExecuteAsync(@"
                INSERT INTO DecoyOffsets (SessionCycleConfigId, OffsetMinutes)
                VALUES (@SessionCycleConfigId, @OffsetMinutes)",
                new { offset.SessionCycleConfigId, offset.OffsetMinutes });
        });
    }

    // ── CycleRecords ──

    public async Task<int> CreateCycleRecordAsync(CycleRecord record)
    {
        return await RetryAsync(async () =>
        {
            using var conn = OpenConnection();
            return await conn.ExecuteScalarAsync<int>(@"
                INSERT INTO CycleRecords (SessionCycleConfigId, CycleNumber, ActualDurationTicks, CycleStartUtc, ScheduledScreenshotUtc, PromptDueUtc, Status)
                VALUES (@SessionCycleConfigId, @CycleNumber, @ActualDurationTicks, @CycleStartUtc, @ScheduledScreenshotUtc, @PromptDueUtc, @Status);
                SELECT last_insert_rowid();",
                new
                {
                    record.SessionCycleConfigId,
                    record.CycleNumber,
                    record.ActualDurationTicks,
                    CycleStartUtc = record.CycleStartUtc.ToString("O"),
                    ScheduledScreenshotUtc = record.ScheduledScreenshotUtc.ToString("O"),
                    PromptDueUtc = record.PromptDueUtc?.ToString("O"),
                    Status = (int)record.Status
                });
        });
    }

    public async Task UpdateCycleRecordAsync(CycleRecord record)
    {
        await RetryAsync(async () =>
        {
            using var conn = OpenConnection();
            await conn.ExecuteAsync(@"
                UPDATE CycleRecords SET
                    ScreenshotTakenUtc = @ScreenshotTakenUtc,
                    PromptDueUtc = @PromptDueUtc,
                    PromptShownUtc = @PromptShownUtc,
                    CompletedUtc = @CompletedUtc,
                    Status = @Status,
                    MissedReason = @MissedReason
                WHERE Id = @Id",
                new
                {
                    record.Id,
                    ScreenshotTakenUtc = record.ScreenshotTakenUtc?.ToString("O"),
                    PromptDueUtc = record.PromptDueUtc?.ToString("O"),
                    PromptShownUtc = record.PromptShownUtc?.ToString("O"),
                    CompletedUtc = record.CompletedUtc?.ToString("O"),
                    Status = (int)record.Status,
                    record.MissedReason
                });
        });
    }

    public async Task<CycleRecord?> GetCycleRecordAsync(int id)
    {
        using var conn = OpenConnection();
        var row = await conn.QueryFirstOrDefaultAsync("SELECT * FROM CycleRecords WHERE Id = @Id", new { Id = id });
        if (row == null) return null;
        return MapCycleRecord(row);
    }

    public async Task<List<CycleRecord>> GetCyclesByConfigAsync(int configId)
    {
        using var conn = OpenConnection();
        var rows = await conn.QueryAsync("SELECT * FROM CycleRecords WHERE SessionCycleConfigId = @ConfigId ORDER BY CycleNumber", new { ConfigId = configId });
        return rows.Select(r => (CycleRecord)MapCycleRecord(r)).ToList();
    }

    public async Task<CycleRecord?> GetLatestActiveCycleAsync(int configId)
    {
        using var conn = OpenConnection();
        var row = await conn.QueryFirstOrDefaultAsync(
            "SELECT * FROM CycleRecords WHERE SessionCycleConfigId = @ConfigId AND Status NOT IN (@Completed, @Missed, @Incomplete) ORDER BY CycleNumber DESC LIMIT 1",
            new { ConfigId = configId, Completed = (int)CycleStatus.Completed, Missed = (int)CycleStatus.Missed, Incomplete = (int)CycleStatus.Incomplete });
        if (row == null) return null;
        return MapCycleRecord(row);
    }

    private static CycleRecord MapCycleRecord(dynamic r)
    {
        return new CycleRecord
        {
            Id = (int)r.Id,
            SessionCycleConfigId = (int)r.SessionCycleConfigId,
            CycleNumber = (int)r.CycleNumber,
            ActualDurationTicks = (long)r.ActualDurationTicks,
            CycleStartUtc = DateTime.Parse((string)r.CycleStartUtc, null, System.Globalization.DateTimeStyles.RoundtripKind),
            ScheduledScreenshotUtc = DateTime.Parse((string)r.ScheduledScreenshotUtc, null, System.Globalization.DateTimeStyles.RoundtripKind),
            ScreenshotTakenUtc = r.ScreenshotTakenUtc != null ? DateTime.Parse((string)r.ScreenshotTakenUtc, null, System.Globalization.DateTimeStyles.RoundtripKind) : null,
            PromptDueUtc = r.PromptDueUtc != null ? DateTime.Parse((string)r.PromptDueUtc, null, System.Globalization.DateTimeStyles.RoundtripKind) : null,
            PromptShownUtc = r.PromptShownUtc != null ? DateTime.Parse((string)r.PromptShownUtc, null, System.Globalization.DateTimeStyles.RoundtripKind) : null,
            CompletedUtc = r.CompletedUtc != null ? DateTime.Parse((string)r.CompletedUtc, null, System.Globalization.DateTimeStyles.RoundtripKind) : null,
            Status = (CycleStatus)(int)(long)r.Status,
            MissedReason = (string?)r.MissedReason,
        };
    }

    // ── CaptureRecords ──

    public async Task<int> CreateCaptureRecordAsync(CaptureRecord record)
    {
        return await RetryAsync(async () =>
        {
            using var conn = OpenConnection();
            return await conn.ExecuteScalarAsync<int>(@"
                INSERT INTO CaptureRecords (CycleRecordId, Type, IsMain, DecoyOffsetMinutes, FilePath, TakenAtUtc, Availability, IsDeleted)
                VALUES (@CycleRecordId, @Type, @IsMain, @DecoyOffsetMinutes, @FilePath, @TakenAtUtc, @Availability, 0);
                SELECT last_insert_rowid();",
                new
                {
                    record.CycleRecordId,
                    Type = (int)record.Type,
                    IsMain = record.IsMain ? 1 : 0,
                    record.DecoyOffsetMinutes,
                    record.FilePath,
                    TakenAtUtc = record.TakenAtUtc?.ToString("O"),
                    Availability = (int)record.Availability,
                });
        });
    }

    public async Task<List<CaptureRecord>> GetCapturesByCycleAsync(int cycleRecordId)
    {
        using var conn = OpenConnection();
        var rows = await conn.QueryAsync("SELECT * FROM CaptureRecords WHERE CycleRecordId = @CycleRecordId", new { CycleRecordId = cycleRecordId });
        return rows.Select<dynamic, CaptureRecord>(r => MapCaptureRecord(r)).ToList();
    }

    public async Task<List<CaptureRecord>> GetCapturesByConfigAsync(int configId)
    {
        using var conn = OpenConnection();
        var rows = await conn.QueryAsync(@"
            SELECT c.* FROM CaptureRecords c
            INNER JOIN CycleRecords cr ON cr.Id = c.CycleRecordId
            WHERE cr.SessionCycleConfigId = @ConfigId",
            new { ConfigId = configId });
        return rows.Select<dynamic, CaptureRecord>(r => MapCaptureRecord(r)).ToList();
    }

    public async Task<List<CaptureRecord>> GetCapturesBySessionAsync(int sessionId)
    {
        using var conn = OpenConnection();
        var rows = await conn.QueryAsync(@"
            SELECT c.* FROM CaptureRecords c
            INNER JOIN CycleRecords cr ON cr.Id = c.CycleRecordId
            INNER JOIN SessionCycleConfigs scc ON scc.Id = cr.SessionCycleConfigId
            WHERE scc.SessionId = @SessionId AND c.IsDeleted = 0",
            new { SessionId = sessionId });
        return rows.Select<dynamic, CaptureRecord>(r => MapCaptureRecord(r)).ToList();
    }

    public async Task<CaptureRecord?> GetRandomAvailableScreenshotCaptureAsync(int excludeCycleRecordId)
    {
        using var conn = OpenConnection();
        var row = await conn.QueryFirstOrDefaultAsync(@"
            SELECT * FROM CaptureRecords
            WHERE IsDeleted = 0 AND Type = @Type AND IsMain = 0 AND CycleRecordId != @ExcludeCycleRecordId
            ORDER BY RANDOM() LIMIT 1",
            new { ExcludeCycleRecordId = excludeCycleRecordId, Type = (int)CaptureType.Screenshot });
        if (row == null) return null;
        return MapCaptureRecord(row);
    }

    public async Task MarkCaptureDeletedAsync(int captureId)
    {
        await RetryAsync(async () =>
        {
            using var conn = OpenConnection();
            await conn.ExecuteAsync("UPDATE CaptureRecords SET IsDeleted = 1 WHERE Id = @Id", new { Id = captureId });
        });
    }

    public async Task MarkAllCapturesDeletedAsync()
    {
        await RetryAsync(async () =>
        {
            using var conn = OpenConnection();
            await conn.ExecuteAsync("UPDATE CaptureRecords SET IsDeleted = 1");
        });
    }

    private static CaptureRecord MapCaptureRecord(dynamic r)
    {
        return new CaptureRecord
        {
            Id = (int)r.Id,
            CycleRecordId = (int)r.CycleRecordId,
            Type = (CaptureType)(int)(long)r.Type,
            IsMain = (long)r.IsMain == 1,
            DecoyOffsetMinutes = r.DecoyOffsetMinutes is long dm ? (int?)((int)dm) : null,
            FilePath = (string?)r.FilePath,
            TakenAtUtc = r.TakenAtUtc != null ? DateTime.Parse((string)r.TakenAtUtc, null, System.Globalization.DateTimeStyles.RoundtripKind) : null,
            Availability = (CaptureAvailability)(int)(long)r.Availability,
            IsDeleted = (long)r.IsDeleted == 1,
        };
    }

    // ── FreeRecallResults ──

    public async Task CreateFreeRecallResultAsync(FreeRecallResult result)
    {
        await RetryAsync(async () =>
        {
            using var conn = OpenConnection();
            await conn.ExecuteAsync(@"
                INSERT INTO FreeRecallResults (CycleRecordId, RecallText, Result, EvaluatedAtUtc)
                VALUES (@CycleRecordId, @RecallText, @Result, @EvaluatedAtUtc)",
                new
                {
                    result.CycleRecordId,
                    result.RecallText,
                    Result = (int)result.Result,
                    EvaluatedAtUtc = result.EvaluatedAtUtc.ToString("O")
                });
        });
    }

    public async Task<FreeRecallResult?> GetFreeRecallByCycleAsync(int cycleRecordId)
    {
        using var conn = OpenConnection();
        var row = await conn.QueryFirstOrDefaultAsync("SELECT * FROM FreeRecallResults WHERE CycleRecordId = @CycleRecordId", new { CycleRecordId = cycleRecordId });
        if (row == null) return null;
        return new FreeRecallResult
        {
            Id = (int)row.Id,
            CycleRecordId = (int)row.CycleRecordId,
            RecallText = (string)row.RecallText,
            Result = (EvaluationResult)(int)(long)row.Result,
            EvaluatedAtUtc = DateTime.Parse((string)row.EvaluatedAtUtc, null, System.Globalization.DateTimeStyles.RoundtripKind),
        };
    }

    // ── RecognitionResults ──

    public async Task CreateRecognitionResultAsync(RecognitionResult result)
    {
        await RetryAsync(async () =>
        {
            using var conn = OpenConnection();
            await conn.ExecuteAsync(@"
                INSERT INTO RecognitionResults (CycleRecordId, SelectedScreenshotRecordId, CorrectScreenshotRecordId, IsCorrect, EvaluatedAtUtc)
                VALUES (@CycleRecordId, @SelectedScreenshotRecordId, @CorrectScreenshotRecordId, @IsCorrect, @EvaluatedAtUtc)",
                new
                {
                    result.CycleRecordId,
                    SelectedScreenshotRecordId = result.SelectedCaptureRecordId,
                    CorrectScreenshotRecordId = result.CorrectCaptureRecordId,
                    IsCorrect = result.IsCorrect ? 1 : 0,
                    EvaluatedAtUtc = result.EvaluatedAtUtc.ToString("O")
                });
        });
    }

    public async Task<RecognitionResult?> GetRecognitionByCycleAsync(int cycleRecordId)
    {
        using var conn = OpenConnection();
        var row = await conn.QueryFirstOrDefaultAsync("SELECT * FROM RecognitionResults WHERE CycleRecordId = @CycleRecordId", new { CycleRecordId = cycleRecordId });
        if (row == null) return null;
        return new RecognitionResult
        {
            Id = (int)row.Id,
            CycleRecordId = (int)row.CycleRecordId,
            SelectedCaptureRecordId = (int)row.SelectedScreenshotRecordId,
            CorrectCaptureRecordId = (int)row.CorrectScreenshotRecordId,
            IsCorrect = (long)row.IsCorrect == 1,
            EvaluatedAtUtc = DateTime.Parse((string)row.EvaluatedAtUtc, null, System.Globalization.DateTimeStyles.RoundtripKind),
        };
    }

    // ── AudioRecallResults ──

    public async Task CreateAudioRecallResultAsync(AudioRecallResult result)
    {
        await RetryAsync(async () =>
        {
            using var conn = OpenConnection();
            await conn.ExecuteAsync(@"
                INSERT INTO AudioRecallResults (CycleRecordId, RecallText, Result, EvaluatedAtUtc)
                VALUES (@CycleRecordId, @RecallText, @Result, @EvaluatedAtUtc)",
                new
                {
                    result.CycleRecordId,
                    result.RecallText,
                    Result = (int)result.Result,
                    EvaluatedAtUtc = result.EvaluatedAtUtc.ToString("O")
                });
        });
    }

    // ── CameraRecallResults ──

    public async Task CreateCameraRecallResultAsync(CameraRecallResult result)
    {
        await RetryAsync(async () =>
        {
            using var conn = OpenConnection();
            await conn.ExecuteAsync(@"
                INSERT INTO CameraRecallResults (CycleRecordId, RecallText, Result, EvaluatedAtUtc)
                VALUES (@CycleRecordId, @RecallText, @Result, @EvaluatedAtUtc)",
                new
                {
                    result.CycleRecordId,
                    result.RecallText,
                    Result = (int)result.Result,
                    EvaluatedAtUtc = result.EvaluatedAtUtc.ToString("O")
                });
        });
    }

    // ── History Queries ──

    public record HistoryFilters(
        DateTime? FromUtc,
        DateTime? ToUtc,
        List<long>? DurationTicksList,
        int? SessionId,
        bool ShowMissed,
        RecallMode? RecallModeFilter,
        string? CaptureTypeFilter = null,
        string SortBy = "Date",
        bool SortDesc = true
    );

    public record CycleRecordHistoryRow(
        int CycleRecordId,
        DateTime ScreenshotTakenUtc,
        string SessionName,
        long BaseDurationTicks,
        int CycleNumber,
        EvaluationResult? FreeRecallResult,
        string? FreeRecallText,
        bool? RecognitionCorrect,
        EvaluationResult? AudioRecallResult,
        EvaluationResult? CameraRecallResult,
        CycleStatus Status
    );

    public async Task<List<CycleRecordHistoryRow>> GetFilteredCycleRecordsAsync(HistoryFilters filters)
    {
        using var conn = OpenConnection();
        var sql = BuildHistoryQuery(filters);
        var rows = await conn.QueryAsync(sql.sql, sql.param);
        return rows.Select(r => (CycleRecordHistoryRow)MapHistoryRow(r)).ToList();
    }

    private static CycleRecordHistoryRow MapHistoryRow(dynamic r)
    {
        EvaluationResult? freeResult = r.FreeResult != null ? (EvaluationResult)(int)(long)r.FreeResult : null;
        bool? recogCorrect = r.RecogCorrect != null ? (long)r.RecogCorrect == 1 : null;
        EvaluationResult? audioResult = r.AudioResult != null ? (EvaluationResult)(int)(long)r.AudioResult : null;
        EvaluationResult? cameraResult = r.CameraResult != null ? (EvaluationResult)(int)(long)r.CameraResult : null;
        DateTime screenshotUtc = r.ScreenshotTakenUtc != null
            ? DateTime.Parse((string)r.ScreenshotTakenUtc, null, System.Globalization.DateTimeStyles.RoundtripKind)
            : DateTime.Parse((string)r.CycleStartUtc, null, System.Globalization.DateTimeStyles.RoundtripKind);
        return new CycleRecordHistoryRow(
            (int)r.CycleRecordId,
            screenshotUtc,
            (string)r.SessionName,
            (long)r.BaseDurationTicks,
            (int)r.CycleNumber,
            freeResult,
            (string?)r.FreeRecallText,
            recogCorrect,
            audioResult,
            cameraResult,
            (CycleStatus)(int)(long)r.Status
        );
    }

    private static (string sql, object param) BuildHistoryQuery(HistoryFilters f)
    {
        var conditions = new List<string>();
        var p = new DynamicParameters();

        if (f.FromUtc.HasValue) { conditions.Add("cr.ScreenshotTakenUtc >= @From"); p.Add("From", f.FromUtc.Value.ToString("O")); }
        if (f.ToUtc.HasValue) { conditions.Add("cr.ScreenshotTakenUtc <= @To"); p.Add("To", f.ToUtc.Value.ToString("O")); }
        if (f.SessionId.HasValue) { conditions.Add("s.Id = @SessionId"); p.Add("SessionId", f.SessionId.Value); }
        if (!f.ShowMissed) conditions.Add("cr.Status != @MissedStatus");
        p.Add("MissedStatus", (int)CycleStatus.Missed);

        if (f.DurationTicksList != null && f.DurationTicksList.Count > 0)
        {
            conditions.Add("scc.BaseDurationTicks IN @DurationTicks");
            p.Add("DurationTicks", f.DurationTicksList);
        }

        if (f.RecallModeFilter == RecallMode.Free)
            conditions.Add("scc.FreeRecallEnabled = 1");
        else if (f.RecallModeFilter == RecallMode.Recognition)
            conditions.Add("scc.RecognitionEnabled = 1");

        if (f.CaptureTypeFilter == "Audio")
            conditions.Add("scc.AudioEnabled = 1");
        else if (f.CaptureTypeFilter == "Camera")
            conditions.Add("scc.CameraEnabled = 1");

        string where = conditions.Count > 0 ? "WHERE " + string.Join(" AND ", conditions) : "";
        string orderBy = f.SortBy switch
        {
            "Duration" => $"scc.BaseDurationTicks {(f.SortDesc ? "DESC" : "ASC")}",
            "Result" => $"fr.Result {(f.SortDesc ? "DESC" : "ASC")}",
            _ => $"cr.ScreenshotTakenUtc {(f.SortDesc ? "DESC" : "ASC")}"
        };

        string sql = $@"
            SELECT cr.Id AS CycleRecordId, cr.ScreenshotTakenUtc, cr.CycleStartUtc, cr.CycleNumber, cr.Status,
                   s.Name AS SessionName,
                   scc.BaseDurationTicks,
                   fr.Result AS FreeResult, fr.RecallText AS FreeRecallText,
                   rr.IsCorrect AS RecogCorrect,
                   ar.Result AS AudioResult,
                   camr.Result AS CameraResult
            FROM CycleRecords cr
            INNER JOIN SessionCycleConfigs scc ON scc.Id = cr.SessionCycleConfigId
            INNER JOIN Sessions s ON s.Id = scc.SessionId
            LEFT JOIN FreeRecallResults fr ON fr.CycleRecordId = cr.Id
            LEFT JOIN RecognitionResults rr ON rr.CycleRecordId = cr.Id
            LEFT JOIN AudioRecallResults ar ON ar.CycleRecordId = cr.Id
            LEFT JOIN CameraRecallResults camr ON camr.CycleRecordId = cr.Id
            {where}
            ORDER BY {orderBy}";

        return (sql, p);
    }

    public async Task<List<long>> GetDistinctDurationTicksAsync()
    {
        using var conn = OpenConnection();
        var rows = await conn.QueryAsync<long>("SELECT DISTINCT BaseDurationTicks FROM SessionCycleConfigs ORDER BY BaseDurationTicks");
        return rows.ToList();
    }
}
