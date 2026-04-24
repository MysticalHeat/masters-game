namespace MastersGame.AI
{
    [System.Serializable]
    public class LlamaChatCompletionMessage
    {
        public string role;
        public string content;
    }

    [System.Serializable]
    public class LlamaChatCompletionRequest
    {
        public string model;
        public LlamaChatCompletionMessage[] messages;
        public int max_tokens;
        public float temperature;
        public float top_p;
        public bool stream;
    }

    [System.Serializable]
    public class LlamaChatCompletionResponse
    {
        public LlamaChatCompletionChoice[] choices;
    }

    [System.Serializable]
    public class LlamaChatCompletionStreamChunk
    {
        public LlamaChatCompletionChoice[] choices;
    }

    [System.Serializable]
    public class LlamaChatCompletionChoice
    {
        public LlamaChatCompletionMessage message;
        public LlamaChatCompletionDelta delta;
        public string finish_reason;
    }

    [System.Serializable]
    public class LlamaChatCompletionDelta
    {
        public string role;
        public string content;
    }
}
