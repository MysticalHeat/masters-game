using System;
using MastersGame.AI;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem;
using UnityEngine.UI;

namespace MastersGame.UI
{
    public class ChatWindowController : MonoBehaviour
    {
        [SerializeField] private Text titleLabel;
        [SerializeField] private Text statusLabel;
        [SerializeField] private ScrollRect scrollRect;
        [SerializeField] private RectTransform messageContainer;
        [SerializeField] private Text messageTemplate;
        [SerializeField] private InputField inputField;
        [SerializeField] private Button sendButton;
        [SerializeField] private Button closeButton;

        public event Action<string> SendRequested;

        public event Action CloseRequested;

        public void Configure(Text title, Text status, ScrollRect messagesScrollRect, RectTransform messagesContainer, Text template, InputField field, Button send, Button close)
        {
            titleLabel = title;
            statusLabel = status;
            scrollRect = messagesScrollRect;
            messageContainer = messagesContainer;
            messageTemplate = template;
            inputField = field;
            sendButton = send;
            closeButton = close;
        }

        private void Awake()
        {
            sendButton.onClick.AddListener(SubmitDraft);
            closeButton.onClick.AddListener(() => CloseRequested?.Invoke());
        }

        private void Update()
        {
            if (!isActiveAndEnabled || inputField == null || !inputField.isFocused || Keyboard.current == null)
            {
                return;
            }

            if (Keyboard.current.enterKey.wasPressedThisFrame || Keyboard.current.numpadEnterKey.wasPressedThisFrame)
            {
                SubmitDraft();
            }
        }

        public void Show(string title, string status)
        {
            gameObject.SetActive(true);
            ClearMessages();
            titleLabel.text = title;
            statusLabel.text = status;
            inputField.text = string.Empty;
            SetBusy(false, status);
        }

        public void Hide()
        {
            gameObject.SetActive(false);
        }

        public void SetBusy(bool busy, string status)
        {
            if (statusLabel != null)
            {
                statusLabel.text = status;
            }

            if (sendButton != null)
            {
                sendButton.interactable = !busy;
            }

            if (inputField != null)
            {
                inputField.interactable = !busy;
            }
        }

        public void AppendMessage(ChatRole role, string author, string body)
        {
            if (messageTemplate == null || messageContainer == null)
            {
                return;
            }

            var entry = Instantiate(messageTemplate, messageContainer);
            entry.gameObject.SetActive(true);
            entry.text = $"{author}:\n{body}";

            switch (role)
            {
                case ChatRole.Player:
                    entry.alignment = TextAnchor.UpperRight;
                    entry.color = new Color(0.87f, 0.95f, 1f);
                    break;
                case ChatRole.Npc:
                    entry.alignment = TextAnchor.UpperLeft;
                    entry.color = new Color(1f, 0.93f, 0.8f);
                    break;
                default:
                    entry.alignment = TextAnchor.UpperLeft;
                    entry.color = new Color(0.8f, 1f, 0.82f);
                    break;
            }

            Canvas.ForceUpdateCanvases();
            scrollRect.verticalNormalizedPosition = 0f;
        }

        public void FocusInput()
        {
            if (inputField == null)
            {
                return;
            }

            EventSystem.current?.SetSelectedGameObject(inputField.gameObject);
            inputField.ActivateInputField();
        }

        private void SubmitDraft()
        {
            if (inputField == null)
            {
                return;
            }

            var draft = inputField.text.Trim();
            if (string.IsNullOrEmpty(draft))
            {
                return;
            }

            inputField.text = string.Empty;
            SendRequested?.Invoke(draft);
        }

        private void ClearMessages()
        {
            if (messageContainer == null || messageTemplate == null)
            {
                return;
            }

            for (var index = messageContainer.childCount - 1; index >= 0; index--)
            {
                var child = messageContainer.GetChild(index);
                if (child == messageTemplate.transform)
                {
                    continue;
                }

                Destroy(child.gameObject);
            }

            messageTemplate.gameObject.SetActive(false);
        }
    }
}