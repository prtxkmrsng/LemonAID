using System;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Threading.Tasks;
using Windows.Devices.Haptics;

namespace LemonAID.Services;

public enum FeedbackCue
{
    WedgeTick,
    WheelSummon,
    ModeSwitch,
    ExecuteSuccess,
    Dismiss
}

public static class FeedbackService
{
    private const uint SND_ASYNC = 0x0001;
    private const uint SND_NODEFAULT = 0x0002;
    private const uint SND_MEMORY = 0x0004;

    // Pinned buffers so winmm.dll can play them asynchronously without GC relocation
    private static readonly IntPtr _tickWavPtr;
    private static readonly IntPtr _summonWavPtr;
    private static readonly IntPtr _modeWavPtr;
    private static readonly IntPtr _successWavPtr;
    private static readonly IntPtr _dismissWavPtr;

    private static SimpleHapticsController? _hapticsController;
    private static SimpleHapticsControllerFeedback? _clickWaveform;
    private static SimpleHapticsControllerFeedback? _pressWaveform;
    private static bool _hapticsInitialized;

    static FeedbackService()
    {
        _tickWavPtr = PinBuffer(SynthesizeToneWav(startFreq: 1350, endFreq: 420, durationMs: 11, volume: 0.22));
        _summonWavPtr = PinBuffer(SynthesizeToneWav(startFreq: 480, endFreq: 860, durationMs: 48, volume: 0.28));
        _modeWavPtr = PinBuffer(SynthesizeToneWav(startFreq: 640, endFreq: 960, durationMs: 32, volume: 0.25));
        _successWavPtr = PinBuffer(SynthesizeChordWav(freq1: 784.0, freq2: 1174.6, durationMs: 95, volume: 0.30));
        _dismissWavPtr = PinBuffer(SynthesizeToneWav(startFreq: 390, endFreq: 210, durationMs: 36, volume: 0.22));

        _ = InitializeHapticsAsync();
    }

    private static IntPtr PinBuffer(byte[] wavBytes)
    {
        GCHandle handle = GCHandle.Alloc(wavBytes, GCHandleType.Pinned);
        return handle.AddrOfPinnedObject();
    }

    private static async Task InitializeHapticsAsync()
    {
        if (_hapticsInitialized) return;
        _hapticsInitialized = true;

        try
        {
            var access = await VibrationDevice.RequestAccessAsync();
            if (access != VibrationAccessStatus.Allowed) return;

            var device = await VibrationDevice.GetDefaultAsync();
            if (device?.SimpleHapticsController == null) return;

            _hapticsController = device.SimpleHapticsController;
            _clickWaveform = _hapticsController.SupportedFeedback.FirstOrDefault(
                f => f.Waveform == KnownSimpleHapticsControllerWaveforms.Click);
            _pressWaveform = _hapticsController.SupportedFeedback.FirstOrDefault(
                f => f.Waveform == KnownSimpleHapticsControllerWaveforms.Press) ?? _clickWaveform;
        }
        catch
        {
            // Haptic hardware not present or unsupported on this system
        }
    }

    public static void Play(FeedbackCue cue)
    {
        var settings = BackgroundSettingsService.Current;

        if (settings.SoundFeedbackEnabled)
        {
            IntPtr ptr = cue switch
            {
                FeedbackCue.WedgeTick => _tickWavPtr,
                FeedbackCue.WheelSummon => _summonWavPtr,
                FeedbackCue.ModeSwitch => _modeWavPtr,
                FeedbackCue.ExecuteSuccess => _successWavPtr,
                FeedbackCue.Dismiss => _dismissWavPtr,
                _ => _tickWavPtr
            };

            if (ptr != IntPtr.Zero)
            {
                PlaySoundW(ptr, IntPtr.Zero, SND_MEMORY | SND_ASYNC | SND_NODEFAULT);
            }
        }

        if (settings.HapticFeedbackEnabled)
        {
            TriggerHapticPulse(cue);
        }
    }

    private static void TriggerHapticPulse(FeedbackCue cue)
    {
        try
        {
            if (_hapticsController != null)
            {
                var waveform = cue is FeedbackCue.ExecuteSuccess or FeedbackCue.WheelSummon
                    ? _pressWaveform
                    : _clickWaveform;

                if (waveform != null)
                {
                    double intensity = cue switch
                    {
                        FeedbackCue.WedgeTick => 0.45,
                        FeedbackCue.ModeSwitch => 0.65,
                        FeedbackCue.WheelSummon => 0.80,
                        FeedbackCue.ExecuteSuccess => 0.95,
                        _ => 0.40
                    };
                    _hapticsController.SendHapticFeedback(waveform, intensity);
                    return;
                }
            }

            // Optional XInput gamepad tactile pulse fallback
            _ = PulseXInputControllerAsync(cue);
        }
        catch
        {
            // Ignore haptic errors
        }
    }

