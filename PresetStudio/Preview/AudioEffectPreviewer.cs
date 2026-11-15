using System.IO;
using NAudio.Wave;
using PresetStudio.Settings;
using YukkuriMovieMaker.Player.Audio.Effects;
using YukkuriMovieMaker.Plugin.Effects;

namespace PresetStudio.Preview
{
    internal sealed record AudioSample(float[] Samples, int Hz);

    internal static class AudioEffectPreviewer
    {
        private const int DefaultHz = 48000;
        private const double MaxSourceSeconds = 8;
        private const double MaxTailSeconds = 4;

        private static readonly Lock locker = new();
        private static AudioSample? defaultSample;
        private static (string Path, AudioSample Sample)? fileSample;
        private static WaveOut? output;

        public static object? PlayingKey { get; private set; }

        public static event Action? PlaybackChanged;

        public static AudioSample GetSample()
        {
            var path = PresetStudioSettings.Default.AudioEffectSamplePath;
            if (path.Length > 0 && File.Exists(path))
            {
                lock (locker)
                {
                    if (fileSample is { } cached && cached.Path == path)
                        return cached.Sample;
                }

                try
                {
                    var loaded = LoadFile(path);
                    lock (locker)
                        fileSample = (path, loaded);
                    return loaded;
                }
                catch
                {
                }
            }

            lock (locker)
                return defaultSample ??= CreateDefaultSample();
        }

        private static AudioSample LoadFile(string path)
        {
            using var reader = new AudioFileReader(path);
            var channels = reader.WaveFormat.Channels;
            var hz = reader.WaveFormat.SampleRate;
            var limit = (int)(hz * channels * MaxSourceSeconds) * sizeof(float);
            var bytes = new byte[limit];
            var readBytes = 0;
            int count;
            while (readBytes < limit && (count = reader.Read(bytes, readBytes, Math.Min(16384, limit - readBytes))) > 0)
                readBytes += count;

            var read = readBytes / sizeof(float);
            var buffer = new float[read];
            Buffer.BlockCopy(bytes, 0, buffer, 0, read * sizeof(float));

            var frames = read / channels;
            var stereo = new float[frames * 2];
            for (var i = 0; i < frames; i++)
            {
                var left = buffer[i * channels];
                var right = channels > 1 ? buffer[i * channels + 1] : left;
                stereo[i * 2] = left;
                stereo[i * 2 + 1] = right;
            }

            return new AudioSample(stereo, hz);
        }

        private static AudioSample CreateDefaultSample()
        {
            double[] notes = [659.25, 523.25, 587.33, 392.00, 392.00, 587.33, 659.25, 523.25];
            double[] ratios = [1.0, 2.76, 5.40, 8.93];
            double[] amplitudes = [1.0, 0.45, 0.22, 0.1];
            double[] decays = [1.1, 1.8, 3.2, 5.0];
            const double noteSeconds = 0.55;
            const double tailSeconds = 2.2;
            var frames = (int)(DefaultHz * (notes.Length * noteSeconds + tailSeconds));
            var samples = new float[frames * 2];
            for (var n = 0; n < notes.Length; n++)
            {
                var start = (int)(n * noteSeconds * DefaultHz);
                var pan = n % 2 == 0 ? 0.35 : 0.65;
                for (var i = start; i < frames; i++)
                {
                    var t = (double)(i - start) / DefaultHz;
                    var value = 0.0;
                    for (var k = 0; k < ratios.Length; k++)
                        value += amplitudes[k] * Math.Exp(-t * decays[k]) * Math.Sin(2 * Math.PI * notes[n] * ratios[k] * t);
                    var sample = value * Math.Min(1, t / 0.002) * 0.16;
                    samples[i * 2] += (float)(sample * (1 - pan) * 2);
                    samples[i * 2 + 1] += (float)(sample * pan * 2);
                }
            }

            return new AudioSample(samples, DefaultHz);
        }

