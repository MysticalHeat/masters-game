using System;
using System.Collections.Generic;
using System.Linq;
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
        private const string InputIdsName = "input_ids";
        private const string AttentionMaskName = "attention_mask";
        private const string PositionIdsName = "position_ids";
        private const string LogitsOutputName = "logits";
        private const int KvHeads = 2;
        private const int KvHeadDimension = 64;

        [Header("Assets")]
        public string displayName = "Qwen2.5-0.5B-Instruct";
        public ModelAsset modelAsset;
        public TextAsset tokenizerJson;
        
        [Header("Generation Settings")]
        public int maxTokens = 256;
        [Range(0.1f, 2.0f)]
        public float temperature = 0.7f;
        public int topK = 40;

        [Header("Debug")]
        [SerializeField] private bool enableDebugLogging = true;
        [SerializeField] private int maxLoggedCharacters = 1200;

        private Model runtimeModel;
        private Worker worker;
        private ITokenizer tokenizer;
        private string validationState = "No model configured.";
    private readonly List<string> pastInputNames = new();
    private readonly List<string> presentOutputNames = new();

        public string DisplayName => displayName;

        public string StatusSummary => validationState;

        public bool IsConfigured => modelAsset != null && tokenizerJson != null;

        private void OnDestroy()
        {
            worker?.Dispose();
        }

        public async Task<string> GenerateReplyAsync(ChatRequest request, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();

            if (!IsConfigured)
            {
                validationState = "Qwen model is not configured. Assign a ModelAsset and tokenizer.json on the Systems object.";
                return "Модель не подключена. Назначь ModelAsset и tokenizer.json.";
            }

            try
            {
                EnsureInitialized();
            }
            catch (Exception exception)
            {
                validationState = $"Setup failed: {exception.Message}";
                return $"Ошибка подготовки: {exception.Message}";
            }

            var prompt = BuildPrompt(request);
            var promptTokens = tokenizer.Encode(prompt).GetIds().ToArray();
            if (promptTokens.Length == 0)
            {
                validationState = "Tokenizer returned an empty prompt.";
                return "Не удалось токенизировать prompt для модели.";
            }

            LogDebug($"Prompt for {request.NpcName}:\n{LimitForLog(prompt)}");
            LogDebug($"Prompt token count: {promptTokens.Length}");

            validationState = $"Running {displayName} with {pastInputNames.Count / 2} KV-cache layers.";

            var cacheTensors = CreateInitialCacheTensors();
            Tensor<float> logitsTensor = null;
            var generatedTokens = new List<int>(maxTokens);

            try
            {
                // Prefill the prompt one token at a time because this ONNX export is decoder-with-past.
                for (var promptIndex = 0; promptIndex < promptTokens.Length; promptIndex++)
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    logitsTensor?.Dispose();
                    logitsTensor = RunSingleTokenStep(promptTokens[promptIndex], promptIndex, cacheTensors);
                    await Task.Yield();
                }

                if (logitsTensor == null)
                {
                    return "Модель не вернула logits после prefill стадии.";
                }

                var nextToken = SampleNextToken(logitsTensor);

                for (var generationIndex = 0; generationIndex < maxTokens && !IsEos(nextToken); generationIndex++)
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    generatedTokens.Add(nextToken);

                    var position = promptTokens.Length + generationIndex;
                    logitsTensor.Dispose();
                    logitsTensor = RunSingleTokenStep(nextToken, position, cacheTensors);

                    nextToken = SampleNextToken(logitsTensor);
                    await Task.Yield();
                }

                var generatedText = generatedTokens.Count == 0
                    ? string.Empty
                    : tokenizer.Decode(generatedTokens.ToArray());
                var cleanedText = CleanGeneratedText(generatedText);
                LogDebug($"Generated {generatedTokens.Count} tokens.");
                LogDebug($"Raw model output:\n{LimitForLog(generatedText)}");
                LogDebug($"Cleaned model output:\n{LimitForLog(cleanedText)}");

                validationState = $"Generated {generatedTokens.Count} tokens with {displayName}.";
                return string.IsNullOrWhiteSpace(cleanedText)
                    ? $"Модель сгенерировала {generatedTokens.Count} токенов, но ответ состоял только из служебных символов."
                    : cleanedText;
            }
            catch (Exception exception)
            {
                validationState = $"Inference failed: {exception.Message}";
                throw;
            }
            finally
            {
                logitsTensor?.Dispose();
                DisposeTensors(cacheTensors);
            }
        }

        private string BuildPrompt(ChatRequest request)
        {
            var prompt = $"<|im_start|>system\n{NpcConversationSupport.BuildSystemPrompt(request, true)}<|im_end|>\n";
            foreach (var message in request.History)
            {
                var role = message.Role == ChatRole.Player ? "user" : "assistant";
                prompt += $"<|im_start|>{role}\n{message.Text}<|im_end|>\n";
            }

            prompt += $"<|im_start|>user\n{request.PlayerMessage}<|im_end|>\n<|im_start|>assistant\n";
            return prompt;
        }

        private Tensor<float> RunSingleTokenStep(int tokenId, int position, List<Tensor> cacheTensors)
        {
            using var inputIds = new Tensor<int>(new TensorShape(1, 1), new[] { tokenId });
            using var attentionMask = new Tensor<int>(new TensorShape(1, position + 1), CreateFilledArray(position + 1, 1));
            using var positionIds = new Tensor<int>(new TensorShape(1, 1), new[] { position });

            worker.SetInput(InputIdsName, inputIds);
            worker.SetInput(AttentionMaskName, attentionMask);
            worker.SetInput(PositionIdsName, positionIds);

            for (var index = 0; index < pastInputNames.Count; index++)
            {
                worker.SetInput(pastInputNames[index], cacheTensors[index]);
            }

            worker.Schedule();

            Tensor logits = null;
            worker.CopyOutput(LogitsOutputName, ref logits);
            UpdateCacheFromOutputs(cacheTensors);
            return logits as Tensor<float>;
        }

        private void UpdateCacheFromOutputs(List<Tensor> cacheTensors)
        {
            for (var index = 0; index < presentOutputNames.Count; index++)
            {
                Tensor replacement = null;
                worker.CopyOutput(presentOutputNames[index], ref replacement);
                cacheTensors[index]?.Dispose();
                cacheTensors[index] = replacement;
            }
        }

        private List<Tensor> CreateInitialCacheTensors()
        {
            var tensors = new List<Tensor>(pastInputNames.Count);
            for (var index = 0; index < pastInputNames.Count; index++)
            {
                tensors.Add(new Tensor<float>(new TensorShape(1, KvHeads, 0, KvHeadDimension)));
            }

            return tensors;
        }

        private static void DisposeTensors(IEnumerable<Tensor> tensors)
        {
            foreach (var tensor in tensors)
            {
                tensor?.Dispose();
            }
        }

        private int SampleNextToken(Tensor<float> logits)
        {
            using var readableLogits = logits.ReadbackAndClone();

            var sequenceLength = readableLogits.shape[1];
            var vocabSize = readableLogits.shape[2];
            var tokenIndex = sequenceLength - 1;

            if (topK <= 1 || temperature <= 0.0001f)
            {
                return GetArgmaxToken(readableLogits, tokenIndex, vocabSize);
            }

            var candidateCount = Mathf.Clamp(topK, 1, vocabSize);
            var candidateIds = new int[candidateCount];
            var candidateScores = new float[candidateCount];
            for (var i = 0; i < candidateCount; i++)
            {
                candidateIds[i] = -1;
                candidateScores[i] = float.NegativeInfinity;
            }

            for (var vocabIndex = 0; vocabIndex < vocabSize; vocabIndex++)
            {
                var score = readableLogits[0, tokenIndex, vocabIndex];
                for (var slot = 0; slot < candidateCount; slot++)
                {
                    if (score <= candidateScores[slot])
                    {
                        continue;
                    }

                    for (var shift = candidateCount - 1; shift > slot; shift--)
                    {
                        candidateScores[shift] = candidateScores[shift - 1];
                        candidateIds[shift] = candidateIds[shift - 1];
                    }

                    candidateScores[slot] = score;
                    candidateIds[slot] = vocabIndex;
                    break;
                }
            }

            var maxScore = candidateScores[0];
            var weights = new float[candidateCount];
            var weightSum = 0f;
            for (var i = 0; i < candidateCount; i++)
            {
                if (candidateIds[i] < 0)
                {
                    continue;
                }

                var weight = Mathf.Exp((candidateScores[i] - maxScore) / temperature);
                weights[i] = weight;
                weightSum += weight;
            }

            if (weightSum <= 0f)
            {
                return candidateIds[0];
            }

            var sample = UnityEngine.Random.value * weightSum;
            var cumulative = 0f;
            for (var i = 0; i < candidateCount; i++)
            {
                cumulative += weights[i];
                if (sample <= cumulative)
                {
                    return candidateIds[i];
                }
            }

            return candidateIds[0];
        }

        private static int GetArgmaxToken(Tensor<float> logits, int tokenIndex, int vocabSize)
        {
            var maxValue = float.NegativeInfinity;
            var maxIndex = 0;

            for (var vocabIndex = 0; vocabIndex < vocabSize; vocabIndex++)
            {
                var value = logits[0, tokenIndex, vocabIndex];
                if (value <= maxValue)
                {
                    continue;
                }

                maxValue = value;
                maxIndex = vocabIndex;
            }

            return maxIndex;
        }

        private bool IsEos(int token)
        {
            return token == 151643 || token == 151645;
        }

        private static string CleanGeneratedText(string text)
        {
            if (string.IsNullOrEmpty(text))
            {
                return string.Empty;
            }

            var cleaned = text
                .Replace("<|im_start|>", string.Empty)
                .Replace("<|im_end|>", string.Empty)
                .Replace("<|endoftext|>", string.Empty)
                .Replace("<|vision_start|>", string.Empty)
                .Replace("<|vision_end|>", string.Empty)
                .Replace("<|object_ref_start|>", string.Empty)
                .Replace("<|object_ref_end|>", string.Empty);

            var buffer = new char[cleaned.Length];
            var count = 0;
            foreach (var character in cleaned)
            {
                if (character == '\n' || character == '\r' || character == '\t' || !char.IsControl(character))
                {
                    buffer[count++] = character;
                }
            }

            cleaned = new string(buffer, 0, count);

            while (cleaned.Contains("\n\n\n", StringComparison.Ordinal))
            {
                cleaned = cleaned.Replace("\n\n\n", "\n\n", StringComparison.Ordinal);
            }

            return cleaned.Trim();
        }

        private void LogDebug(string message)
        {
            if (!enableDebugLogging)
            {
                return;
            }

            Debug.Log($"[SentisLocalLanguageModel] {message}");
        }

        private string LimitForLog(string text)
        {
            if (string.IsNullOrEmpty(text) || text.Length <= maxLoggedCharacters)
            {
                return text;
            }

            return text.Substring(0, maxLoggedCharacters) + "\n...<truncated>";
        }

        private void EnsureInitialized()
        {
            if (runtimeModel == null)
            {
                runtimeModel = ModelLoader.Load(modelAsset);
                var backendType = SystemInfo.supportsComputeShaders ? BackendType.GPUCompute : BackendType.CPU;
                worker = new Worker(runtimeModel, backendType);
                DiscoverModelIo(runtimeModel);
            }

            if (tokenizer == null)
            {
                var parser = HuggingFaceParser.GetDefault();
                tokenizer = parser.Parse(tokenizerJson.text);
            }

            validationState = $"Loaded {displayName}: {runtimeModel.inputs.Count} inputs, {runtimeModel.outputs.Count} outputs.";
        }

        private void DiscoverModelIo(Model model)
        {
            pastInputNames.Clear();
            presentOutputNames.Clear();

            foreach (var input in model.inputs)
            {
                if (input.name.StartsWith("past_key_values.", StringComparison.Ordinal))
                {
                    pastInputNames.Add(input.name);
                }
            }

            foreach (var output in model.outputs)
            {
                if (output.name.StartsWith("present.", StringComparison.Ordinal))
                {
                    presentOutputNames.Add(output.name);
                }
            }

            pastInputNames.Sort(CompareCacheTensorNames);
            presentOutputNames.Sort(CompareCacheTensorNames);

            if (pastInputNames.Count == 0 || presentOutputNames.Count == 0)
            {
                throw new InvalidOperationException("The imported ONNX model is missing past/present KV-cache tensors.");
            }

            if (pastInputNames.Count != presentOutputNames.Count)
            {
                throw new InvalidOperationException($"KV-cache tensor count mismatch: inputs={pastInputNames.Count}, outputs={presentOutputNames.Count}.");
            }
        }

        private static int CompareCacheTensorNames(string left, string right)
        {
            var leftLayer = ExtractLayerIndex(left);
            var rightLayer = ExtractLayerIndex(right);
            var layerComparison = leftLayer.CompareTo(rightLayer);
            if (layerComparison != 0)
            {
                return layerComparison;
            }

            var leftIsKey = left.EndsWith(".key", StringComparison.Ordinal);
            var rightIsKey = right.EndsWith(".key", StringComparison.Ordinal);
            if (leftIsKey == rightIsKey)
            {
                return string.CompareOrdinal(left, right);
            }

            return leftIsKey ? -1 : 1;
        }

        private static int ExtractLayerIndex(string tensorName)
        {
            var firstDot = tensorName.IndexOf('.') + 1;
            var secondDot = tensorName.IndexOf('.', firstDot);
            if (firstDot <= 0 || secondDot <= firstDot)
            {
                return -1;
            }

            return int.TryParse(tensorName.Substring(firstDot, secondDot - firstDot), out var index) ? index : -1;
        }

        private static int[] CreateFilledArray(int length, int value)
        {
            var array = new int[length];
            for (var i = 0; i < length; i++)
            {
                array[i] = value;
            }

            return array;
        }
    }
}
