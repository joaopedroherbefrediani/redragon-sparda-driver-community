using SpardaControl.Core;

public static class WriteTest
{
    public static async Task RunAsync()
    {
        using var mouse = await MouseDevice.ConnectAsync() ?? throw new Exception("not found");
        await mouse.LoadAllAsync();
        var s = mouse.Settings;
        var before = mouse.SnapshotFlash();
        int deb = s.DebounceTime;
        var c = s.Dpis[0];
        Console.WriteLine($"Rewriting debounce={deb} and DPI1 color #{c.R:X2}{c.G:X2}{c.B:X2} ...");
        await mouse.SetDebounceAsync(deb);
        await mouse.SetDpiColorAsync(0, c.R, c.G, c.B);
        await mouse.SetDpiValueAsync(0, s.Dpis[0].X);
        await mouse.ReadSettingsAsync();
        var after = mouse.SnapshotFlash();
        int diffs = 0;
        for (int i = 0; i < 256; i++) if (before[i] != after[i]) { diffs++; Console.WriteLine($"  diff @{i}: {before[i]:X2} -> {after[i]:X2}"); }
        Console.WriteLine(diffs == 0 ? "WRITE TEST OK (acks received, flash unchanged)" : $"{diffs} differences");
        await mouse.CloseAsync();
    }
}
