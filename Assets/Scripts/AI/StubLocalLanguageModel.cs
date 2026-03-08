using System;
using System.Threading;
using System.Threading.Tasks;
using UnityEngine;

namespace MastersGame.AI
{
    public class StubLocalLanguageModel : MonoBehaviour, ILocalLanguageModel
    {
        [SerializeField] private string displayName = "Stub NPC Brain";
        [SerializeField] private float simulatedLatencySeconds = 0.65f;

        public string DisplayName => displayName;

        public string StatusSummary => "Stub replies active. Assign a Sentis ModelAsset and tokenizer.json to switch to local inference.";

        public bool IsConfigured => true;

        public async Task<string> GenerateReplyAsync(ChatRequest request, CancellationToken cancellationToken)
        {
            var delay = Mathf.Max(0.05f, simulatedLatencySeconds);
            await Task.Delay(TimeSpan.FromSeconds(delay), cancellationToken);

            cancellationToken.ThrowIfCancellationRequested();

            var prompt = request.PlayerMessage?.Trim() ?? string.Empty;
            if (string.IsNullOrEmpty(prompt))
            {
                return $"{request.NpcName}: Сформулируй вопрос, и я попробую ответить.";
            }

            var lower = prompt.ToLowerInvariant();
            if (lower.Contains("привет") || lower.Contains("hello") || lower.Contains("hi"))
            {
                return $"Привет. Я {request.NpcName}. Сейчас ты говоришь со stub-версией NPC, но весь игровой цикл чата уже работает.";
            }

            if (lower.Contains("sentis") || lower.Contains("llm") || lower.Contains("модель"))
            {
                return "Локальная модель ещё не подключена. Пока ответы идут из stub-слоя, а Sentis-обвязка уже подготовлена под будущую ONNX-модель.";
            }

            if (lower.Contains("где") || lower.Contains("location") || lower.Contains("место"))
            {
                return "Это MVP-локация: игрок может свободно двигаться, подойти ко мне и открыть чат по Interact.";
            }

            if (lower.Contains("что") || lower.Contains("зачем") || lower.Contains("помоги"))
            {
                return $"Моя персона для MVP: {request.Persona}. Когда подключим реальную модель, этот же prompt-контур начнёт генерировать ответы локально.";
            }

            return $"Я услышал: \"{prompt}\". Сейчас это шаблонный ответ, но UI, interaction loop и orchestration уже готовы для реальной Sentis-модели.";
        }
    }
}