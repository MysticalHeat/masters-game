using System;
using System.Threading;
using System.Threading.Tasks;

namespace MastersGame.AI
{
    public interface ILocalLanguageModel
    {
        string DisplayName { get; }

        string StatusSummary { get; }

        bool IsConfigured { get; }

        Task<string> GenerateReplyAsync(ChatRequest request, CancellationToken cancellationToken);
    }

    public interface IStreamingLocalLanguageModel : ILocalLanguageModel
    {
        Task<string> GenerateReplyStreamingAsync(ChatRequest request, Action<string> onPartialText, CancellationToken cancellationToken);
    }
}
