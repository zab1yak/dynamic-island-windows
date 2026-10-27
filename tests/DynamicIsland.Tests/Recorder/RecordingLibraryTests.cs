using DynamicIsland.Modules.Recorder.Services;

namespace DynamicIsland.Tests.Recorder;

public sealed class RecordingLibraryTests : IDisposable
{
    private readonly TempFolder _temp = new();

    public void Dispose() => _temp.Dispose();

    [Fact]
    public void File_names_carry_the_date_and_format()
    {
        Assert.Equal("Recording 2026-10-27 19-05-12.mp3", RecordingLibrary.FileName(new DateTime(2026, 10, 27, 19, 5, 12), RecordingFormat.Mp3));
        Assert.EndsWith(".wav", RecordingLibrary.FileName(DateTime.Now, RecordingFormat.Wav), StringComparison.Ordinal);
    }

    [Fact]
    public void Delete_refuses_subfolders_other_extensions_and_missing_files()
    {
        var sub = Directory.CreateDirectory(Path.Combine(_temp.Path, "sub")).FullName;
        var nested = Path.Combine(sub, "Recording n.wav");
        File.WriteAllText(nested, "x");
        var text = Path.Combine(_temp.Path, "Recording t.txt");
        File.WriteAllText(text, "x");

        Assert.False(RecordingLibrary.Delete(_temp.Path, nested));
        Assert.False(RecordingLibrary.Delete(_temp.Path, text));
        Assert.False(RecordingLibrary.Delete(_temp.Path, Path.Combine(_temp.Path, "Recording gone.wav")));
        Assert.True(File.Exists(nested));
        Assert.True(File.Exists(text));
    }

    [Fact]
    public void Unsafe_recordings_folders_fall_back_to_the_default()
    {
        var windows = Environment.GetFolderPath(Environment.SpecialFolder.Windows);
        string?[] bad = [null, "", @"relative\path", @"\\server\share\rec", Path.GetPathRoot(windows), windows, Path.Combine(windows, "Temp")];
        foreach (var folder in bad)
        {
            Assert.False(RecorderSettings.IsAcceptableFolder(folder), folder);
            Assert.Equal(RecorderSettings.DefaultFolder, new RecorderSettings { Folder = folder }.EffectiveFolder);
        }

        Assert.True(RecorderSettings.IsAcceptableFolder(_temp.Path));
        Assert.Equal(_temp.Path, new RecorderSettings { Folder = _temp.Path }.EffectiveFolder);
    }

    [Fact]
    public void Unique_path_never_overwrites()
    {
        var first = RecordingLibrary.UniquePath(_temp.Path, "Recording a.wav");
        File.WriteAllText(first, "x");
        var second = RecordingLibrary.UniquePath(_temp.Path, "Recording a.wav");
        Assert.NotEqual(first, second);
        Assert.EndsWith("Recording a (2).wav", second, StringComparison.Ordinal);
    }

    [Fact]
    public void Only_recorder_files_are_listed_newest_first()
    {
        File.WriteAllText(Path.Combine(_temp.Path, "Recording 1.wav"), "a");
        File.SetCreationTimeUtc(Path.Combine(_temp.Path, "Recording 1.wav"), DateTime.UtcNow.AddMinutes(-5));
        File.WriteAllText(Path.Combine(_temp.Path, "Recording 2.mp3"), "b");
        File.WriteAllText(Path.Combine(_temp.Path, "song.mp3"), "c");
        File.WriteAllText(Path.Combine(_temp.Path, "Recording 3.txt"), "d");

        var list = RecordingLibrary.List(_temp.Path);

        Assert.Equal(["2", "1"], list.Select(r => r.Name));
    }

    [Fact]
    public void Delete_refuses_files_the_recorder_did_not_create()
    {
        var foreign = Path.Combine(_temp.Path, "song.mp3");
        File.WriteAllText(foreign, "c");
        var outside = Path.Combine(Path.GetTempPath(), "Recording outside.wav");

        Assert.False(RecordingLibrary.Delete(_temp.Path, foreign));
        Assert.False(RecordingLibrary.Delete(_temp.Path, outside));
        Assert.True(File.Exists(foreign));

        var own = Path.Combine(_temp.Path, "Recording x.wav");
        File.WriteAllText(own, "x");
        Assert.True(RecordingLibrary.Delete(_temp.Path, own));
        Assert.False(File.Exists(own));
    }

    [Theory]
    [InlineData(65, "1:05")]
    [InlineData(3725, "1:02:05")]
    public void Elapsed_is_formatted(double seconds, string expected)
    {
        Assert.Equal(expected, RecordingLibrary.FormatElapsed(TimeSpan.FromSeconds(seconds)));
    }

    [Fact]
    public void Waveform_scrolls_and_stays_in_range()
    {
        var waveform = new WaveformHistory(4);
        waveform.Push(1);
        waveform.Push(0);
        Assert.Equal(1, waveform.Levels[^2], 3);
        Assert.All(waveform.Levels, l => Assert.InRange(l, 0.06, 1));
        waveform.Reset();
        Assert.All(waveform.Levels, l => Assert.Equal(0.06, l));
    }

    [Fact]
    public void Default_folder_is_music_dynamic_island_and_relative_paths_are_ignored()
    {
        var settings = new RecorderSettings { Folder = "relative" };
        Assert.Equal(RecorderSettings.DefaultFolder, settings.EffectiveFolder);
        Assert.EndsWith("DynamicIsland", RecorderSettings.DefaultFolder, StringComparison.Ordinal);
    }
}
