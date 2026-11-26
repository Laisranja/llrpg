using System;
using System.Collections;
using System.Text;
using UnityEngine;
using UnityEngine.Networking;

[Serializable]
public sealed class ChatMessage
{
    public string role;
    public string content;

    public ChatMessage(string role, string content)
    {
        this.role = role;
        this.content = content;
    }
}

[Serializable] internal sealed class OpenRouterReasoningOptions
{
    public bool exclude = true;
    public string effort = "low";
}

[Serializable] internal sealed class OpenRouterRequest
{
    public string model;
    public ChatMessage[] messages;
    public int max_tokens = 120;
    public float temperature = 0.7f;
    public OpenRouterReasoningOptions reasoning = new OpenRouterReasoningOptions();
}

[Serializable] internal sealed class OpenRouterChoice
{
    public ChatMessage message;
}

[Serializable] internal sealed class OpenRouterResponse
{
    public OpenRouterChoice[] choices;
}

public sealed class OpenRouterClient : MonoBehaviour
{
    [Header("API")]
    [SerializeField] private string apiUrl = "https://openrouter.ai/api/v1/chat/completions";
    [Tooltip("NUNCA salve a cena com a chave preenchida. Cole so durante o Play (o Unity descarta ao sair). Alternativa: variavel de ambiente OPENROUTER_API_KEY.")]
    [SerializeField] private string apiKey = "";
    [SerializeField] private string model = "meta-llama/llama-3.1-8b-instruct";
    [SerializeField] private int maxTokens = 300;
    [SerializeField, Range(0f, 1f)] private float temperature = 0.7f;
    [SerializeField] private int timeoutSeconds = 30;

    private string ResolveKey()
    {
        if (!string.IsNullOrWhiteSpace(apiKey)) return apiKey.Trim();
        return Environment.GetEnvironmentVariable("OPENROUTER_API_KEY");
    }

    // Rede de seguranca: avisa se a chave estiver gravada na cena fora do Play.
    private void OnValidate()
    {
        if (!Application.isPlaying && !string.IsNullOrWhiteSpace(apiKey))
            Debug.LogWarning("[SEGURANCA] Api Key preenchida fora do Play: ela sera salva na cena. Apague antes de commitar!", this);
    }

    public IEnumerator Chat(ChatMessage[] messages, Action<string> onSuccess, Action<string> onError)
    {
        string key = ResolveKey();
        if (string.IsNullOrWhiteSpace(key))
        {
            onError?.Invoke("sem chave de API");
            yield break;
        }

        OpenRouterRequest payload = new OpenRouterRequest
        {
            model = model,
            messages = messages,
            max_tokens = maxTokens,
            temperature = temperature,
        };

        byte[] jsonBytes = Encoding.UTF8.GetBytes(JsonUtility.ToJson(payload));

        using UnityWebRequest web = new UnityWebRequest(apiUrl, "POST");
        web.SetRequestHeader("Content-Type", "application/json");
        web.SetRequestHeader("Authorization", "Bearer " + key);
        web.uploadHandler = new UploadHandlerRaw(jsonBytes);
        web.downloadHandler = new DownloadHandlerBuffer();
        web.timeout = timeoutSeconds;

        yield return web.SendWebRequest();

        if (web.result != UnityWebRequest.Result.Success)
        {
            onError?.Invoke(web.responseCode + ": " + web.error);
            yield break;
        }

        OpenRouterResponse response;
        try { response = JsonUtility.FromJson<OpenRouterResponse>(web.downloadHandler.text); }
        catch (ArgumentException) { onError?.Invoke("resposta ilegivel"); yield break; }

        if (response?.choices == null || response.choices.Length == 0)
        {
            onError?.Invoke("resposta sem choices");
            yield break;
        }

        string content = response.choices[0].message?.content;
        if (string.IsNullOrEmpty(content))
        {
            onError?.Invoke("resposta vazia (max_tokens baixo ou so reasoning)");
            yield break;
        }

        onSuccess?.Invoke(content);
    }
}
