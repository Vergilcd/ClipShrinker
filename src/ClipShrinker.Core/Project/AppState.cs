using System.Text.Json.Nodes;
using ClipShrinker.Core.Settings;
using static ClipShrinker.Core.Py.PyJson;

namespace ClipShrinker.Core.Project;

/// <summary>
/// The export-related values of the main window, as text like the Tk variables held them
/// (reference App.__init__ lines 3289-3342 and 4013-4022, _apply_project_settings, _bitrate_changed, start_export).
/// </summary>
public sealed class AppState
{
    /// <summary>2.0: 128k and up (1.0.8 also had 48k / 64k / 96k; a saved lower value falls back to 128k).</summary>
    /// <summary>"Original" (2.6) = the clips' own audio bitrate (Text.ResolveAudioK).</summary>
    public static readonly string[] AudioRates = ["Original", "128k", "192k", "256k", "320k"];
    public static readonly string[] AudioTrackModes = ["Keep as separate tracks", "Mix into one track"];

    /// <summary>2.6 renamed "Keep separate tracks"; settings and projects from before still say the old name.</summary>
    static void Migrate(JsonObject s)
    {
        if (s["atrk"] is JsonValue v && v.TryGetValue(out string? a) && a == "Keep separate tracks") s["atrk"] = "Keep as separate tracks";
    }
    public static readonly string[] ExportModes = ["separate", "join"];

    public string Size = "50", Bitrate = "", Res = "Auto", ResBeforeBitrate = "Auto", Codec = "HEVC (hevc_nvenc)";
    /// <summary>2.9.1: the Export panel's "Size limit | Fixed bitrate" switch. Bitrate stays the value the export uses
    /// (empty in size-limit mode, so exports and saved files read as before); KeptBitrate keeps the number typed for
    /// Fixed bitrate while Size limit is picked, so switching back brings it back (settings key "bitrate_kept").</summary>
    public bool FixedRate;
    public string KeptBitrate = "";
    public string Crop = "16:9", Audio = "128k", Atrk = "Mix into one track", Mode = "join";
    public string Trans = "Gradient wipe, left to right", Tlen = "0.4";
    public string FastAudio = C.FastAudio[0], SlowAudio = C.SlowAudio[0];
    public string SlowPitch = C.SlowPitch[0], FastPitch = C.FastPitch[1];
    public string ZoomFx = C.ZoomFx[0], ZoomSnd = C.ZoomAudio[0], ZoomLen = "0.4", SlowInterp = C.SlowInterp[2];
    public bool MuteMic, Vhs = true, Hdr = true;
    public int FastVol = 70, SlowVol = 150, ZoomVol = 80;
    public string Prefix = "", Suffix = "";
    /// <summary>2.3: the clip's own name stays in the exported file name (off = only the prefix and suffix).</summary>
    public bool KeepName = true;

    // Picture adjustments module: percentages (-100 .. 100, 0 = unchanged); denoise 0 .. 100 (2.5)
    public int PicBright, PicContrast, PicSat, PicDenoise;
    /// <summary>2.8 white balance: temperature (+ warmer / - cooler) and tint (+ magenta / - green), -100 .. 100.</summary>
    public int PicTemp, PicTint;
    /// <summary>Denoise may run on the graphics card (OpenCL works on this PC; set by the app, not saved).</summary>
    public bool OpenCl;
    /// <summary>The module is switched on (set by the app, not saved here).</summary>
    public bool PictureOn;
    /// <summary>2.6: this ffmpeg has the nlmeans (denoise) filter; without it denoise is left out (set by the app).</summary>
    public bool DenoiseOk = true;

    // Sound equalizer module (2.6): five bands in dB (-12 .. 12) and the overall volume in dB (-20 .. 20)
    public int[] EqBands = new int[Modules.Equalizer.Bands.Length];
    public int EqVolume;
    /// <summary>The module is switched on (set by the app, not saved here).</summary>
    public bool EqOn;

    /// <summary>The equalizer filter for the export and the preview sound, or null (module off, or nothing changed).</summary>
    public string? EqFilter() => EqOn ? Modules.Equalizer.Filter(EqBands, EqVolume) : null;

