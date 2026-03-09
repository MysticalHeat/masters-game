using System;
using System.Text;
using MastersGame.AI;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem;
using UnityEngine.UI;

namespace MastersGame.UI
{
    public class ChatWindowController : MonoBehaviour
    {
        private static readonly string[] PreferredFontNames =
        {
            "Noto Sans",
            "Noto Sans CJK JP",
            "DejaVu Sans",
            "Liberation Sans",
            "Arial"
        };

        private static Font cachedUnicodeFont;

        [SerializeField] private Text titleLabel;
        [SerializeField] private Text statusLabel;
        [SerializeField] private ScrollRect scrollRect;
        [SerializeField] private RectTransform messageContainer;
        [SerializeField] private Text messageTemplate;
        [SerializeField] private InputField inputField;
        [SerializeField] private Button sendButton;
        [SerializeField] private Button closeButton;
        [SerializeField] private bool useUnicodeFallbackFont = true;
        [SerializeField] private bool enableDebugLogging = true;
        [SerializeField] private bool useTranscriptMode = true;

        private readonly StringBuilder transcriptBuilder = new();
        private Text transcriptText;

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
            ApplyUnicodeFallbackFont();
            EnsureTranscriptOverlay();
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

            if (useTranscriptMode)
            {
                AppendTranscriptMessage(role, author, body);
                return;
            }

            var entry = Instantiate(messageTemplate, messageContainer);
            entry.gameObject.SetActive(true);
            if (useUnicodeFallbackFont)
            {
                var fallbackFont = GetUnicodeFallbackFont();
                if (fallbackFont != null)
                {
                    entry.font = fallbackFont;
                }
            }

            entry.supportRichText = false;
            entry.horizontalOverflow = HorizontalWrapMode.Wrap;
            entry.verticalOverflow = VerticalWrapMode.Overflow;
            entry.text = $"{author}:\n{body}";

            var layoutElement = entry.GetComponent<LayoutElement>();
            if (layoutElement == null)
            {
                layoutElement = entry.gameObject.AddComponent<LayoutElement>();
            }

            var preferredHeight = Mathf.Max(28f, entry.preferredHeight + 12f);
            layoutElement.preferredHeight = preferredHeight;
            layoutElement.flexibleHeight = 0f;

            var rect = entry.rectTransform;
            rect.anchorMin = new Vector2(0f, 1f);
            rect.anchorMax = new Vector2(1f, 1f);
            rect.pivot = new Vector2(0.5f, 1f);
            rect.sizeDelta = new Vector2(0f, preferredHeight);

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

            LayoutRebuilder.ForceRebuildLayoutImmediate(rect);
            LayoutRebuilder.ForceRebuildLayoutImmediate(messageContainer);
            Canvas.ForceUpdateCanvases();
            scrollRect.verticalNormalizedPosition = 0f;

            if (enableDebugLogging)
            {
                Debug.Log($"[ChatWindowController] Appended {role} message. Author='{author}' BodyLength={body?.Length ?? 0} PreferredHeight={preferredHeight} Font='{entry.font?.name}'");
            }
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

