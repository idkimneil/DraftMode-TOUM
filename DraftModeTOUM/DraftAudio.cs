using BepInEx;
using MiraAPI.LocalSettings;
using Reactor.Utilities;
using System;
using System.Collections;
using System.IO;
using System.Text;
using TownOfUs.Assets;
using TownOfUs.Utilities;
using UnityEngine;
using UnityEngine.Networking;

namespace DraftModeTOUM
{
    internal static class DraftAudio
    {
        private static AudioClip _customClip;
        private static bool _customClipFailed;
        private static bool _customClipLoading;

        public static void PlayDraftStartCue()
        {
            try
            {
                if (!Constants.ShouldPlaySfx() || SoundManager.Instance == null)
                    return;

                if (TryPlayCustomChime())
                    return;

                PlayDefaultCue();
            }
            catch (System.Exception ex)
            {
                DraftModePlugin.Logger?.LogWarning($"[DraftAudio] Failed to play draft-start cue: {ex.Message}");
            }
        }

        private static bool TryPlayCustomChime()
        {
            var localSettings = LocalSettingsTabSingleton<DraftModeLocalSettings>.Instance;
            if (localSettings == null || !localSettings.CustomChime.Value)
                return false;

            if (_customClip != null)
            {
                PlayClip(_customClip);
                return true;
            }

            if (_customClipFailed || _customClipLoading)
                return true;

            if (!TryResolveCustomChimePath(out var path, out var audioType))
                return false;

            _customClipLoading = true;
            Coroutines.Start(CoLoadAndPlayCustom(path, audioType));
            return true;
        }

        private static bool TryResolveCustomChimePath(out string path, out AudioType audioType)
        {
            path = null;
            audioType = AudioType.UNKNOWN;

            var root = Paths.ConfigPath;
            if (string.IsNullOrWhiteSpace(root))
                return false;

            var wave = Path.Combine(root, "draftchime.wave");
            var wav  = Path.Combine(root, "draftchime.wav");
            var mp3  = Path.Combine(root, "draftchime.mp3");

            if (File.Exists(wave))
            {
                path = wave;
                audioType = AudioType.WAV;
                return true;
            }
            if (File.Exists(wav))
            {
                path = wav;
                audioType = AudioType.WAV;
                return true;
            }
            if (File.Exists(mp3))
            {
                path = mp3;
                audioType = AudioType.MPEG;
                return true;
            }
            return false;
        }

        private static IEnumerator CoLoadAndPlayCustom(string path, AudioType audioType)
        {
            try
            {
                if (audioType == AudioType.WAV && TryLoadWavFromFile(path, out var wavClip))
                {
                    _customClip = wavClip;
                    _customClip.name = Path.GetFileName(path);
                    PlayClip(_customClip);
                    yield break;
                }

                var uri = new Uri(path).AbsoluteUri;
                var req = TryCreateAudioRequest(uri, audioType, out var reqError);
                if (req == null)
                {
                    _customClipFailed = true;
                    DraftModePlugin.Logger?.LogWarning(
                        $"[DraftAudio] Custom chime '{path}' could not be loaded: {reqError ?? "unsupported audio loader"}");
                    yield break;
                }

                try
                {
                    yield return req.SendWebRequest();

                    if (req.result != UnityWebRequest.Result.Success)
                    {
                        _customClipFailed = true;
                        DraftModePlugin.Logger?.LogWarning(
                            $"[DraftAudio] Failed to load custom chime '{path}': {req.error}");
                        yield break;
                    }

                    var dh = req.downloadHandler as DownloadHandlerAudioClip;
                    var clip = dh != null ? dh.audioClip : null;
                    _customClip = clip;
                    if (_customClip != null)
                    {
                        _customClip.name = Path.GetFileName(path);
                        PlayClip(_customClip);
                    }
                    else
                    {
                        _customClipFailed = true;
                        DraftModePlugin.Logger?.LogWarning(
                            $"[DraftAudio] Custom chime '{path}' loaded but AudioClip was null");
                    }
                }
                finally
                {
                    req.Dispose();
                }
            }
            finally
            {
                _customClipLoading = false;
            }
        }

        private static UnityWebRequest TryCreateAudioRequest(string uri, AudioType audioType, out string error)
        {
            error = null;
            try
            {
                return UnityWebRequestMultimedia.GetAudioClip(uri, audioType);
            }
            catch (Exception ex)
            {
                error = ex.Message;
                return null;
            }
        }