    /// <summary>The equalizer differs from the neutral one (then it is saved in projects too).</summary>
    public bool EqChanged => EqVolume != 0 || EqBands.Any(b => b != 0);

    /// <summary>The Picture adjustments filter for every clip, or null (module off, or nothing changed).</summary>
    public string? PictureFilter(bool hdrKept = false) =>
        PictureOn ? Modules.PictureAdjust.Filter(PicBright, PicContrast, PicSat, PicTemp, PicTint, DenoiseOk ? PicDenoise : 0, OpenCl, hdrKept) : null;

    /// <summary>The picture values differ from the neutral ones (then they are saved in projects too).</summary>
    public bool PictureChanged => PicBright != 0 || PicContrast != 0 || PicSat != 0 || PicDenoise != 0 || PicTemp != 0 || PicTint != 0;

    static IEnumerable<string> ResModes => C.ResSizeMode.Concat(C.ResBitrateMode);

    /// <summary>Start-up values from settings.json (the _saved / _saved_int rules), then _bitrate_changed().</summary>
    public static AppState FromSettings(JsonObject? s)
    {
        s ??= [];
        Migrate(s);
        string Saved(string key, string def, IEnumerable<string>? allowed = null) =>
            s.TryGetPropertyValue(key, out var v) ? (Str(v) is { } x && (allowed is null || allowed.Contains(x)) ? x : def) : def;
        bool SavedB(string key, bool def) => s.TryGetPropertyValue(key, out var v) ? Bool(v) ?? def : def;
        int SavedI(string key, int def) =>
            s.TryGetPropertyValue(key, out var v) && IsPlainInt(v) ? (int)Math.Max(0, Math.Min(200, AsLong(v!))) : def;
        var a = new AppState
        {
            Hdr = SavedB("hdr", true),
            FastAudio = Saved("fast_audio", C.FastAudio[0], C.FastAudio),
            SlowAudio = Saved("slow_audio", C.SlowAudio[0], C.SlowAudio),
            FastVol = SavedI("fast_vol", 70), SlowVol = SavedI("slow_vol", 150),
            ZoomFx = Saved("zoom_fx", C.ZoomFx[0], C.ZoomFx),
            ZoomSnd = Saved("zoom_snd", C.ZoomAudio[0], C.ZoomAudio),
            ZoomLen = s.TryGetPropertyValue("zoom_len", out var zl) ? PyStr(zl) : "0.4",
            ZoomVol = SavedI("zoom_vol", 80),
            SlowPitch = Saved("slow_pitch", C.SlowPitch[0], C.SlowPitch),
            FastPitch = Saved("fast_pitch", C.FastPitch[1], C.FastPitch),
            Vhs = SavedB("vhs", true),
            SlowInterp = Saved("slow_interp", C.SlowInterp[2], C.SlowInterp),
            Atrk = Saved("atrk", "Mix into one track", AudioTrackModes),
            MuteMic = SavedB("mute_mic", false),
            Size = Saved("size", "50"),
            Crop = Saved("crop", "16:9", C.Aspects.Select(x => x.Name)),
            Res = Saved("res", "Auto", ResModes),
            Codec = Saved("codec", "HEVC (hevc_nvenc)", C.Codecs),
            Bitrate = Saved("bitrate", ""),
            ResBeforeBitrate = Saved("res_before_bitrate", "Auto", C.ResSizeMode),
            Audio = Saved("audio", "128k", AudioRates),
            Mode = Saved("mode", "join", ExportModes),
            Trans = Saved("trans", "Gradient wipe, left to right", C.Transitions.Select(x => x.Name)),
            Tlen = Saved("tlen", "0.4"),
            Prefix = Saved("prefix", ""),
            Suffix = Saved("suffix", ""),
            KeepName = SavedB("keep_name", true),
        };
        a.KeptBitrate = Saved("bitrate_kept", "");
        a.FixedRate = a.Bitrate.Trim().Length > 0;
        a.ReadPicture(s);
        a.ReadEq(s);
        a.BitrateChanged();
        return a;
    }

