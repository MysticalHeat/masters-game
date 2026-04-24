using System;
using MastersGame.AI;
using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem;
using UnityEngine.UI;

namespace MastersGame.UI
{
    public class ChatWindowController : MonoBehaviour
    {
        [Header("References")]
        [SerializeField] private TextMeshProUGUI titleLabel;
        [SerializeField] private TextMeshProUGUI statusLabel;
        [SerializeField] private ScrollRect scrollRect;
        [SerializeField] private RectTransform messageContainer;
        [SerializeField] private TMP_InputField inputField;
        [SerializeField] private Button sendButton;
        [SerializeField] private Button closeButton;

        [Header("Bubble Style")]
        [SerializeField] private Color playerBubbleColor = new Color(0.17f, 0.36f, 0.56f, 0.95f);
        [SerializeField] private Color npcBubbleColor = new Color(0.19f, 0.20f, 0.26f, 0.95f);
        [SerializeField] private Color systemBubbleColor = new Color(0.14f, 0.24f, 0.18f, 0.95f);
        [SerializeField] private Color playerTextColor = Color.white;
        [SerializeField] private Color npcTextColor = new Color(0.94f, 0.92f, 0.87f);
        [SerializeField] private Color systemTextColor = new Color(0.72f, 0.94f, 0.78f);
        [SerializeField] private Color authorLabelColor = new Color(1f, 1f, 1f, 0.50f);
        [SerializeField] private float maxBubbleWidthFraction = 0.78f;
        [SerializeField] private int bodyFontSize = 16;
        [SerializeField] private int authorFontSize = 11;

        [Header("Debug")]
        [SerializeField] private bool enableDebugLogging = true;

        public event Action<string> SendRequested;

        public event Action CloseRequested;