        private static bool TryLoadWavFromFile(string path, out AudioClip clip)
        {
            clip = null;
            try
            {
                var data = File.ReadAllBytes(path);
                if (data.Length < 44) return false;

                int offset = 0;
                if (!MatchFourCC(data, offset, "RIFF")) return false;
                offset += 4;
                offset += 4; // file size
                if (!MatchFourCC(data, offset, "WAVE")) return false;
                offset += 4;

                int channels = 0;
                int sampleRate = 0;
                int bitsPerSample = 0;
                int dataStart = 0;
                int dataSize = 0;
                int audioFormat = 0;

                while (offset + 8 <= data.Length)
                {
                    string chunkId = ReadFourCC(data, offset);
                    int chunkSize = ReadInt32LE(data, offset + 4);
                    offset += 8;
                    if (chunkId == "fmt ")
                    {
                        audioFormat = ReadInt16LE(data, offset);
                        channels = ReadInt16LE(data, offset + 2);
                        sampleRate = ReadInt32LE(data, offset + 4);
                        bitsPerSample = ReadInt16LE(data, offset + 14);
                    }
                    else if (chunkId == "data")
                    {
                        dataStart = offset;
                        dataSize = chunkSize;
                        break;
                    }
                    offset += chunkSize;
                }

                if (dataStart == 0 || dataSize <= 0 || channels <= 0 || sampleRate <= 0 || bitsPerSample <= 0)
                    return false;
                if (audioFormat != 1 && audioFormat != 3) return false; // PCM or IEEE float

                int bytesPerSample = bitsPerSample / 8;
                int totalSamples = dataSize / bytesPerSample;
                if (totalSamples <= 0) return false;

                var samples = new float[totalSamples];
                int idx = dataStart;
                for (int i = 0; i < totalSamples; i++)
                {
                    samples[i] = ReadSample(data, idx, bitsPerSample, audioFormat);
                    idx += bytesPerSample;
                }

                int samplesPerChannel = totalSamples / channels;
                clip = AudioClip.Create(Path.GetFileNameWithoutExtension(path), samplesPerChannel, channels, sampleRate, false);
                if (!clip.SetData(samples, 0)) return false;
                return true;
            }
            catch (Exception ex)
            {
                DraftModePlugin.Logger?.LogWarning($"[DraftAudio] WAV decode failed for '{path}': {ex.Message}");
                return false;
            }
        }

        private static float ReadSample(byte[] data, int index, int bitsPerSample, int format)
        {
            if (format == 3 && bitsPerSample == 32)
            {
                return BitConverter.ToSingle(data, index);
            }

            return bitsPerSample switch
            {
                8 => (data[index] - 128) / 128f,
                16 => BitConverter.ToInt16(data, index) / 32768f,
                24 => ReadInt24LE(data, index) / 8388608f,
                32 => BitConverter.ToInt32(data, index) / 2147483648f,
                _ => 0f
            };
        }

        private static int ReadInt24LE(byte[] data, int index)
        {
            int value = data[index] | (data[index + 1] << 8) | (data[index + 2] << 16);
            if ((value & 0x800000) != 0) value |= unchecked((int)0xFF000000);
            return value;
        }

        private static bool MatchFourCC(byte[] data, int index, string fourcc)
        {
            if (index + 4 > data.Length) return false;
            return data[index] == fourcc[0] &&
                   data[index + 1] == fourcc[1] &&
                   data[index + 2] == fourcc[2] &&
                   data[index + 3] == fourcc[3];
        }

        private static string ReadFourCC(byte[] data, int index)
        {
            if (index + 4 > data.Length) return string.Empty;
            var sb = new StringBuilder(4);
            sb.Append((char)data[index]);
            sb.Append((char)data[index + 1]);
            sb.Append((char)data[index + 2]);
            sb.Append((char)data[index + 3]);
            return sb.ToString();
        }

        private static int ReadInt32LE(byte[] data, int index) =>
            data[index] | (data[index + 1] << 8) | (data[index + 2] << 16) | (data[index + 3] << 24);

        private static int ReadInt16LE(byte[] data, int index) =>
            data[index] | (data[index + 1] << 8);

        private static void PlayDefaultCue()
        {
            var clip = TouAudio.QuestionSound.LoadAsset();
            clip ??= TouAudio.TrackerActivateSound.LoadAsset();
            if (clip == null)
                return;

            PlayClip(clip);
        }

        private static void PlayClip(AudioClip clip)
        {
            var source = SoundManager.Instance.PlaySound(clip, false, 1.05f);
            if (source != null)
            {
                source.pitch = 1.02f;
            }
        }
    }
}