    /// <summary>pic_bright / pic_contrast / pic_sat (-100 .. 100) and pic_denoise (0 .. 100) from settings.json or a project.</summary>
    void ReadPicture(JsonObject s)
    {
        int? P(string key) => IsPlainInt(s[key]) ? Modules.PictureAdjust.Clamp((int)Math.Max(-1000, Math.Min(1000, AsLong(s[key]!)))) : null;
        if (P("pic_bright") is { } b) PicBright = b;
        if (P("pic_contrast") is { } c) PicContrast = c;
        if (P("pic_sat") is { } sat) PicSat = sat;
        if (P("pic_denoise") is { } dn) PicDenoise = Modules.PictureAdjust.ClampDenoise(dn);
        if (P("pic_temp") is { } tp) PicTemp = tp;
        if (P("pic_tint") is { } tn) PicTint = tn;
    }

    /// <summary>eq_bands (5 x dB) and eq_vol (dB) from settings.json or a project.</summary>
    void ReadEq(JsonObject s)
    {
        if (s["eq_bands"] is JsonArray arr)
            for (int i = 0; i < Math.Min(arr.Count, EqBands.Length); i++)
                if (IsPlainInt(arr[i])) EqBands[i] = Modules.Equalizer.ClampBand((int)Math.Max(-1000, Math.Min(1000, AsLong(arr[i]!))));
        if (IsPlainInt(s["eq_vol"])) EqVolume = Modules.Equalizer.ClampVolume((int)Math.Max(-1000, Math.Min(1000, AsLong(s["eq_vol"]!))));
    }

    /// <summary>Fixed bitrate in Mbps from the entry, or null when it is empty / not a positive number.</summary>
    public double? BitrateMbps()
    {
        var v = Py.Py.ParseFloat(Bitrate.Trim().Replace(",", "."));
        return v is { } x && x > 0 ? x : null;
    }

    /// <summary>The "Size limit | Fixed bitrate" switch (2.9.1). Leaving Fixed bitrate keeps its number in KeptBitrate
    /// and clears Bitrate; coming back puts it in again.</summary>
    public void SetFixedRate(bool on)
    {
        if (on == FixedRate) return;
        if (on) Bitrate = KeptBitrate;
        else
        {
            if (Bitrate.Trim().Length > 0) KeptBitrate = Bitrate;
            Bitrate = "";
        }
        FixedRate = on;
        BitrateChanged();
    }

    /// <summary>Bitrate mode switches the resolution list (Original / 75% / 50%), size mode switches it back.</summary>
    public void BitrateChanged()
    {
        if (BitrateMbps() is not null)
        {
            if (!C.ResBitrateMode.Contains(Res)) { ResBeforeBitrate = Res; Res = "Original"; }
        }
        else if (!C.ResSizeMode.Contains(Res))
            Res = C.ResSizeMode.Contains(ResBeforeBitrate) ? ResBeforeBitrate : "Auto";
    }

