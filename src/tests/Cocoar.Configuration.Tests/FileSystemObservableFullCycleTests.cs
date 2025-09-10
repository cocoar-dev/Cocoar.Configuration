// -----------------------------------------------------------------------------
// File: FileSystemObservableFullCycleTests.cs
// -----------------------------------------------------------------------------

using Cocoar.Configuration.Providers.FileSourceProvider;

namespace Cocoar.Configuration.Tests;

public class FileSystemObservableFullCycleTests
{
    [Fact]
    public void Watcher_reports_create_change_rename_delete_correctly()
    {
        // ── arrange test directory ───────────────────────────────────────────
        var dir  = Path.Combine(Path.GetTempPath(), "FsRx_Int_" + Guid.NewGuid());
        Directory.CreateDirectory(dir);

        var opts = new FileSystemObservableOptions
        {
            DebounceTime = TimeSpan.FromMilliseconds(50),          // per-file
            IdentityMode = PathIdentityMode.CurrentOrOldPath
        };

        var received = new List<FileSystemChange>();
        using var watcher = new FileSystemObservable(dir, opts)
            .Subscribe(received.Add);

        // give the watcher a moment to attach to OS notifications
        Thread.Sleep(100);

        try
        {
            // ── 1️⃣ create file ──────────────────────────────────────────
            var f1 = Path.Combine(dir, "config.json");
            File.WriteAllText(f1, "{}");
            WaitUntil(() => received.Count == 1);

            Assert.Single(received);
            Assert.Contains(received[0].ChangeType,
                new[] { FileSystemChangeType.Changed, FileSystemChangeType.Created });

            received.Clear();

            // ── 2️⃣ modify twice fast → expect ONE Changed ───────────────
            File.AppendAllText(f1, "x");
            Thread.Sleep(20);                 // within debounce window
            File.AppendAllText(f1, "y");

            WaitUntil(() => received.Any());
            Assert.Single(received);
            Assert.Contains(received[0].ChangeType,
                new[] { FileSystemChangeType.Changed, FileSystemChangeType.Created });

            Assert.Equal(f1, received[0].Path);

            received.Clear();

            // ── 3️⃣ rename file ─────────────────────────────────────────
            var f2 = Path.Combine(dir, "config_new.json");
            File.Move(f1, f2);

            WaitUntil(() => received.Any());
            Assert.Single(received);
            var ren = received[0];
            Assert.Equal(FileSystemChangeType.Renamed, ren.ChangeType);
            Assert.Equal(f2, ren.Path);
            Assert.Equal(f1, ren.OldPath);

            received.Clear();

            // ── 4️⃣ delete file ─────────────────────────────────────────
            File.Delete(f2);
            WaitUntil(() => received.Any());
            Assert.Single(received);
            Assert.Equal(FileSystemChangeType.Deleted, received[0].ChangeType);
            Assert.Equal(f2, received[0].Path);
        }
        finally
        {
            watcher.Dispose();                // stop before deleting dir
            Directory.Delete(dir, true);
        }
    }

    // helper: spin-wait up to 5 s (tight but resilient on CI);
    // if tests exceed this repeatedly, there's a deeper issue with file notifications
    private static void WaitUntil(Func<bool> predicate, int timeoutMs = 5000)
    {
        var start = Environment.TickCount;
        while (!predicate())
        {
            if (Environment.TickCount - start > timeoutMs)
                throw new TimeoutException("Timed out waiting for watcher event.");
            Thread.Sleep(10);
        }
    }
}