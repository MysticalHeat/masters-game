using System.Collections.Generic;

namespace MastersGame.AI
{
    public enum ChatRole
    {
        System,
        Player,
        Npc
    }

    [System.Serializable]
    public class ChatMessage
    {
        public ChatMessage(ChatRole role, string text)
        {
            Role = role;
            Text = text;
        }

        public ChatRole Role { get; }

        public string Text { get; }
    }

    public class ChatRequest
    {
        public ChatRequest(string npcName, string persona, string greeting, IReadOnlyList<ChatMessage> history, string playerMessage)
        {
            NpcName = npcName;
            Persona = persona;
            Greeting = greeting;
            History = history;
            PlayerMessage = playerMessage;
        }

        public string NpcName { get; }

        public string Persona { get; }

        public string Greeting { get; }

        public IReadOnlyList<ChatMessage> History { get; }

        public string PlayerMessage { get; }
    }
}