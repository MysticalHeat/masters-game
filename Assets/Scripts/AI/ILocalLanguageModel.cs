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
}