    /// <summary>The "settings" object of a project (reference _apply_project_settings).</summary>
    public void ApplyProject(JsonObject? st)
    {
        if (st is null) return;
        Migrate(st);
        bool Put(ref string field, string key, IEnumerable<string>? allowed = null)
        {
            if (Str(st[key]) is { } v && (allowed is null || allowed.Contains(v))) { field = v; return true; }
            return false;
        }
        Put(ref Size, "size");
        string oldBitrate = Bitrate;
        if (Put(ref Bitrate, "bitrate"))                            // the Tk trace on the bitrate entry
        {
            FixedRate = Bitrate.Trim().Length > 0;
            if (!FixedRate && oldBitrate.Trim().Length > 0) KeptBitrate = oldBitrate;
            BitrateChanged();
        }
        Put(ref Res, "res", ResModes);
        Put(ref Codec, "codec", C.Codecs);
        Put(ref Crop, "crop", C.Aspects.Select(x => x.Name));
        Put(ref Audio, "audio", AudioRates);
        Put(ref Atrk, "atrk", AudioTrackModes);
        Put(ref Mode, "mode", ExportModes);
        Put(ref Trans, "trans", C.Transitions.Select(x => x.Name));
        Put(ref Tlen, "tlen");
        Put(ref FastAudio, "fast_audio", C.FastAudio);
        Put(ref SlowAudio, "slow_audio", C.SlowAudio);
        Put(ref SlowPitch, "slow_pitch", C.SlowPitch);
        Put(ref FastPitch, "fast_pitch", C.FastPitch);
        Put(ref ZoomFx, "zoom_fx", C.ZoomFx);
        Put(ref ZoomSnd, "zoom_snd", C.ZoomAudio);
        if (IsStr(st["zoom_len"]) || IsNumber(st["zoom_len"])) ZoomLen = PyStr(st["zoom_len"]);
        if (IsPlainInt(st["zoom_vol"])) ZoomVol = (int)Math.Max(0, Math.Min(200, AsLong(st["zoom_vol"]!)));
        if (IsPlainInt(st["fast_vol"])) FastVol = (int)Math.Max(0, Math.Min(200, AsLong(st["fast_vol"]!)));
        if (IsPlainInt(st["slow_vol"])) SlowVol = (int)Math.Max(0, Math.Min(200, AsLong(st["slow_vol"]!)));
        Put(ref SlowInterp, "slow_interp", C.SlowInterp);
        if (Bool(st["mute_mic"]) is { } mm) MuteMic = mm;
        if (Bool(st["vhs"]) is { } vh) Vhs = vh;
        if (Bool(st["hdr"]) is { } hd) Hdr = hd;
        // a project saved with picture adjustments brings them back; one without them opens with a neutral picture
        (PicBright, PicContrast, PicSat, PicDenoise, PicTemp, PicTint) = (0, 0, 0, 0, 0, 0);
        ReadPicture(st);
        (EqBands, EqVolume) = (new int[Modules.Equalizer.Bands.Length], 0);
        ReadEq(st);
        BitrateChanged();
    }

    public double? Ratio => C.Aspects.FirstOrDefault(x => x.Name == Crop).Ratio;

    /// <summary>The export settings start_export builds. Error = the message box text when the input is invalid.</summary>
    public ExportSettings? ToExportSettings(bool join, out string? error)
    {
        error = null;
        double? bitrate = null, targetMb = null;
        if (Bitrate.Trim().Length > 0 || FixedRate)
        {
            bitrate = BitrateMbps();
            if (bitrate is null)
            {
                error = "Enter the bitrate in Mbps, e.g. 20 (or pick Size limit).";
                return null;
            }
        }
        else
        {
            var t = Py.Py.ParseFloat(Size.Replace(",", "."));
            if (t is not { } tm || !(tm > 0)) { error = "Enter the max file size in MB, e.g. 50"; return null; }
            targetMb = tm;
        }
        double tlen = Py.Py.ParseFloat(Tlen.Replace(",", ".")) ?? 0.4;
        return new ExportSettings
        {
            TargetMb = targetMb, BitrateMbps = bitrate, ResMode = Res, Codec = Text.CodecKey(Codec), AudioK = Audio,
            Ratio = Ratio, Xfade = join ? C.TransitionMode(Trans) : null, T = tlen, Tonemap = Hdr,
            FastAudio = FastAudio, SlowAudio = SlowAudio, SlowPitch = SlowPitch, FastPitch = FastPitch, Vhs = Vhs,
            SlowInterp = SlowInterp, FastVol = FastVol, SlowVol = SlowVol, ZoomFx = ZoomFx, ZoomSnd = ZoomSnd,
            ZoomLen = ZoomLen.Replace(",", "."), ZoomVol = ZoomVol, MixAudio = Atrk.StartsWith("Mix"), AudioAf = EqFilter(),
        };
    }

    /// <summary>Settings that replace the global ones for one separately exported clip (its bitrate preset).</summary>
    public ExportSettings ClipOverride(ExportSettings st, Preset? p) =>
        p is null ? st : st with { BitrateMbps = p.Mbps, TargetMb = null, ResMode = Res == "Auto" ? "Original" : Res };