        public void Configure(
            TextMeshProUGUI title,
            TextMeshProUGUI status,
            ScrollRect messagesScrollRect,
            RectTransform messagesContainer,
            TMP_InputField field,
            Button send,
            Button close)
        {
            titleLabel = title;
            statusLabel = status;
            scrollRect = messagesScrollRect;
            messageContainer = messagesContainer;
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
            if (messageContainer == null)
            {
                return;
            }

            author ??= string.Empty;
            body ??= string.Empty;

            var isPlayer = role == ChatRole.Player;
            const int bubblePaddingLeft = 14;
            const int bubblePaddingRight = 14;
            const int bubblePaddingTop = 8;
            const int bubblePaddingBottom = 10;
            const float bubbleSpacing = 2f;

            Color bubbleColor;
            Color textColor;

            switch (role)
            {
                case ChatRole.Player:
                    bubbleColor = playerBubbleColor;
                    textColor = playerTextColor;
                    break;
                case ChatRole.Npc:
                    bubbleColor = npcBubbleColor;
                    textColor = npcTextColor;
                    break;
                default:
                    bubbleColor = systemBubbleColor;
                    textColor = systemTextColor;
                    break;
            }

            var row = CreateChild("MessageRow", messageContainer);
            var rowLayout = row.gameObject.AddComponent<HorizontalLayoutGroup>();
            rowLayout.childControlWidth = true;
            rowLayout.childControlHeight = true;
            rowLayout.childForceExpandWidth = false;
            rowLayout.childForceExpandHeight = false;
            rowLayout.spacing = 0f;

            var rowElement = row.gameObject.AddComponent<LayoutElement>();
            rowElement.flexibleWidth = 1f;

            if (isPlayer)
            {
                AddFlexSpacer(row);
            }

            var bubble = CreateChild("Bubble", row);
            var bubbleImage = bubble.gameObject.AddComponent<Image>();
            bubbleImage.color = bubbleColor;

            var bubbleLayout = bubble.gameObject.AddComponent<VerticalLayoutGroup>();
            bubbleLayout.padding = new RectOffset(bubblePaddingLeft, bubblePaddingRight, bubblePaddingTop, bubblePaddingBottom);
            bubbleLayout.spacing = bubbleSpacing;
            bubbleLayout.childControlWidth = true;
            bubbleLayout.childControlHeight = true;
            bubbleLayout.childForceExpandWidth = true;
            bubbleLayout.childForceExpandHeight = false;

            var maxBubbleWidth = ComputeMaxBubbleWidth();
            var bubbleElement = bubble.gameObject.AddComponent<LayoutElement>();
            bubbleElement.preferredWidth = maxBubbleWidth;
            bubbleElement.flexibleWidth = 0f;

            var authorObj = CreateChild("Author", bubble);
            var authorTmp = authorObj.gameObject.AddComponent<TextMeshProUGUI>();
            authorTmp.text = author;
            authorTmp.fontSize = authorFontSize;
            authorTmp.fontStyle = FontStyles.Bold;
            authorTmp.color = authorLabelColor;
            authorTmp.alignment = isPlayer ? TextAlignmentOptions.TopRight : TextAlignmentOptions.TopLeft;
            authorTmp.textWrappingMode = TextWrappingModes.NoWrap;
            authorTmp.overflowMode = TextOverflowModes.Ellipsis;

            var authorElement = authorObj.gameObject.AddComponent<LayoutElement>();
            authorElement.flexibleWidth = 1f;

            var bodyObj = CreateChild("Body", bubble);
            var bodyTmp = bodyObj.gameObject.AddComponent<TextMeshProUGUI>();
            bodyTmp.text = body;
            bodyTmp.fontSize = bodyFontSize;
            bodyTmp.color = textColor;
            bodyTmp.alignment = isPlayer ? TextAlignmentOptions.TopRight : TextAlignmentOptions.TopLeft;
            bodyTmp.textWrappingMode = TextWrappingModes.Normal;
            bodyTmp.overflowMode = TextOverflowModes.Overflow;

            var bodyElement = bodyObj.gameObject.AddComponent<LayoutElement>();
            bodyElement.flexibleWidth = 1f;

            if (!isPlayer)
            {
                AddFlexSpacer(row);
            }

            authorTmp.ForceMeshUpdate();
            bodyTmp.ForceMeshUpdate();

            var textWidth = Mathf.Max(1f, maxBubbleWidth - bubblePaddingLeft - bubblePaddingRight);
            var authorHeight = Mathf.Max(authorTmp.preferredHeight, authorTmp.GetPreferredValues(author, textWidth, 0f).y);
            var bodyHeight = Mathf.Max(bodyTmp.preferredHeight, bodyTmp.GetPreferredValues(body, textWidth, 0f).y);
            var bubbleHeight = bubblePaddingTop + authorHeight + bubbleSpacing + bodyHeight + bubblePaddingBottom;

            authorElement.preferredHeight = authorHeight;
            bodyElement.preferredHeight = bodyHeight;
            bubbleElement.preferredHeight = bubbleHeight;
            rowElement.preferredHeight = bubbleHeight;

            LayoutRebuilder.ForceRebuildLayoutImmediate(row);
            LayoutRebuilder.ForceRebuildLayoutImmediate(bubble);
            LayoutRebuilder.ForceRebuildLayoutImmediate(messageContainer);
            Canvas.ForceUpdateCanvases();

            if (scrollRect != null)
            {
                scrollRect.verticalNormalizedPosition = 0f;
            }

            if (enableDebugLogging)
            {
                Debug.Log($"[ChatWindowController] Appended {role} bubble. Author='{author}' BodyLength={body?.Length ?? 0}");
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
            if (messageContainer == null)
            {
                return;
            }

            for (var index = messageContainer.childCount - 1; index >= 0; index--)
            {
                Destroy(messageContainer.GetChild(index).gameObject);
            }
        }

        private float ComputeMaxBubbleWidth()
        {
            if (messageContainer != null && messageContainer.rect.width > 10f)
            {
                return messageContainer.rect.width * maxBubbleWidthFraction;
            }

            return 420f;
        }

        private static RectTransform CreateChild(string objectName, Transform parent)
        {
            var child = new GameObject(objectName, typeof(RectTransform));
            var rect = child.GetComponent<RectTransform>();
            rect.SetParent(parent, false);
            return rect;
        }

        private static void AddFlexSpacer(Transform parent)
        {
            var spacer = CreateChild("Spacer", parent);
            var element = spacer.gameObject.AddComponent<LayoutElement>();
            element.flexibleWidth = 1f;
        }
    }
}
