using System.Collections.Generic;
using MastersGame.AI;
using UnityEngine;

namespace MastersGame.Gameplay
{
    [RequireComponent(typeof(Collider))]
    public class NpcChatTarget : MonoBehaviour
    {
        [SerializeField] private string npcName = "Archivist";
        [SerializeField] private string persona = "Спокойный хранитель места, который кратко и доброжелательно отвечает на вопросы прохожим и не выходит из своей роли.";
        [SerializeField] private string greeting = "Привет. Если есть дело — говори.";
        [SerializeField] private string interactionHint = "Press Interact to talk";

        public string NpcName => npcName;

        public string Persona => persona;

        public string Greeting => greeting;

        public string InteractionHint => interactionHint;

        public void Configure(string configuredName, string configuredPersona, string configuredGreeting, string configuredHint)
        {
            npcName = configuredName;
            persona = configuredPersona;
            greeting = configuredGreeting;
            interactionHint = configuredHint;
        }

        public ChatRequest BuildRequest(IReadOnlyList<ChatMessage> history, string playerMessage, WorldContextSnapshot worldContext)
        {
            return new ChatRequest(npcName, persona, greeting, history, playerMessage, worldContext);
        }

        private void Reset()
        {
            var trigger = GetComponent<Collider>();
            trigger.isTrigger = true;
        }

        private void OnTriggerEnter(Collider other)
        {
            var interaction = other.GetComponent<PlayerInteractionController>() ?? other.GetComponentInParent<PlayerInteractionController>();
            if (interaction != null)
            {
                interaction.RegisterNearbyTarget(this);
            }
        }

        private void OnTriggerExit(Collider other)
        {
            var interaction = other.GetComponent<PlayerInteractionController>() ?? other.GetComponentInParent<PlayerInteractionController>();
            if (interaction != null)
            {
                interaction.UnregisterNearbyTarget(this);
            }
        }
    }
}