    /// <summary>The four SUMMARY lines of the export panel (reference _update_summary).</summary>
    public string[] SummaryLines(IReadOnlyList<ExportClip> clips)
    {
        int n = clips.Count;
        double total = TotalLength(clips, out bool joined);
        double? br = Bitrate.Trim().Length > 0 ? BitrateMbps() : null;
        string crop = Crop.Split(' ')[0];
        bool separate = Separate(n);
        // 2.9.1: the One video / Separate files switch decides (before: "when joined" whenever a transition was set)
        string l1 = n == 0 ? "No clips yet" : n == 1 ? $"1 clip  ·  {Text.FmtTime(total)}"
            : separate ? $"{n} clips  ·  {n} files" : $"{n} clips joined  ·  {Text.FmtTime(total)}";
        string l2 = $"{Text.CodecShort(Codec)}  ·  {Res} resolution  ·  " + (crop == "Original" ? "no crop" : $"crop {crop}");
        string l3;
        if (br is { } b)
        {
            long ak = AudioK();                               // ("Original" made long.Parse throw)
            double mb = Py.Py.Max(total, 0) * (b * 1e6 + ak * 1000) / 8 / 1048576;
            l3 = $"Fixed {Py.Py.G(b)} Mbps video" + (n > 0 ? $"  ·  about {Py.Py.F(mb, 0)} MB in total" : "");
        }
        else l3 = $"Max {(Size.Trim() is { Length: > 0 } s ? s : "?")} MB per file";
        string l4 = OutStem(n > 0 ? ExportPlanner.Stem(clips[0].Path) : "clip") + ".mp4" + (separate ? $" and {n - 1} more" : "");
        return [l1, l2, l3, l4];
    }

    /// <summary>2.9.1: several clips exported as one file each (the Export panel's "Separate files"); one clip is
    /// always one file.</summary>
    public bool Separate(int clipCount) => clipCount > 1 && Mode == "separate";

    /// <summary>The audio bitrate in kbit/s for size estimates (128 for "Original", whose rate is the clips' own).</summary>
    long AudioK() => long.TryParse(Audio.TrimEnd('k'), System.Globalization.NumberStyles.Integer,
                                   System.Globalization.CultureInfo.InvariantCulture, out var k) ? k : 128;

    /// <summary>The length of the exported video: every clip after its edits, minus the transitions' overlap.</summary>
    double TotalLength(IReadOnlyList<ExportClip> clips, out bool joined)
    {
        int n = clips.Count;
        double total = Py.Py.Sum(clips.Select(c => Timing.Duration(c.GetPieces())));
        joined = n > 1 && !Separate(n);
        if (joined && !string.IsNullOrEmpty(C.TransitionMode(Trans)))   // transitions overlap the clips
        {
            double T = Py.Py.ParseFloat(Tlen.Replace(",", ".")) is { } t ? Py.Py.Max(0.1, Py.Py.Min(3.0, t)) : 0.4;
            total -= (n - 1) * T;
        }
        return total;
    }

    /// <summary>2.9.1: the large line at the top of the SUMMARY, what the export promises. Size limit: "Under 50 MB" and
    /// the video bitrate that size leaves for this length. Fixed bitrate: the expected size, flagged (Warn) because no
    /// limit applies. Warn also flags a missing value or a size too small for the length.</summary>
    public (string Big, string Sub, bool Warn) SummaryResult(IReadOnlyList<ExportClip> clips)
    {
        int n = clips.Count;
        double total = TotalLength(clips, out _);
        long ak = AudioK();
        if (FixedRate || Bitrate.Trim().Length > 0)
        {
            if (BitrateMbps() is not { } b) return ("Enter a bitrate", "in Mbps, e.g. 20", true);
            if (n == 0 || total <= 0) return ($"Fixed {Py.Py.G(b)} Mbps", "no size limit", true);
            double mb = total * (b * 1e6 + ak * 1000) / 8 / 1048576;
            return ($"About {Py.Py.F(mb, 0)} MB", $"in total  ·  no size limit (fixed {Py.Py.G(b)} Mbps)", true);
        }
        if (Py.Py.ParseFloat(Size.Replace(",", ".")) is not { } size || !(size > 0)) return ("Enter a max size", "in MB, e.g. 50", true);
        string big = $"Under {Py.Py.G(size)} MB";
        if (n == 0 || total <= 0) return (big, "per file", false);
        // separate files: the longest clip gets the least bitrate per second
        bool separate = Separate(n);
        double len = separate ? clips.Max(c => Timing.Duration(c.GetPieces())) : total;
        double video = (size * 1048576 * 8 / len - ak * 1000) / 1e6;
        string forWhat = separate ? $"the longest clip ({Text.FmtTime(len)})" : Text.FmtTime(len);
        string what = separate ? "per file  ·  " : n > 1 ? "one video  ·  " : "";
        if (video < 0.1) return (big, $"{what}too small for {forWhat} of video", true);
        string v = video.ToString(video < 10 ? "0.0" : "0", System.Globalization.CultureInfo.InvariantCulture);
        return (big, $"{what}about {v} Mbps video for {forWhat}", false);
    }

