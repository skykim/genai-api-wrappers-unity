using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Threading.Tasks;
using UnityEngine;
using UnityEngine.Networking;

namespace ElevenLabs
{
    public class MusicResult
    {
        public AudioClip audioClip;
        public string lyrics;
    }

    public static class ElevenLabsUtil
    {
        private static string apiKey = "elevenlabs_api_key";
        private const string defaultVoiceId = "elevenlabs_voice_id";
        private const int timeOut = 120;
        private const string voiceBaseUrl = "https://api.elevenlabs.io/v1/text-to-speech";
        private const string musicBaseUrl = "https://api.elevenlabs.io/v1/music/detailed";

        public static void SetApiKey(string key)
        {
            apiKey = key;
        }

        public static async Task<AudioClip> GenerateVoice(string text, string voiceId = null, string modelId = "eleven_multilingual_v2")
        {
            if (string.IsNullOrEmpty(voiceId))
            {
                voiceId = defaultVoiceId;
            }

            string url = $"{voiceBaseUrl}/{voiceId}";

            TTSRequest req = new TTSRequest
            {
                text = text,
                model_id = modelId,
                voice_settings = new VoiceSettings
                {
                    stability = 0.5f,
                    similarity_boost = 0.8f,
                    style = 0.0f,
                    use_speaker_boost = true
                }
            };

            string jsonBody = JsonUtility.ToJson(req);

            using (UnityWebRequest request = new UnityWebRequest(url, "POST"))
            {
                byte[] bodyRaw = Encoding.UTF8.GetBytes(jsonBody);
                request.uploadHandler = new UploadHandlerRaw(bodyRaw);
                request.downloadHandler = new DownloadHandlerAudioClip(url, AudioType.MPEG);

                request.SetRequestHeader("Content-Type", "application/json");
                request.SetRequestHeader("xi-api-key", apiKey);
                request.timeout = timeOut;

                var operation = request.SendWebRequest();
                while (!operation.isDone) await Task.Yield();

                if (request.result == UnityWebRequest.Result.Success)
                {
                    return DownloadHandlerAudioClip.GetContent(request);
                }
                else
                {
                    Debug.LogError($"[ElevenLabs] Voice Error: {request.error}\n{request.downloadHandler.text}");
                    return null;
                }
            }
        }

        public static async Task<MusicResult> GenerateMusic(string prompt, int durationMs = 30000)
        {
            MusicRequest req = new MusicRequest 
            { 
                prompt = prompt, 
                music_length_ms = durationMs 
            };

            string jsonBody = JsonUtility.ToJson(req);

            using (UnityWebRequest request = new UnityWebRequest(musicBaseUrl, "POST"))
            {
                byte[] bodyRaw = Encoding.UTF8.GetBytes(jsonBody);
                request.uploadHandler = new UploadHandlerRaw(bodyRaw);
                request.downloadHandler = new DownloadHandlerBuffer();
                
                request.SetRequestHeader("Content-Type", "application/json");
                request.SetRequestHeader("xi-api-key", apiKey);
                request.timeout = timeOut;

                var operation = request.SendWebRequest();
                while (!operation.isDone) await Task.Yield();

                if (request.result == UnityWebRequest.Result.Success)
                {
                    var parsedData = ParseMultipartResponse(request);

                    if (parsedData.audioData == null || parsedData.audioData.Length == 0)
                    {
                        return null;
                    }

                    AudioClip clip = await ConvertBytesToAudioClip(parsedData.audioData);
                    
                    return new MusicResult
                    {
                        audioClip = clip,
                        lyrics = parsedData.lyrics
                    };
                }
                else
                {
                    Debug.LogError($"[ElevenLabs] Music Error: {request.error}\n{request.downloadHandler.text}");
                    return null;
                }
            }
        }

        private static async Task<AudioClip> ConvertBytesToAudioClip(byte[] audioData)
        {
            string tempPath = Path.Combine(Application.temporaryCachePath, "temp_music_" + Guid.NewGuid() + ".mp3");
            
            try
            {
                File.WriteAllBytes(tempPath, audioData);
                
                string url = "file://" + tempPath;
                using (UnityWebRequest uwr = UnityWebRequestMultimedia.GetAudioClip(url, AudioType.MPEG))
                {
                    ((DownloadHandlerAudioClip)uwr.downloadHandler).streamAudio = true;
                    var op = uwr.SendWebRequest();
                    while (!op.isDone) await Task.Yield();

                    if (uwr.result == UnityWebRequest.Result.Success)
                    {
                        return DownloadHandlerAudioClip.GetContent(uwr);
                    }
                    return null;
                }
            }
            finally
            {
                if (File.Exists(tempPath))
                {
                    File.Delete(tempPath);
                }
            }
        }

