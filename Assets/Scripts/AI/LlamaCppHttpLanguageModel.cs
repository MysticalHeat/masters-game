using System;
using System.Collections.Generic;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using UnityEngine;
using UnityEngine.Networking;

namespace MastersGame.AI
{
    public class LlamaCppHttpLanguageModel : MonoBehaviour, IStreamingLocalLanguageModel
    {
        [SerializeField] private string displayName = "llama.cpp";
        [SerializeField] private bool useAsPrimaryBackend;
        [SerializeField] private string baseUrl = "http://127.0.0.1:8080";
        [SerializeField] private string modelName = "qwen";
        [SerializeField] private int maxTokens = 128;
        [SerializeField] private float temperature = 0.3f;
        [SerializeField] private float topP = 0.9f;
        [SerializeField] private int requestTimeoutSeconds = 60;
        [SerializeField] private bool forceRussianResponses = true;

        private string statusSummary = "llama.cpp backend отключён.";

        public string DisplayName => displayName;

        public string StatusSummary => statusSummary;

        public bool IsConfigured => useAsPrimaryBackend && !string.IsNullOrWhiteSpace(baseUrl) && !string.IsNullOrWhiteSpace(modelName);

        public void Configure(string configuredBaseUrl, string configuredModelName, bool enableAsPrimaryBackend)
        {
            baseUrl = configuredBaseUrl;
            modelName = configuredModelName;
            useAsPrimaryBackend = enableAsPrimaryBackend;
        }

        public async Task<string> GenerateReplyAsync(ChatRequest request, CancellationToken cancellationToken)
        {
            return await GenerateReplyInternalAsync(request, false, null, cancellationToken);
        }

        public async Task<string> GenerateReplyStreamingAsync(ChatRequest request, Action<string> onPartialText, CancellationToken cancellationToken)
        {
            return await GenerateReplyInternalAsync(request, true, onPartialText, cancellationToken);
        }

        private async Task<string> GenerateReplyInternalAsync(ChatRequest request, bool stream, Action<string> onPartialText, CancellationToken cancellationToken)
        {
            if (!IsConfigured)
            {
                statusSummary = useAsPrimaryBackend
                    ? "Локальный llama.cpp backend не настроен. Укажи baseUrl и modelName."
                    : "llama.cpp backend отключён.";
                return useAsPrimaryBackend
                    ? "Модель llama.cpp не настроена. Проверь адрес сервера и имя модели."
                    : "llama.cpp backend отключён. Включи его в инспекторе, чтобы использовать локальный HTTP сервер.";
            }

            var endpoint = baseUrl.TrimEnd('/') + "/v1/chat/completions";
            var payload = new LlamaChatCompletionRequest
            {
                model = modelName,
                max_tokens = maxTokens,
                temperature = temperature,
                top_p = topP,
                stream = stream,
                messages = BuildMessages(request)
            };

            var json = JsonUtility.ToJson(payload);
            using var webRequest = new UnityWebRequest(endpoint, UnityWebRequest.kHttpVerbPOST);
            webRequest.uploadHandler = new UploadHandlerRaw(Encoding.UTF8.GetBytes(json));
            StreamingSseDownloadHandler streamingHandler = null;
            webRequest.downloadHandler = stream
                ? (streamingHandler = new StreamingSseDownloadHandler(onPartialText))
                : new DownloadHandlerBuffer();
            webRequest.SetRequestHeader("Content-Type", "application/json");
            webRequest.timeout = requestTimeoutSeconds;

            using var registration = cancellationToken.Register(() => webRequest.Abort());

            try
            {
                statusSummary = "Отправка запроса к llama.cpp...";
                await SendWebRequestAsync(webRequest, cancellationToken);

                if (cancellationToken.IsCancellationRequested)
                {
                    statusSummary = "Генерация отменена.";
                    return "Генерация отменена.";
                }

                if (webRequest.result != UnityWebRequest.Result.Success)
                {
                    statusSummary = BuildHttpFailureStatus(webRequest);
                    return GetUserFacingError(webRequest);
                }

                if (stream)
                {
                    var streamedReply = streamingHandler?.AccumulatedText;
                    if (string.IsNullOrWhiteSpace(streamedReply))
                    {
                        statusSummary = "llama.cpp вернул пустой или непонятный ответ.";
                        return "llama.cpp вернул пустой ответ.";
                    }

                    statusSummary = "Локальная модель llama.cpp готова.";
                    return streamedReply.Trim();
                }

                var response = JsonUtility.FromJson<LlamaChatCompletionResponse>(webRequest.downloadHandler.text);
                var reply = response != null && response.choices != null && response.choices.Length > 0
                    ? response.choices[0]?.message?.content
                    : null;

                if (string.IsNullOrWhiteSpace(reply))
                {
                    statusSummary = "llama.cpp вернул пустой или непонятный ответ.";
                    return "llama.cpp вернул пустой ответ.";
                }

                statusSummary = "Локальная модель llama.cpp готова.";
                return reply.Trim();
            }
            catch (OperationCanceledException)
            {
                statusSummary = "Генерация отменена.";
                return "Генерация отменена.";
            }
            catch (Exception exception)
            {
                statusSummary = $"Ошибка llama.cpp: {exception.Message}";
                return GetUserFacingError($"Ошибка llama.cpp: {exception.Message}");
            }
        }

