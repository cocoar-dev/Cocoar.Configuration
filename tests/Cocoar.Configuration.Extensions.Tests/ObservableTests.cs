// -----------------------------------------------------------------------------
// File: FileSystemObservableTests.cs          ❶ put in test project
// -----------------------------------------------------------------------------
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reactive;
using System.Reactive.Linq;
using Cocoar.Configuration.Extensions;
using Cocoar.Configuration.Extensions.Providers.FileSourceProvider;
using Microsoft.Reactive.Testing;
using Xunit;

// namespace that holds your code

public sealed class FileSystemObservableTests : ReactiveTest
{
    // ─────────────────────────────────────────────────────────────────────────
    //  1.  Pure operator: per-file debouncing
    // ─────────────────────────────────────────────────────────────────────────
    [Fact]
    public void Debounce_one_event_per_path()
    {
        var sch = new TestScheduler();
        var source = sch.CreateColdObservable(
            OnNext( 010, New("a")),
            OnNext( 020, New("a")),    // within window
            OnNext( 030, New("b")),
            OnNext( 080, New("a")),    // after window – new round
            OnCompleted<FileSystemChange>(200));

        var results = sch.Start(
            () => source
                .GroupBy(ch => ch.Path)
                .SelectMany(g => g.Throttle(TimeSpan.FromTicks(50), sch)),
            created:    0,
            subscribed: 0,     // ← subscribe at t=0
            disposed:   1000);

        // Expect: last for "a" @70, "b" @80, last for "a" @130
        Assert.Equal(3, results.Messages.Count(m => m.Value.Kind == NotificationKind.OnNext));
        Assert.Equal("a", results.Messages[0].Value.Value.Path);
        Assert.Equal( 071, results.Messages[0].Time);
        Assert.Equal("b", results.Messages[1].Value.Value.Path);
        Assert.Equal( 081, results.Messages[1].Time);
        Assert.Equal("a", results.Messages[2].Value.Value.Path);
        Assert.Equal( 131, results.Messages[2].Time);
    }

    // helper to build test items quickly
    private static FileSystemChange New(string path) =>
        new(FileSystemChangeType.Changed, path);

    // ─────────────────────────────────────────────────────────────────────────
    //  2.  IdentityMode = CurrentOrOldPath   (rename overrides earlier change)
    // ─────────────────────────────────────────────────────────────────────────
    [Fact]
    public void Rename_overrides_change_with_alias_key()
    {
        var sch = new TestScheduler();
        var changed = new FileSystemChange(FileSystemChangeType.Changed, "a.txt");
        var renamed = new FileSystemChange(FileSystemChangeType.Renamed, "b.txt", "a.txt");

        var source = sch.CreateColdObservable(
            OnNext(010, changed),
            OnNext(020, renamed),
            OnCompleted<FileSystemChange>(200));

        Func<FileSystemChange,string> Key = ch => ch.OldPath ?? ch.Path;

        var result = sch.Start(() =>
            source
                .GroupBy(Key)
                .SelectMany(g => g.Throttle(TimeSpan.FromTicks(40), sch)));

        var only = result.Messages.Single(m => m.Value.Kind == NotificationKind.OnNext).Value.Value;
        Assert.Equal(FileSystemChangeType.Renamed, only.ChangeType);
        Assert.Equal("b.txt", only.Path);
        Assert.Equal("a.txt", only.OldPath);
    }

    // ─────────────────────────────────────────────────────────────────────────
    //  3.  CollapseBurst groups by quiet gap and keeps last per path
    // ─────────────────────────────────────────────────────────────────────────
    [Fact]
    public void CollapseBurst_emits_array_with_last_change_per_path()
    {
        var sch = new TestScheduler();
        var src = sch.CreateColdObservable(
            OnNext( 05, new FileSystemChange(FileSystemChangeType.Created, "x.cfg")),
            OnNext( 10, new FileSystemChange(FileSystemChangeType.Changed, "x.cfg")),
            OnNext( 15, new FileSystemChange(FileSystemChangeType.Created, "y.cfg")),
            OnCompleted<FileSystemChange>(200));

        var result = sch.Start(() =>
            src.CollapseBurst(TimeSpan.FromTicks(30), ch => ch.Path));  // quiet gap 30

        var batch = result.Messages.Single(n => n.Value.Kind == NotificationKind.OnNext)
                                   .Value.Value;
        Assert.Equal(2, batch.Length);
        Assert.Contains(batch, ch => ch.Path == "x.cfg" && ch.ChangeType == FileSystemChangeType.Changed);
        Assert.Contains(batch, ch => ch.Path == "y.cfg");
    }

    // ─────────────────────────────────────────────────────────────────────────
    //  4.  Integration smoke-test: watcher sees a Created event
    // ─────────────────────────────────────────────────────────────────────────
    [Fact]
    public void Watcher_emits_created_event_when_file_appears()
    {
        var dir = Path.Combine(Path.GetTempPath(), "FsRxTest_" + Guid.NewGuid());
        Directory.CreateDirectory(dir);
        try
        {
            var evt = new List<FileSystemChange>();
            using var sub = new FileSystemObservable(dir,
                                new FileSystemObservableOptions { DebounceTime = TimeSpan.FromMilliseconds(50) })
                            .Subscribe(evt.Add);

            var file = Path.Combine(dir, "new.json");
            File.WriteAllText(file, "{}");                // trigger event
            File.SetLastWriteTimeUtc(file, DateTime.UtcNow); // make sure

            SpinWait.SpinUntil(() => evt.Any(), 2000);    // wait ≤2 s

            Assert.Single(evt);
            Assert.Contains(evt[0].ChangeType,
                new[] { FileSystemChangeType.Changed, FileSystemChangeType.Created });
            Assert.Equal(file, evt[0].Path);
        }
        finally
        {
            Directory.Delete(dir, true);
        }
    }
}
