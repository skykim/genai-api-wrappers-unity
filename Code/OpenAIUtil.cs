using System;
using System.IO;
using System.Text;
using System.Threading.Tasks;
using UnityEngine;
using UnityEngine.Networking;

namespace OpenAI
{
    public static class OpenAIUtil
    {
        private const string GPTUrl = "https://api.openai.com/v1/chat/completions";
        private const string WhisperUrl = "https://api.openai.com/v1/audio/transcriptions";

        private static string OpenAIApiKey = "openai_api_key";
        private const int TimeOut = 30;

        public static async Task<string> InvokeChat(string prompt)
        {
            var req = new Request
            {
                model = "gpt-4o-mini",
                messages = new[]
                {
                    new RequestMessage { role = "user", content = prompt }
                }
            };

            string jsonBody = JsonUtility.ToJson(req);

            using (UnityWebRequest request = new UnityWebRequest(GPTUrl, "POST"))
            {
                byte[] bodyRaw = Encoding.UTF8.GetBytes(jsonBody);
                request.uploadHandler = new UploadHandlerRaw(bodyRaw);
                request.downloadHandler = new DownloadHandlerBuffer();
                
                request.SetRequestHeader("Content-Type", "application/json");
                request.SetRequestHeader("Authorization", "Bearer " + OpenAIApiKey);
                request.timeout = TimeOut;

                var operation = request.SendWebRequest();

                while (!operation.isDone) await Task.Yield();

                if (request.result == UnityWebRequest.Result.Success)
                {
                    var responseData = JsonUtility.FromJson<Response>(request.downloadHandler.text);
                    if (responseData.choices != null && responseData.choices.Length > 0)
                    {
                        return responseData.choices[0].message.content;
                    }
                }
                else
                {
                    Debug.LogError($"[OpenAI] Chat Error: {request.error}\n{request.downloadHandler.text}");
                }
            }

            return null;
        }

        public static async Task<string> InvokeWhisper(byte[] audioData)
        {
            WWWForm form = new WWWForm();
            form.AddBinaryData("file", audioData, "audio.wav", "audio/wav");
            form.AddField("model", "whisper-1");
            form.AddField("language", "ko");

            using (UnityWebRequest request = UnityWebRequest.Post(WhisperUrl, form))
            {
                request.SetRequestHeader("Authorization", $"Bearer {OpenAIApiKey}");
                request.timeout = TimeOut;

                var operation = request.SendWebRequest();
                while (!operation.isDone) await Task.Yield();

                if (request.result == UnityWebRequest.Result.Success)
                {
                    string response = request.downloadHandler.text;
                    var data = JsonUtility.FromJson<TranscriptionResponse>(response);
                    
                    if (data.text.Contains("mbc") || data.text.Contains("MBC") || string.IsNullOrWhiteSpace(data.text))
                    {
                        return "?";
                    }
                    return data.text;
                }
                else
                {
                    Debug.LogError($"[OpenAI] Whisper Error: {request.error}");
                    return null;
                }
            }
        }

        public static byte[] FromFloatToWav(float[] samples, int channels = 1, int frequency = 16000)
        {
            int sampleCount = samples.Length;
            int byteCount = sampleCount * 2;

            using (var stream = new MemoryStream())
            using (var writer = new BinaryWriter(stream))
            {
                writer.Write(Encoding.ASCII.GetBytes("RIFF"));
                writer.Write(36 + byteCount);
                writer.Write(Encoding.ASCII.GetBytes("WAVE"));

                writer.Write(Encoding.ASCII.GetBytes("fmt "));
                writer.Write(16);
                writer.Write((ushort)1);
                writer.Write((ushort)channels);
                writer.Write(frequency);
                writer.Write(frequency * channels * 2);
                writer.Write((ushort)(channels * 2));
                writer.Write((ushort)16);

                writer.Write(Encoding.ASCII.GetBytes("data"));
                writer.Write(byteCount);

                const float rescaleFactor = 32767;
                for (int i = 0; i < sampleCount; i++)
                {
                    short value = (short)(Mathf.Clamp(samples[i], -1f, 1f) * rescaleFactor);
                    writer.Write(value);
                }

                return stream.ToArray();
            }
        }

        [Serializable]
        public class TranscriptionResponse
        {
            public string text;
        }

        [Serializable]
        public struct Response
        {
            public string id;
            public ResponseChoice[] choices;
        }

        [Serializable]
        public struct ResponseChoice
        {
            public int index;
            public ResponseMessage message;
        }

        [Serializable]
        public struct ResponseMessage
        {
            public string role;
            public string content;
        }

        [Serializable]
        public struct Request
        {
            public string model;
            public RequestMessage[] messages;
        }

        [Serializable]
        public struct RequestMessage
        {
            public string role;
            public string content;
        }
    }
}