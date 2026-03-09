using System.Collections.Generic;
using MastersGame.UI;
using UnityEngine;
using UnityEngine.InputSystem;

namespace MastersGame.Gameplay
{
    [RequireComponent(typeof(PlayerInput))]
    public class PlayerInteractionController : MonoBehaviour
    {
        [SerializeField] private NpcChatGameManager gameManager;
        [SerializeField] private InteractionPromptView promptView;

        private readonly List<NpcChatTarget> nearbyTargets = new();

        private PlayerInput playerInput;
        private InputAction interactAction;
        private NpcChatTarget currentTarget;

        public NpcChatTarget CurrentTarget => currentTarget;

        public void Configure(NpcChatGameManager manager, InteractionPromptView prompt)
        {
            gameManager = manager;
            promptView = prompt;
        }

        private void Awake()
        {
            playerInput = GetComponent<PlayerInput>();
            interactAction = playerInput.actions["Interact"];
        }

        private void OnEnable()
        {
            if (interactAction != null)
            {
                interactAction.performed += OnInteract;
            }
        }

        private void OnDisable()
        {
            if (interactAction != null)
            {
                interactAction.performed -= OnInteract;
            }
        }

        private void Update()
        {
            currentTarget = FindClosestTarget();
            RefreshPrompt();
        }

        public void RegisterNearbyTarget(NpcChatTarget target)
        {
            if (target == null || nearbyTargets.Contains(target))
            {
                return;
            }

            nearbyTargets.Add(target);
        }

        public void UnregisterNearbyTarget(NpcChatTarget target)
        {
            nearbyTargets.Remove(target);
            if (currentTarget == target)
            {
                currentTarget = null;
            }
        }

        public void OnInteract(InputValue value)
        {
            if (value == null || !value.isPressed)
            {
                return;
            }

            TryOpenConversation();
        }

        private void OnInteract(InputAction.CallbackContext _)
        {
            TryOpenConversation();
        }

        private void TryOpenConversation()
        {
            if (gameManager == null || gameManager.ChatOpen || currentTarget == null)
            {
                return;
            }

            gameManager.OpenConversation(currentTarget);
        }

        private NpcChatTarget FindClosestTarget()
        {
            nearbyTargets.RemoveAll(target => target == null);
            if (nearbyTargets.Count == 0)
            {
                return null;
            }

            var bestDistance = float.MaxValue;
            NpcChatTarget bestTarget = null;
            var currentPosition = transform.position;

            foreach (var target in nearbyTargets)
            {
                var distance = (target.transform.position - currentPosition).sqrMagnitude;
                if (distance < bestDistance)
                {
                    bestDistance = distance;
                    bestTarget = target;
                }
            }

            return bestTarget;
        }

        private void RefreshPrompt()
        {
            if (promptView == null)
            {
                return;
            }

            if (gameManager != null && gameManager.ChatOpen)
            {
                promptView.Hide();
                return;
            }

            if (currentTarget == null)
            {
                promptView.Hide();
                return;
            }

            promptView.Show(currentTarget.InteractionHint);
        }
    }
}