        public static float[] Process(IReadOnlyList<IAudioEffect> effects, AudioSample sample)
        {
            var duration = TimeSpan.FromSeconds((double)sample.Samples.Length / 2 / sample.Hz);
            var processors = new List<IAudioEffectProcessor>();
            IAudioStream stream = new SampleStream(sample);
            try
            {
                foreach (var effect in effects.Where(effect => effect.IsEnabled))
                {
                    var processor = effect.CreateAudioEffect(duration);
                    processor.Input = stream;
                    processors.Add(processor);
                    stream = processor;
                }

                stream.Seek(0);
                var limit = sample.Samples.Length + (int)(sample.Hz * 2 * MaxTailSeconds);
                var result = new float[limit];
                var read = 0;
                int count;
                while (read < limit && (count = stream.Read(result, read, Math.Min(4096, limit - read))) > 0)
                    read += count;

                var end = read - read % 2;
                while (end > sample.Samples.Length && Math.Abs(result[end - 1]) < 0.0005f && Math.Abs(result[end - 2]) < 0.0005f)
                    end -= 2;

                Array.Resize(ref result, end);
                return result;
            }
            finally
            {
                foreach (var processor in processors)
                {
                    try
                    {
                        processor.Dispose();
                    }
                    catch
                    {
                    }
                }
            }
        }

        public static void Play(object key, float[] samples, int hz)
        {
            Stop();
            var bytes = new byte[samples.Length * sizeof(float)];
            Buffer.BlockCopy(samples, 0, bytes, 0, bytes.Length);
            var provider = new RawSourceWaveStream(new MemoryStream(bytes), WaveFormat.CreateIeeeFloatWaveFormat(hz, 2));
            var player = new WaveOut();
            player.Init(provider);
            player.Volume = (float)Math.Clamp(PresetStudioSettings.Default.AudioPreviewVolume / 100, 0, 1);
            player.PlaybackStopped += (_, _) =>
            {
                if (!ReferenceEquals(output, player))
                    return;

                output = null;
                PlayingKey = null;
                player.Dispose();
                PlaybackChanged?.Invoke();
            };
            output = player;
            PlayingKey = key;
            player.Play();
            PlaybackChanged?.Invoke();
        }

        public static void SetVolume(double volume)
        {
            if (output is { } player)
                player.Volume = (float)Math.Clamp(volume / 100, 0, 1);
        }

        public static void Stop()
        {
            if (output is not { } player)
                return;

            output = null;
            PlayingKey = null;
            player.Stop();
            player.Dispose();
            PlaybackChanged?.Invoke();
        }

        public static double[] GetPeaks(float[] samples, int columns)
        {
            var peaks = new double[columns];
            var frames = samples.Length / 2;
            if (frames == 0)
                return peaks;

            for (var column = 0; column < columns; column++)
            {
                var start = (int)((long)frames * column / columns);
                var end = Math.Max(start + 1, (int)((long)frames * (column + 1) / columns));
                var peak = 0.0;
                for (var i = start; i < end && i < frames; i++)
                    peak = Math.Max(peak, Math.Max(Math.Abs(samples[i * 2]), Math.Abs(samples[i * 2 + 1])));
                peaks[column] = Math.Min(1, peak);
            }

            return peaks;
        }

        private sealed class SampleStream(AudioSample sample) : IAudioStream
        {
            private long position;

            public int Hz => sample.Hz;

            public long Position => position;

            public long Duration => sample.Samples.Length;

            public void Seek(TimeSpan time) => Seek((long)(time.TotalSeconds * sample.Hz) * 2);

            public void Seek(long position) => this.position = Math.Clamp(position - position % 2, 0, sample.Samples.Length);

            public int Read(float[] destBuffer, int offset, int count)
            {
                var available = (int)Math.Max(0, Math.Min(count, sample.Samples.Length - position));
                if (available > 0)
                    Array.Copy(sample.Samples, position, destBuffer, offset, available);
                if (available < count)
                    Array.Clear(destBuffer, offset + available, count - available);
                position += count;
                return count;
            }

            public void Dispose()
            {
            }
        }
    }
}