        private static (byte[] audioData, string lyrics) ParseMultipartResponse(UnityWebRequest request)
        {
            byte[] audioData = null;
            string lyricsText = "";

            string contentType = request.GetResponseHeader("Content-Type");
            if (string.IsNullOrEmpty(contentType) || !contentType.Contains("boundary=")) 
            {
                return (null, null);
            }

            string boundaryStr = contentType.Split(new string[] { "boundary=" }, StringSplitOptions.None)[1].Trim('"');
            byte[] boundaryBytes = Encoding.UTF8.GetBytes("--" + boundaryStr);
            
            List<byte[]> parts = SplitByteArray(request.downloadHandler.data, boundaryBytes);

            foreach (byte[] part in parts)
            {
                if (part.Length < 4) continue;
                
                int splitIndex = FindByteArrayPattern(part, new byte[] { 13, 10, 13, 10 });

                if (splitIndex != -1)
                {
                    string header = Encoding.UTF8.GetString(part, 0, splitIndex);
                    int bodyStart = splitIndex + 4;
                    int bodyLen = part.Length - bodyStart;
                    
                    if (bodyLen >= 2 && part[part.Length - 2] == 13 && part[part.Length - 1] == 10) bodyLen -= 2;

                    byte[] body = new byte[bodyLen];
                    Array.Copy(part, bodyStart, body, 0, bodyLen);

                    if (header.Contains("application/json"))
                    {
                        try 
                        {
                            string json = Encoding.UTF8.GetString(body);
                            var data = JsonUtility.FromJson<MusicJsonData>(json);
                            List<string> lines = new List<string>();
                            if(data.composition_plan?.sections != null)
                            {
                                foreach(var section in data.composition_plan.sections) 
                                    if(section.lines != null) lines.AddRange(section.lines);
                            }
                            lyricsText = string.Join("\n", lines);
                        } 
                        catch { }
                    }
                    else if (header.Contains("audio/mpeg"))
                    {
                        audioData = body;
                    }
                }
            }
            return (audioData, lyricsText);
        }

        private static List<byte[]> SplitByteArray(byte[] source, byte[] separator)
        {
            List<byte[]> parts = new List<byte[]>();
            int index = 0;
            while (index < source.Length)
            {
                int next = FindByteArrayPattern(source, separator, index);
                if (next == -1) {
                    byte[] rest = new byte[source.Length - index];
                    Array.Copy(source, index, rest, 0, rest.Length);
                    parts.Add(rest);
                    break;
                }
                if (next > index) {
                    byte[] chunk = new byte[next - index];
                    Array.Copy(source, index, chunk, 0, chunk.Length);
                    parts.Add(chunk);
                }
                index = next + separator.Length;
            }
            return parts;
        }

        private static int FindByteArrayPattern(byte[] source, byte[] pattern, int startIndex = 0)
        {
            for (int i = startIndex; i <= source.Length - pattern.Length; i++) {
                bool match = true;
                for (int j = 0; j < pattern.Length; j++) {
                    if (source[i + j] != pattern[j]) { match = false; break; }
                }
                if (match) return i;
            }
            return -1;
        }

        [Serializable]
        private class TTSRequest
        {
            public string text;
            public string model_id;
            public VoiceSettings voice_settings;
        }

        [Serializable]
        public class VoiceSettings
        {
            public float stability;
            public float similarity_boost;
            public float style;
            public bool use_speaker_boost;
            public float speed = 1.0f;
        }

        [Serializable]
        private class MusicRequest
        {
            public string prompt;
            public int music_length_ms;
        }

        [Serializable]
        private class MusicJsonData
        {
            public MusicCompositionPlan composition_plan;
        }

        [Serializable]
        private class MusicCompositionPlan
        {
            public MusicSection[] sections;
        }

        [Serializable]
        private class MusicSection
        {
            public string[] lines;
        }
    }
}