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
            return NpcConversationSupport.BuildStubReply(request);
        }
    }
}
