using UnityEngine;
using UnityEngine.Networking;
using System.Threading.Tasks;
using System.Text;
using System.IO;
using System;
using NLayer; 

public static class MiniMaxUtil
{
    private static string MiniMaxUrl = "https://api.minimax.io/v1/t2a_v2";
    private static string MiniMaxApiKey = "minimax_api_key";

    private const int timeOut = 120;

    public static async Task<AudioClip> GenerateAudio(string text)
    {
        System.Diagnostics.Stopwatch sw = System.Diagnostics.Stopwatch.StartNew();

        MinimaxRequest req = new MinimaxRequest
        {
            model = "speech-2.6-turbo",
            text = text,
            stream = false,
            language_boost = "auto",
            output_format = "hex",
            voice_setting = new VoiceSetting
            {
                voice_id = APISettings.VoiceID
            }
        };

        string jsonBody = JsonUtility.ToJson(req);
        byte[] bodyRaw = Encoding.UTF8.GetBytes(jsonBody);

        using (UnityWebRequest request = new UnityWebRequest(MiniMaxUrl, "POST"))
        {
            request.uploadHandler = new UploadHandlerRaw(bodyRaw);
            request.downloadHandler = new DownloadHandlerBuffer();
            request.timeout = timeOut;

            request.SetRequestHeader("Authorization", $"Bearer {MiniMaxApiKey}");
            request.SetRequestHeader("Content-Type", "application/json");

            await request.SendWebRequest();

            if (request.result != UnityWebRequest.Result.Success)
            {
                Debug.LogError($"[Minimax] Network Error: {request.error}");
                return null;
            }

            string responseText = request.downloadHandler.text;
            
            try 
            {
                MinimaxResponse responseData = JsonUtility.FromJson<MinimaxResponse>(responseText);

                if (responseData.base_resp != null && responseData.base_resp.status_code != 0)
                {
                    Debug.LogError($"[Minimax] API Error: {responseData.base_resp.status_msg}");
                    return null;
                }

                string hexAudio = responseData.data?.audio;
                if (string.IsNullOrEmpty(hexAudio)) return null;

                var audioResult = await Task.Run(() => 
                {
                    byte[] audioBytes = FastHexToByteArray(hexAudio);

                    using (var memStream = new MemoryStream(audioBytes))
                    {
                        var mpgFile = new MpegFile(memStream);
                        float[] samples = new float[mpgFile.Length];
                        mpgFile.ReadSamples(samples, 0, (int)mpgFile.Length);
                        
                        return new { 
                            Samples = samples, 
                            Channels = mpgFile.Channels, 
                            Frequency = mpgFile.SampleRate
                        };
                    }
                });

                AudioClip clip = AudioClip.Create("MinimaxTTS", 
                    audioResult.Samples.Length / audioResult.Channels, 
                    audioResult.Channels, 
                    audioResult.Frequency, 
                    false);
                
                clip.SetData(audioResult.Samples, 0);

                sw.Stop();
                Debug.Log($"[Minimax] Audio Generated! Total Time: {sw.Elapsed.TotalSeconds:F4}s");

                return clip;
            }
            catch (Exception ex)
            {
                Debug.LogError($"[Minimax] Exception: {ex.Message}");
                return null;
            }
        }
    }

    private static byte[] FastHexToByteArray(string hex)
    {
        if (hex.Length % 2 != 0) throw new ArgumentException("Hex string must have an even length");
        byte[] bytes = new byte[hex.Length / 2];
        for (int i = 0; i < hex.Length; i += 2)
        {
            bytes[i / 2] = (byte)((GetHexVal(hex[i]) << 4) + GetHexVal(hex[i + 1]));
        }
        return bytes;
    }

    private static int GetHexVal(char hex)
    {
        int val = (int)hex;
        return val - (val < 58 ? 48 : (val < 97 ? 55 : 87));
    }
    
    [Serializable] private class VoiceSetting { public string voice_id; }
    [Serializable] private class MinimaxRequest { public string model; public string text; public bool stream; public string language_boost; public string output_format; public VoiceSetting voice_setting; }
    [Serializable] private class BaseResp { public int status_code; public string status_msg; }
    [Serializable] private class Data { public string audio; public int status; public bool is_end; }
    [Serializable] private class MinimaxResponse { public BaseResp base_resp; public Data data; }
}