using System.Globalization;
using System.Text.RegularExpressions;
using BpmMeasurer;

namespace ParserTester;

class Program
{
    static int _passed, _failed;
    static string _baseDir = "";


    static void Main(string[] args)
    {
        _baseDir = Path.GetFullPath(Path.Combine(AppDomain.CurrentDomain.BaseDirectory,
            "..", "..", ".."));

        Console.OutputEncoding = System.Text.Encoding.UTF8;
        Console.WriteLine("═══ TimingConfigParser 测试 ═══");
        Console.WriteLine();

        TestInvalidConfigs();
        Console.WriteLine();
        TestValidConfigs();
        Console.WriteLine();
        TestDroppedFiles();
        Console.WriteLine();
        Console.WriteLine($"══════════════════════════");
        Console.WriteLine($"  通过: {_passed}  失败: {_failed}");
        Console.WriteLine($"══════════════════════════");

        if (_failed > 0) Environment.Exit(1);
    }

    // ═══ Invalid configs ═══

    static void TestInvalidConfigs()
    {
        Console.WriteLine("─── 非法配置测试 ───");
        Console.WriteLine();

        TestInvalid("R1.1_missing_offset", "ConfigImport_Err_NoOffset");
        TestInvalid("R1.2_duplicate_offset", "ConfigImport_Err_DuplicateOffset");
        TestInvalid("R1.3_invalid_offset_value", "ConfigImport_Err_NoOffset");
        TestInvalid("R1.5_offset_mixed_with_segment", "ConfigImport_Err_MultipleInLine");
        TestInvalid("R1.6_two_offsets_one_line", "ConfigImport_Err_MultipleInLine");
        TestInvalid("R2_no_segment", "ConfigImport_Err_NoSegment");
        TestInvalid("R3.1_missing_bpm", "ConfigImport_Err_MalformedSegment");
        TestInvalid("R3.2_missing_beat_index", "ConfigImport_Err_MalformedSegment");
        TestInvalid("R3.3_duplicate_beat_index", "ConfigImport_Err_MultipleInLineSeg");
        TestInvalid("R3.4_duplicate_bpm", "ConfigImport_Err_MultipleInLineSeg");
        TestInvalid("R3.5_segment_mixed_with_offset", "ConfigImport_Err_MultipleInLine");
        TestInvalid("R4.2_beat_index_negative", "ConfigImport_Err_BadBeatIndex");
        TestInvalid("R4.3_beat_index_nan", "ConfigImport_Err_BadBeatIndex");
        TestInvalid("R5.1_bpm_zero_or_negative", "ConfigImport_Err_BadBpm");
        TestInvalid("R5.2_bpm_scientific_notation", "ConfigImport_Err_BadBpm");
        TestInvalid("R5.3_bpm_not_number", "ConfigImport_Err_BadBpm");
        TestInvalid("R6.1_beat_index_equal", "ConfigImport_Err_NotIncreasing");
        TestInvalid("R6.2_beat_index_decreasing", "ConfigImport_Err_NotIncreasing");
        TestInvalid("R7_first_beat_not_zero", "ConfigImport_Err_FirstBeatNotZero");
    }

    static void TestInvalid(string fileName, string expectedErrorKey)
    {
        var path = Path.Combine(_baseDir, "InvalidConfigs", $"{fileName}.txt");
        if (!File.Exists(path))
        {
            Fail(fileName, $"文件不存在: {path}");
            return;
        }
        var text = File.ReadAllText(path);

        try
        {
            bool ok = TimingConfigParser.TryParse(text, out _, out _, out string? error);
            if (ok)
                Fail(fileName, "期望解析失败，但返回了成功");
            else if (error == null || !error.Contains(expectedErrorKey))
                Fail(fileName, $"期望错误包含 '{expectedErrorKey}'，实际错误: '{error}'");
            else
                Pass(fileName, $"正确拒绝 (错误: {error})");
        }
        catch (Exception ex)
        {
            Fail(fileName, $"异常: {ex.GetType().Name}: {ex.Message}");
        }
    }

    // ═══ Valid configs ═══

    static void TestValidConfigs()
    {
        Console.WriteLine("─── 合法配置测试 ───");
        Console.WriteLine();

        TestValid_N1();
        TestValid_N2();
        TestValid_N3();
        TestValid_N4();
        TestValid_N5();
        TestValid_N6();
        TestValid_N7();
        TestValid_N8();
        TestValid_N9();
        TestValid_N10();
        TestValid_N11();
        TestValid_N12();
    }

    static bool ParseValid(string fileName, out double offset, out List<RawTimingPoint> points)
    {
        offset = 0;
        points = new();
        var path = Path.Combine(_baseDir, "ValidConfigs", $"{fileName}.txt");
        if (!File.Exists(path))
        {
            Fail(fileName, $"文件不存在: {path}");
            return false;
        }
        var text = File.ReadAllText(path);
        try
        {
            bool ok = TimingConfigParser.TryParse(text, out offset, out points, out string? error);
            if (!ok)
            {
                Fail(fileName, $"期望成功，但失败: {error}");
                return false;
            }
            return true;
        }
        catch (Exception ex)
        {
            Fail(fileName, $"异常: {ex.GetType().Name}: {ex.Message}");
            return false;
        }
    }