    private static async Task PulseXInputControllerAsync(FeedbackCue cue)
    {
        try
        {
            ushort speed = cue switch
            {
                FeedbackCue.WedgeTick => 6500,
                FeedbackCue.ModeSwitch => 11000,
                FeedbackCue.ExecuteSuccess => 18000,
                _ => 8000
            };

            var vib = new XINPUT_VIBRATION { wLeftMotorSpeed = 0, wRightMotorSpeed = speed };
            if (XInputSetState(0, ref vib) == 0)
            {
                await Task.Delay(cue == FeedbackCue.WedgeTick ? 12 : 28);
                var stop = new XINPUT_VIBRATION { wLeftMotorSpeed = 0, wRightMotorSpeed = 0 };
                XInputSetState(0, ref stop);
            }
        }
        catch
        {
            // Ignore if xinput1_4.dll is unavailable
        }
    }

    private static byte[] SynthesizeToneWav(double startFreq, double endFreq, int durationMs, double volume)
    {
        const int sampleRate = 44100;
        int numSamples = Math.Max(1, (sampleRate * durationMs) / 1000);

        using var ms = new MemoryStream(44 + numSamples * 2);
        using var bw = new BinaryWriter(ms);

        WriteWavHeader(bw, sampleRate, numSamples);

        double phase = 0.0;
        for (int i = 0; i < numSamples; i++)
        {
            double t = (double)i / numSamples;
            double freq = startFreq + (endFreq - startFreq) * t;
            phase += (2.0 * Math.PI * freq) / sampleRate;

            // Smooth attack and exponential decay envelope to prevent speaker popping
            double attack = Math.Min(1.0, i / 48.0);
            double decay = Math.Exp(-4.8 * t);
            double sample = Math.Sin(phase) * attack * decay * volume;

            short pcm = (short)Math.Clamp(sample * short.MaxValue, short.MinValue, short.MaxValue);
            bw.Write(pcm);
        }

        return ms.ToArray();
    }

    private static byte[] SynthesizeChordWav(double freq1, double freq2, int durationMs, double volume)
    {
        const int sampleRate = 44100;
        int numSamples = Math.Max(1, (sampleRate * durationMs) / 1000);

        using var ms = new MemoryStream(44 + numSamples * 2);
        using var bw = new BinaryWriter(ms);

        WriteWavHeader(bw, sampleRate, numSamples);

        for (int i = 0; i < numSamples; i++)
        {
            double t = (double)i / numSamples;
            double activeFreq = t < 0.42 ? freq1 : freq2;
            double localT = t < 0.42 ? (t / 0.42) : ((t - 0.42) / 0.58);

            double attack = Math.Min(1.0, (localT * numSamples) / 60.0);
            double decay = Math.Exp(-3.8 * localT);
            double sample = Math.Sin(2.0 * Math.PI * activeFreq * i / sampleRate) * attack * decay * volume;

            short pcm = (short)Math.Clamp(sample * short.MaxValue, short.MinValue, short.MaxValue);
            bw.Write(pcm);
        }

        return ms.ToArray();
    }

    private static void WriteWavHeader(BinaryWriter bw, int sampleRate, int numSamples)
    {
        int dataSize = numSamples * 2;
        bw.Write("RIFF"u8);
        bw.Write(36 + dataSize);
        bw.Write("WAVE"u8);
        bw.Write("fmt "u8);
        bw.Write(16);
        bw.Write((short)1); // PCM
        bw.Write((short)1); // Mono
        bw.Write(sampleRate);
        bw.Write(sampleRate * 2);
        bw.Write((short)2);
        bw.Write((short)16);
        bw.Write("data"u8);
        bw.Write(dataSize);
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct XINPUT_VIBRATION
    {
        public ushort wLeftMotorSpeed;
        public ushort wRightMotorSpeed;
    }

    [DllImport("winmm.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool PlaySoundW(IntPtr pszSound, IntPtr hmod, uint fdwSound);

    [DllImport("xinput1_4.dll", EntryPoint = "XInputSetState")]
    private static extern uint XInputSetState(uint dwUserIndex, ref XINPUT_VIBRATION pVibration);
}