    /// <summary>Writes these values into settings.json's object (the AppState part of save_all_settings);
    /// other keys are kept.</summary>
    public void WriteTo(JsonObject d)
    {
        d["size"] = Size; d["bitrate"] = Bitrate; d["bitrate_kept"] = KeptBitrate; d["crop"] = Crop; d["res"] = Res; d["res_before_bitrate"] = ResBeforeBitrate;
        d["codec"] = Codec; d["audio"] = Audio; d["atrk"] = Atrk; d["mute_mic"] = MuteMic;
        d["fast_audio"] = FastAudio; d["slow_audio"] = SlowAudio; d["slow_pitch"] = SlowPitch; d["fast_pitch"] = FastPitch;
        d["vhs"] = Vhs; d["fast_vol"] = FastVol; d["slow_vol"] = SlowVol; d["zoom_fx"] = ZoomFx; d["zoom_snd"] = ZoomSnd;
        d["zoom_len"] = ZoomLen; d["zoom_vol"] = ZoomVol; d["slow_interp"] = SlowInterp; d["hdr"] = Hdr; d["mode"] = Mode;
        d["trans"] = Trans; d["tlen"] = Tlen; d["prefix"] = Prefix; d["suffix"] = Suffix;
        d["keep_name"] = KeepName;
        d["pic_bright"] = PicBright; d["pic_contrast"] = PicContrast; d["pic_sat"] = PicSat; d["pic_denoise"] = PicDenoise;
        d["pic_temp"] = PicTemp; d["pic_tint"] = PicTint;
        d["eq_bands"] = new JsonArray(EqBands.Select(b => (JsonNode?)JsonValue.Create(b)).ToArray()); d["eq_vol"] = EqVolume;
    }

    /// <summary>clip -> prefix_clip_suffix (the output file name without extension). The prefix and suffix may hold
    /// dynamic labels ({date}, {time}, {preset}, {res}, {size}, {codec}, see NameTokens), filled in for this file.
    /// 2.2: an empty suffix adds nothing (1.0.8 - 2.1 added the codec name; {codec} does that now).
    /// 2.3: without "Keep original file name" the name is only prefix_suffix (the clip name is left out).</summary>
    public string OutStem(string stem, NameContext? ctx = null)
    {
        ctx ??= new NameContext(DateTime.Now);
        string pre = Text.CleanSuffix(NameTokens.Expand(Prefix, this, ctx));
        string suf = Text.CleanSuffix(NameTokens.Expand(Suffix, this, ctx));
        if (!KeepsName && string.Join("_", new[] { pre, suf }.Where(x => x.Length > 0)) is { Length: > 0 } only) return only;
        return (pre.Length > 0 ? pre + "_" : "") + stem + (suf.Length > 0 ? "_" + suf : "");
    }

    /// <summary>Prefix or suffix has text, so the clip name may be left out (otherwise the name would be empty).</summary>
    public bool HasNameText => Text.CleanSuffix(Prefix).Length > 0 || Text.CleanSuffix(Suffix).Length > 0;

    /// <summary>The clip name is used: ticked, or nothing else to name the file by.</summary>
    public bool KeepsName => KeepName || !HasNameText;

    /// <summary>HEVC / H264: the {codec} label.</summary>
    public string CodecTag => Codec.Contains("HEVC") ? "HEVC" : "H264";
}