            transcriptBuilder.Clear();
            if (transcriptText != null)
            {
                transcriptText.text = string.Empty;
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

        private void AppendTranscriptMessage(ChatRole role, string author, string body)
        {
            EnsureTranscriptOverlay();

            if (useUnicodeFallbackFont)
            {
                var fallbackFont = GetUnicodeFallbackFont();
                if (fallbackFont != null)
                {
                    messageTemplate.font = fallbackFont;
                    if (transcriptText != null)
                    {
                        transcriptText.font = fallbackFont;
                    }
                }
            }

            if (transcriptBuilder.Length > 0)
            {
                transcriptBuilder.Append("\n\n");
            }

            transcriptBuilder.Append(author);
            transcriptBuilder.Append(':');
            transcriptBuilder.Append('\n');
            transcriptBuilder.Append(body);

            transcriptText.gameObject.SetActive(true);
            transcriptText.supportRichText = false;
            transcriptText.horizontalOverflow = HorizontalWrapMode.Wrap;
            transcriptText.verticalOverflow = VerticalWrapMode.Overflow;
            transcriptText.alignment = TextAnchor.UpperLeft;
            transcriptText.color = Color.white;
            transcriptText.text = transcriptBuilder.ToString();

            var rect = transcriptText.rectTransform;
            rect.anchorMin = new Vector2(0f, 1f);
            rect.anchorMax = new Vector2(1f, 1f);
            rect.pivot = new Vector2(0.5f, 1f);
            rect.sizeDelta = new Vector2(0f, Mathf.Max(120f, transcriptText.preferredHeight + 24f));

            LayoutRebuilder.ForceRebuildLayoutImmediate(rect);
            if (scrollRect != null && scrollRect.content != null)
            {
                LayoutRebuilder.ForceRebuildLayoutImmediate(scrollRect.content);
            }
            Canvas.ForceUpdateCanvases();
            if (scrollRect != null)
            {
                scrollRect.verticalNormalizedPosition = 0f;
            }

            if (enableDebugLogging)
            {
                Debug.Log($"[ChatWindowController] Transcript updated for {role}. TotalLength={transcriptBuilder.Length} Font='{transcriptText.font?.name}'");
            }
        }

        private void EnsureTranscriptOverlay()
        {
            if (!useTranscriptMode || transcriptText != null)
            {
                return;
            }

            if (scrollRect != null)
            {
                scrollRect.gameObject.SetActive(false);
            }

            if (messageTemplate != null)
            {
                messageTemplate.gameObject.SetActive(false);
            }

            var transcriptObject = new GameObject("TranscriptOverlay", typeof(RectTransform));
            var transcriptRect = transcriptObject.GetComponent<RectTransform>();
            transcriptRect.SetParent(transform, false);
            transcriptRect.anchorMin = new Vector2(0.03f, 0.22f);
            transcriptRect.anchorMax = new Vector2(0.97f, 0.78f);
            transcriptRect.offsetMin = Vector2.zero;
            transcriptRect.offsetMax = Vector2.zero;

            var background = transcriptObject.AddComponent<Image>();
            background.color = new Color(0.11f, 0.14f, 0.18f, 0.85f);

            var textObject = new GameObject("TranscriptText", typeof(RectTransform));
            var textRect = textObject.GetComponent<RectTransform>();
            textRect.SetParent(transcriptRect, false);
            textRect.anchorMin = new Vector2(0f, 0f);
            textRect.anchorMax = new Vector2(1f, 1f);
            textRect.offsetMin = new Vector2(16f, 16f);
            textRect.offsetMax = new Vector2(-16f, -16f);

            transcriptText = textObject.AddComponent<Text>();
            transcriptText.supportRichText = false;
            transcriptText.font = GetUnicodeFallbackFont() ?? (messageTemplate != null ? messageTemplate.font : null);
            transcriptText.fontSize = messageTemplate != null ? messageTemplate.fontSize : 18;
            transcriptText.fontStyle = FontStyle.Normal;
            transcriptText.alignment = TextAnchor.UpperLeft;
            transcriptText.horizontalOverflow = HorizontalWrapMode.Wrap;
            transcriptText.verticalOverflow = VerticalWrapMode.Overflow;
            transcriptText.color = Color.white;
            transcriptText.text = string.Empty;
        }

        private void ApplyUnicodeFallbackFont()
        {
            if (!useUnicodeFallbackFont)
            {
                return;
            }

            var fallbackFont = GetUnicodeFallbackFont();
            if (fallbackFont == null)
            {
                return;
            }

            foreach (var text in GetComponentsInChildren<Text>(true))
            {
                text.font = fallbackFont;
            }
        }

        private static Font GetUnicodeFallbackFont()
        {
            if (cachedUnicodeFont != null)
            {
                return cachedUnicodeFont;
            }

            cachedUnicodeFont = Font.CreateDynamicFontFromOSFont(PreferredFontNames, 16);
            if (cachedUnicodeFont == null)
            {
                Debug.LogWarning("[ChatWindowController] Failed to create Unicode fallback font from OS fonts.");
            }

            return cachedUnicodeFont;
        }
    }
}