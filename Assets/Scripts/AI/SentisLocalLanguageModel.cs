using System;
using System.Threading;
using System.Threading.Tasks;
using Unity.InferenceEngine;
using Unity.InferenceEngine.Tokenization;
using Unity.InferenceEngine.Tokenization.Parsers.HuggingFace;
using UnityEngine;

namespace MastersGame.AI
{
    public class SentisLocalLanguageModel : MonoBehaviour, ILocalLanguageModel
    {
        [SerializeField] private string displayName = "Sentis Local Model";
        [SerializeField] private ModelAsset modelAsset;
        [SerializeField] private TextAsset tokenizerJson;

        private Model runtimeModel;
        private ITokenizer tokenizer;
        private string validationState = "No model configured.";

        public string DisplayName => displayName;

        public string StatusSummary => validationState;

        public bool IsConfigured => modelAsset != null && tokenizerJson != null;

        public async Task<string> GenerateReplyAsync(ChatRequest request, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();

            if (!IsConfigured)
            {
                validationState = "Sentis model is not configured. Assign a ModelAsset and tokenizer.json on the Systems object.";
                return "Sentis модель пока не подключена. Назначь ModelAsset и tokenizer.json, после чего можно будет подключать generation loop.";
            }

            try
            {
                EnsureInitialized();
            }
            catch (Exception exception)
            {
                validationState = $"Sentis setup failed: {exception.Message}";
                return $"Не удалось подготовить локальную модель: {exception.Message}";
            }

            await Task.Yield();
            cancellationToken.ThrowIfCancellationRequested();

            return $"Sentis assets for {request.NpcName} are assigned and validated. The remaining step is to replace this placeholder with the model-specific autoregressive token loop for your ONNX LLM.";
        }

        private void EnsureInitialized()
        {
            if (runtimeModel == null)
            {
                runtimeModel = ModelLoader.Load(modelAsset);
            }

            if (tokenizer == null)
            {
                var parser = HuggingFaceParser.GetDefault();
                tokenizer = parser.Parse(tokenizerJson.text);
            }

            validationState = $"Validated {displayName}. Model and tokenizer are loaded; generation loop is still a placeholder.";
        }
    }
}