    static void TestValid_N1()
    {
        const string name = "N1_minimal";
        if (!ParseValid(name, out var off, out var pts)) return;
        if (off != 0.0) { Fail(name, $"offset 期望 0，实际 {off}"); return; }
        if (pts.Count != 1) { Fail(name, $"段数期望 1，实际 {pts.Count}"); return; }
        if (pts[0].Bpm != 120) { Fail(name, $"bpm 期望 120，实际 {pts[0].Bpm}"); return; }
        if (pts[0].BeatsPerBar != 4) { Fail(name, $"beats 期望 4，实际 {pts[0].BeatsPerBar}"); return; }
        Pass(name, $"offset={off} 段数={pts.Count} bpm={pts[0].Bpm} beats={pts[0].BeatsPerBar}");
    }

    static void TestValid_N2()
    {
        const string name = "N2_multi_segment";
        if (!ParseValid(name, out var off, out var pts)) return;
        if (off != 0.5) { Fail(name, $"offset 期望 0.5，实际 {off}"); return; }
        if (pts.Count != 3) { Fail(name, $"段数期望 3，实际 {pts.Count}"); return; }
        if (pts[0].Bpm != 120 || pts[1].Bpm != 160 || pts[2].Bpm != 90)
        { Fail(name, $"BPM 不匹配"); return; }
        Pass(name, $"offset={off} 段数={pts.Count} bpm=[{pts[0].Bpm},{pts[1].Bpm},{pts[2].Bpm}]");
    }

    static void TestValid_N3()
    {
        const string name = "N3_old_format_no_beats";
        if (!ParseValid(name, out var off, out var pts)) return;
        if (pts.Any(p => p.BeatsPerBar != 4))
        { Fail(name, "旧格式应默认 beats=4"); return; }
        Pass(name, $"旧格式正确默认为 beats=4（段数={pts.Count}）");
    }

    static void TestValid_N4()
    {
        const string name = "N4_beats_per_bar_bounds";
        if (!ParseValid(name, out var off, out var pts)) return;
        if (pts[0].BeatsPerBar != 1) { Fail(name, $"段0 beats 期望 1，实际 {pts[0].BeatsPerBar}"); return; }
        if (pts[1].BeatsPerBar != 20) { Fail(name, $"段1 beats 期望 20，实际 {pts[1].BeatsPerBar}"); return; }
        Pass(name, $"beats=[{pts[0].BeatsPerBar},{pts[1].BeatsPerBar}]（边界值正确）");
    }

    static void TestValid_N5()
    {
        const string name = "N5_beats_per_bar_oob_clamp";
        if (!ParseValid(name, out var off, out var pts)) return;
        if (pts[0].BeatsPerBar != 1) { Fail(name, $"beats=0 应 clamp→1，实际 {pts[0].BeatsPerBar}"); return; }
        if (pts[1].BeatsPerBar != 20) { Fail(name, $"beats=99 应 clamp→20，实际 {pts[1].BeatsPerBar}"); return; }
        Pass(name, $"beats=[{pts[0].BeatsPerBar},{pts[1].BeatsPerBar}]（越界 clamp 正确）");
    }

    static void TestValid_N6()
    {
        const string name = "N6_with_comments_and_blank_lines";
        if (!ParseValid(name, out var off, out var pts)) return;
        if (pts.Count != 2) { Fail(name, $"段数期望 2，实际 {pts.Count}"); return; }
        Pass(name, $"注释/空行正确跳过（段数={pts.Count}）");
    }

    static void TestValid_N7()
    {
        const string name = "N7_bpm_thousand_sep";
        if (!ParseValid(name, out var off, out var pts)) return;
        if (pts[0].Bpm != 1000) { Fail(name, $"千位分隔符 bpm=1,000 期望 1000，实际 {pts[0].Bpm}"); return; }
        Pass(name, "千位分隔符 bpm=1,000 正确解析为 1000");
    }

    static void TestValid_N8()
    {
        const string name = "N8_offset_thousand_sep";
        if (!ParseValid(name, out var off, out var pts)) return;
        if (off != 1000.5) { Fail(name, $"千位分隔符 offset=1,000.5 期望 1000.5，实际 {off}"); return; }
        Pass(name, "千位分隔符 offset 正确解析为 1000.5");
    }

    static void TestValid_N9()
    {
        const string name = "N9_bpm_low_clamp";
        if (!ParseValid(name, out var off, out var pts)) return;
        if (pts[0].Bpm != 10) { Fail(name, $"bpm=5 应 clamp→10，实际 {pts[0].Bpm}"); return; }
        Pass(name, "bpm=5 正确 clamp 到 10");
    }

