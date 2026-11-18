using DualSenseStudio.Core.Haptics;
using NAudio.Wave;

// WasapiLoopbackCapture still works for system-wide loopback; the replacement builder API is newer than we need.
#pragma warning disable CS0618

namespace DualSenseStudio.Services;

/// <summary>
/// Listens to whatever Windows is playing (WASAPI loopback) and turns it into vibration:
/// bass drives the heavy left side, treble the light right side.
/// </summary>
public sealed class AudioReactiveSource : IHapticSource, IDisposable
{
    private WasapiLoopbackCapture? _capture;
    private float _lowState, _bassEnv, _trebleEnv;
    private volatile float _bassLevel, _trebleLevel;
    private float _outL, _outR;

    public string DisplayName => "Audio reactive";
    public double Gain { get; set; } = 1.5;
    public double BassWeight { get; set; } = 1.0;
    public double TrebleWeight { get; set; } = 0.6;
    /// <summary>Below this level the output is silent (ignores background hiss).</summary>
    public double Gate { get; set; } = 0.03;

    public float BassLevel => _bassLevel;
    public float TrebleLevel => _trebleLevel;
    public bool IsRunning => _capture != null;
    public string? Error { get; private set; }

    public void Start()
    {
        if (_capture != null) return;
        try
        {
            _capture = new WasapiLoopbackCapture();
            _capture.DataAvailable += OnData;
            _capture.StartRecording();
            Error = null;
        }
        catch (Exception ex)
        {
            Error = ex.Message;
            _capture = null;
        }
    }

    public void Stop()
    {
        var c = _capture;
        _capture = null;
        if (c == null) return;
        try
        {
            c.DataAvailable -= OnData;
            c.StopRecording();
            c.Dispose();
        }
        catch { }
        _bassLevel = _trebleLevel = 0;
    }

    private void OnData(object? sender, WaveInEventArgs e)
    {
        var fmt = _capture?.WaveFormat;
        if (fmt == null || e.BytesRecorded == 0) return;
        int channels = Math.Max(1, fmt.Channels);
        float sampleRate = fmt.SampleRate;
        // One-pole low-pass around 150 Hz splits bass from the rest.
        float a = 1f - MathF.Exp(-2f * MathF.PI * 150f / sampleRate);
        double bassSum = 0, trebleSum = 0;
        int frames = 0;

        if (fmt.Encoding == WaveFormatEncoding.IeeeFloat || (fmt.Encoding == WaveFormatEncoding.Extensible && fmt.BitsPerSample == 32))
        {
            var samples = System.Runtime.InteropServices.MemoryMarshal.Cast<byte, float>(e.Buffer.AsSpan(0, e.BytesRecorded));
            for (int i = 0; i + channels - 1 < samples.Length; i += channels)
            {
                float m = 0;
                for (int c = 0; c < channels; c++) m += samples[i + c];
                m /= channels;
                Accumulate(m, a, ref bassSum, ref trebleSum);
                frames++;
            }
        }
        else if (fmt.BitsPerSample == 16)
        {
            var samples = System.Runtime.InteropServices.MemoryMarshal.Cast<byte, short>(e.Buffer.AsSpan(0, e.BytesRecorded));
            for (int i = 0; i + channels - 1 < samples.Length; i += channels)
            {
                float m = 0;
                for (int c = 0; c < channels; c++) m += samples[i + c] / 32768f;
                m /= channels;
                Accumulate(m, a, ref bassSum, ref trebleSum);
                frames++;
            }
        }
        if (frames == 0) return;

        float bass = (float)Math.Sqrt(bassSum / frames);
        float treble = (float)Math.Sqrt(trebleSum / frames);
        // Fast attack, slower release.
        _bassEnv = bass > _bassEnv ? bass : _bassEnv * 0.85f + bass * 0.15f;
        _trebleEnv = treble > _trebleEnv ? treble : _trebleEnv * 0.8f + treble * 0.2f;
        _bassLevel = Math.Clamp(_bassEnv * 3f, 0, 1);
        _trebleLevel = Math.Clamp(_trebleEnv * 4f, 0, 1);
    }

    private void Accumulate(float m, float a, ref double bassSum, ref double trebleSum)
    {
        _lowState += a * (m - _lowState);
        float hi = m - _lowState;
        bassSum += _lowState * _lowState;
        trebleSum += hi * hi;
    }

    public HapticFrame GetFrame(in HapticContext ctx)
    {
        float g = (float)Gain;
        float l = _bassLevel * (float)BassWeight * g;
        float r = _trebleLevel * (float)TrebleWeight * g;
        if (l < Gate) l = 0;
        if (r < Gate) r = 0;
        // Light smoothing at engine rate to avoid zipper noise.
        _outL += (Math.Clamp(l, 0, 1) - _outL) * 0.5f;
        _outR += (Math.Clamp(r, 0, 1) - _outR) * 0.5f;
        return HapticFrame.Motors(_outL, _outR);
    }

    public void Dispose() => Stop();
}