        private LlamaChatCompletionMessage[] BuildMessages(ChatRequest request)
        {
            var messages = new List<LlamaChatCompletionMessage>
            {
                new()
                {
                    role = "system",
                    content = BuildSystemPrompt(request)
                }
            };

            foreach (var message in request.History)
            {
                if (message == null || string.IsNullOrWhiteSpace(message.Text))
                {
                    continue;
                }

                messages.Add(new LlamaChatCompletionMessage
                {
                    role = message.Role == ChatRole.Player ? "user" : "assistant",
                    content = message.Text.Trim()
                });
            }

            if (!string.IsNullOrWhiteSpace(request.PlayerMessage))
            {
                messages.Add(new LlamaChatCompletionMessage
                {
                    role = "user",
                    content = request.PlayerMessage.Trim()
                });
            }

            return messages.ToArray();
        }

        private string BuildSystemPrompt(ChatRequest request)
        {
            return NpcConversationSupport.BuildSystemPrompt(request, forceRussianResponses);
        }

        private static string GetUserFacingError(string message)
        {
            return message;
        }

        private static string GetUserFacingError(UnityWebRequest webRequest)
        {
            var body = webRequest.downloadHandler?.text;
            if (!string.IsNullOrWhiteSpace(body))
            {
                return $"Ошибка llama.cpp ({webRequest.responseCode}): {body}";
            }

            if (!string.IsNullOrWhiteSpace(webRequest.error))
            {
                return $"Ошибка сети llama.cpp ({webRequest.responseCode}): {webRequest.error}";
            }

            return $"Ошибка llama.cpp ({webRequest.responseCode}).";
        }

        private static string BuildHttpFailureStatus(UnityWebRequest webRequest)
        {
            if (!string.IsNullOrWhiteSpace(webRequest.error))
            {
                return $"Ошибка llama.cpp: {webRequest.error} (HTTP {webRequest.responseCode}).";
            }

            return $"llama.cpp вернул HTTP {webRequest.responseCode}.";
        }

        private static Task SendWebRequestAsync(UnityWebRequest webRequest, CancellationToken cancellationToken)
        {
            var taskCompletionSource = new TaskCompletionSource<bool>();
            var operation = webRequest.SendWebRequest();
            operation.completed += _ => taskCompletionSource.TrySetResult(true);

            if (cancellationToken.CanBeCanceled)
            {
                cancellationToken.Register(() => taskCompletionSource.TrySetCanceled(cancellationToken));
            }

            return taskCompletionSource.Task;
        }

        private sealed class StreamingSseDownloadHandler : DownloadHandlerScript
        {
            private readonly Action<string> onPartialText;
            private readonly Decoder decoder = Encoding.UTF8.GetDecoder();
            private readonly StringBuilder accumulatedText = new StringBuilder();
            private readonly StringBuilder pendingLine = new StringBuilder();
            private readonly char[] charBuffer = new char[4096];

            public StreamingSseDownloadHandler(Action<string> onPartialText)
            {
                this.onPartialText = onPartialText;
            }

            public string AccumulatedText => accumulatedText.ToString();

            protected override bool ReceiveData(byte[] data, int dataLength)
            {
                if (data == null || dataLength <= 0)
                {
                    return true;
                }

                var bytesUsed = 0;
                while (bytesUsed < dataLength)
                {
                    decoder.Convert(data, bytesUsed, dataLength - bytesUsed, charBuffer, 0, charBuffer.Length, false,
                        out var bytesUsedNow, out var charsUsed, out _);
                    bytesUsed += bytesUsedNow;
                    if (charsUsed > 0)
                    {
                        ProcessDecodedText(new string(charBuffer, 0, charsUsed));
                    }
                }

                return true;
            }

            protected override void CompleteContent()
            {
                decoder.Convert(Array.Empty<byte>(), 0, 0, charBuffer, 0, charBuffer.Length, true,
                    out _, out var charsUsed, out _);
                if (charsUsed > 0)
                {
                    ProcessDecodedText(new string(charBuffer, 0, charsUsed));
                }

                FlushPendingLine();
            }

            private void ProcessDecodedText(string text)
            {
                pendingLine.Append(text);

                while (true)
                {
                    var newlineIndex = IndexOfNewline(pendingLine);
                    if (newlineIndex < 0)
                    {
                        return;
                    }

                    var line = pendingLine.ToString(0, newlineIndex).TrimEnd('\r');
                    pendingLine.Remove(0, newlineIndex + 1);
                    ProcessLine(line);
                }
            }

            private void FlushPendingLine()
            {
                if (pendingLine.Length == 0)
                {
                    return;
                }

                ProcessLine(pendingLine.ToString().TrimEnd('\r'));
                pendingLine.Clear();
            }

            private void ProcessLine(string line)
            {
                if (string.IsNullOrWhiteSpace(line) || line.StartsWith(":", StringComparison.Ordinal))
                {
                    return;
                }

                if (!line.StartsWith("data:", StringComparison.OrdinalIgnoreCase))
                {
                    return;
                }

                var data = line.Substring(5).Trim();
                if (string.Equals(data, "[DONE]", StringComparison.OrdinalIgnoreCase))
                {
                    return;
                }

                var chunk = JsonUtility.FromJson<LlamaChatCompletionStreamChunk>(data);
                var delta = chunk != null && chunk.choices != null && chunk.choices.Length > 0
                    ? chunk.choices[0]?.delta?.content
                    : null;

                if (string.IsNullOrEmpty(delta))
                {
                    return;
                }

                accumulatedText.Append(delta);
                onPartialText?.Invoke(accumulatedText.ToString());
            }

            private static int IndexOfNewline(StringBuilder builder)
            {
                for (var i = 0; i < builder.Length; i++)
                {
                    if (builder[i] == '\n')
                    {
                        return i;
                    }
                }

                return -1;
            }
        }
    }
}