    static void TestValid_N10()
    {
        const string name = "N10_bpm_high_clamp";
        if (!ParseValid(name, out var off, out var pts)) return;
        if (pts[0].Bpm != 1000) { Fail(name, $"bpm=2000 应 clamp→1000，实际 {pts[0].Bpm}"); return; }
        Pass(name, "bpm=2000 正确 clamp 到 1000");
    }

    static void TestValid_N11()
    {
        const string name = "N11_beat_index_float";
        if (!ParseValid(name, out var off, out var pts)) return;
        if (off != 0.5) { Fail(name, $"offset 期望 0.5，实际 {off}"); return; }
        if (pts.Count != 3) { Fail(name, $"段数期望 3，实际 {pts.Count}"); return; }
        if (pts[0].BeatIndex != 0) { Fail(name, $"段0 beat_index 期望 0，实际 {pts[0].BeatIndex}"); return; }
        if (Math.Abs(pts[1].BeatIndex - 64.5) > 0.001) { Fail(name, $"段1 beat_index 期望 64.5，实际 {pts[1].BeatIndex}"); return; }
        if (pts[1].BeatsPerBar != 3) { Fail(name, $"段1 beats_per_bar 期望 3，实际 {pts[1].BeatsPerBar}"); return; }
        if (pts[2].BeatIndex != 200) { Fail(name, $"段2 beat_index 期望 200，实际 {pts[2].BeatIndex}"); return; }
        Pass(name, $"offset={off} 小数 beat_index 正确解析");
    }

    static void TestValid_N12()
    {
        const string name = "N12_negative_offset";
        if (!ParseValid(name, out var off, out var pts)) return;
        if (off != -1.5) { Fail(name, $"负 offset 期望 -1.5，实际 {off}"); return; }
        if (pts.Count != 1) { Fail(name, $"段数期望 1，实际 {pts.Count}"); return; }
        if (pts[0].Bpm != 120) { Fail(name, $"bpm 期望 120，实际 {pts[0].Bpm}"); return; }
        Pass(name, $"offset={off}（负值正确接受）段数={pts.Count} bpm={pts[0].Bpm}");
    }

    // ═══ DroppedFiles 归类 ═══

    static void TestDroppedFiles()
    {
        Console.WriteLine("─── 拖放归类测试 ───");
        Console.WriteLine();

        var dir = Path.Combine(_baseDir, "ValidConfigs");

        CheckDrop("D1_audio_only", new[] { "song.mp3" }, "song.mp3", null);
        CheckDrop("D2_config_only", new[] { "song.txt" }, null, "song.txt");
        CheckDrop("D3_txt_listed_first", new[] { "song.txt", "song.flac" }, "song.flac", "song.txt");
        CheckDrop("D4_multi_no_stem_match",
            new[] { "b.mp3", "a.mp3", "x.txt", "a.txt" }, "b.mp3", "x.txt");
        CheckDrop("D5_multi_prefer_same_stem",
            new[] { "a.mp3", "b.mp3", "x.txt", "a.txt" }, "a.mp3", "a.txt");
        CheckDrop("D6_unknown_ext_falls_back",
            new[] { "cover.png", "song.txt" }, "cover.png", "song.txt");
        CheckDrop("D7_directory_skipped",
            new[] { dir, "song.mp3" }, "song.mp3", null);
        CheckDrop("D8_extension_case_insensitive",
            new[] { "song.MP3", "SONG.TXT" }, "song.MP3", "SONG.TXT");
        CheckDrop("D9_same_stem_different_dirs",
            new[] { @"C:\m\song.mp3", @"D:\n\song.txt" }, "song.mp3", "song.txt");
        CheckDrop("D10_configs_only_pick_first",
            new[] { "a.txt", "b.txt" }, null, "a.txt");
        CheckDrop("D11_empty", Array.Empty<string>(), null, null);
    }

    static void CheckDrop(string name, string[] paths, string? expectAudio, string? expectConfig)
    {
        var (audio, config) = DroppedFiles.Resolve(paths);
        var gotAudio = audio is null ? null : Path.GetFileName(audio);
        var gotConfig = config is null ? null : Path.GetFileName(config);

        if (!string.Equals(gotAudio, expectAudio, StringComparison.OrdinalIgnoreCase))
        {
            Fail(name, $"音频期望 '{expectAudio ?? "null"}'，实际 '{gotAudio ?? "null"}'");
            return;
        }
        if (!string.Equals(gotConfig, expectConfig, StringComparison.OrdinalIgnoreCase))
        {
            Fail(name, $"配置期望 '{expectConfig ?? "null"}'，实际 '{gotConfig ?? "null"}'");
            return;
        }
        Pass(name, $"audio={gotAudio ?? "null"}  config={gotConfig ?? "null"}");
    }

    // ═══ Helpers ═══

    static void Pass(string name, string detail)
    {
        _passed++;
        Console.WriteLine($"  \u2705 PASS  {name}");
        if (!string.IsNullOrEmpty(detail))
            Console.WriteLine($"         {detail}");
    }

    static void Fail(string name, string reason)
    {
        _failed++;
        Console.WriteLine($"  \u274C FAIL  {name}");
        Console.WriteLine($"         原因: {reason}");
    }
}
