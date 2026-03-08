using System.Collections.Generic;
using System.Linq;
using MastersGame.AI;
using MastersGame.Gameplay;
using MastersGame.UI;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.UI;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

namespace MastersGame.Editor
{
    public static class NpcChatMvpSceneBuilder
    {
        private const string ScenePath = "Assets/Scenes/NpcChatMvp.unity";

        private sealed class PlayerBundle
        {
            public PlayerController3D Controller;
            public PlayerInteractionController Interaction;
        }

        private sealed class ChatBundle
        {
            public ChatWindowController Window;
            public InteractionPromptView Prompt;
        }

        [MenuItem("Tools/LLM Chat/Create MVP Scene")]
        public static void CreateMvpScene()
        {
            var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);

            var actionAsset = AssetDatabase.LoadAssetAtPath<InputActionAsset>("Assets/InputSystem_Actions.inputactions");
            if (actionAsset == null)
            {
                EditorUtility.DisplayDialog("Missing Input Actions", "Assets/InputSystem_Actions.inputactions was not found.", "OK");
                return;
            }

            CreateEnvironment();
            CreateEventSystem();

            var systems = new GameObject("Systems");
            var gameManager = systems.AddComponent<NpcChatGameManager>();
            var sentisModel = systems.AddComponent<SentisLocalLanguageModel>();
            var stubModel = systems.AddComponent<StubLocalLanguageModel>();

            var chatBundle = CreateUi();
            var player = CreatePlayer(actionAsset);
            var npc = CreateNpc();

            gameManager.Configure(player.Controller, chatBundle.Window, sentisModel, stubModel);
            player.Interaction.Configure(gameManager, chatBundle.Prompt);

            Selection.activeGameObject = npc.gameObject;
            EditorSceneManager.SaveScene(scene, ScenePath);
            EnsureSceneInBuildSettings(ScenePath);
            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();

            EditorUtility.DisplayDialog("MVP Scene Created", $"Scene saved to {ScenePath}. Open it and press Play to test the player, NPC and chat flow.", "OK");
        }

        private static void CreateEnvironment()
        {
            var sun = new GameObject("Sun");
            sun.transform.rotation = Quaternion.Euler(50f, -35f, 0f);
            var light = sun.AddComponent<Light>();
            light.type = LightType.Directional;
            light.intensity = 1.1f;

            var ground = GameObject.CreatePrimitive(PrimitiveType.Plane);
            ground.name = "Ground";
            ground.transform.position = Vector3.zero;
            ground.transform.localScale = new Vector3(4f, 1f, 4f);
            SetRendererColor(ground, new Color(0.22f, 0.3f, 0.24f));

            CreateWall("NorthWall", new Vector3(0f, 1.5f, 12f), new Vector3(24f, 3f, 1f));
            CreateWall("SouthWall", new Vector3(0f, 1.5f, -12f), new Vector3(24f, 3f, 1f));
            CreateWall("EastWall", new Vector3(12f, 1.5f, 0f), new Vector3(1f, 3f, 24f));
            CreateWall("WestWall", new Vector3(-12f, 1.5f, 0f), new Vector3(1f, 3f, 24f));

            CreateProp("Crate_A", new Vector3(-4f, 0.5f, 3f), new Vector3(1.25f, 1f, 1.25f), new Color(0.35f, 0.27f, 0.18f));
            CreateProp("Crate_B", new Vector3(4f, 0.5f, -2f), new Vector3(1f, 1f, 1f), new Color(0.28f, 0.22f, 0.14f));
            CreateProp("Marker", new Vector3(0f, 0.1f, 4f), new Vector3(2f, 0.2f, 2f), new Color(0.15f, 0.35f, 0.42f));
        }

        private static void CreateEventSystem()
        {
            var eventSystemObject = new GameObject("EventSystem", typeof(EventSystem), typeof(InputSystemUIInputModule));
            var inputModule = eventSystemObject.GetComponent<InputSystemUIInputModule>();
            inputModule.AssignDefaultActions();
        }

        private static PlayerBundle CreatePlayer(InputActionAsset actionAsset)
        {
            var root = new GameObject("Player");
            root.transform.position = new Vector3(0f, 0f, -6f);

            var controller = root.AddComponent<CharacterController>();
            controller.height = 1.8f;
            controller.radius = 0.32f;
            controller.center = new Vector3(0f, 0.9f, 0f);
            controller.minMoveDistance = 0f;

            var playerInput = root.AddComponent<PlayerInput>();
            playerInput.actions = actionAsset;
            playerInput.defaultActionMap = "Player";
            playerInput.neverAutoSwitchControlSchemes = true;

            var movement = root.AddComponent<PlayerController3D>();
            var interaction = root.AddComponent<PlayerInteractionController>();

            var visual = GameObject.CreatePrimitive(PrimitiveType.Capsule);
            visual.name = "Visual";
            visual.transform.SetParent(root.transform, false);
            visual.transform.localPosition = new Vector3(0f, 0.9f, 0f);
            visual.transform.localScale = new Vector3(0.8f, 0.9f, 0.8f);
            Object.DestroyImmediate(visual.GetComponent<Collider>());
            SetRendererColor(visual, new Color(0.3f, 0.44f, 0.64f));

            var cameraPitchRoot = new GameObject("CameraPitchRoot");
            cameraPitchRoot.transform.SetParent(root.transform, false);
            cameraPitchRoot.transform.localPosition = new Vector3(0f, 1.55f, 0f);

            var cameraObject = new GameObject("Main Camera", typeof(Camera), typeof(AudioListener));
            cameraObject.tag = "MainCamera";
            cameraObject.transform.SetParent(cameraPitchRoot.transform, false);
            cameraObject.transform.localPosition = Vector3.zero;
            cameraObject.transform.localRotation = Quaternion.identity;

            var camera = cameraObject.GetComponent<Camera>();
            camera.fieldOfView = 75f;
            camera.nearClipPlane = 0.05f;
            camera.farClipPlane = 250f;

            movement.ConfigureSceneReferences(cameraPitchRoot.transform, camera);

            return new PlayerBundle
            {
                Controller = movement,
                Interaction = interaction
            };
        }

        private static NpcChatTarget CreateNpc()
        {
            var npc = GameObject.CreatePrimitive(PrimitiveType.Capsule);
            npc.name = "NPC_Archivist";
            npc.transform.position = new Vector3(0f, 1f, 4f);
            SetRendererColor(npc, new Color(0.72f, 0.58f, 0.24f));

            var trigger = npc.AddComponent<SphereCollider>();
            trigger.radius = 2.6f;
            trigger.center = new Vector3(0f, 0.9f, 0f);
            trigger.isTrigger = true;

            var target = npc.AddComponent<NpcChatTarget>();
            target.Configure(
                "Archivist",
                "Неспешный смотритель тестовой локации, который отвечает коротко, понятно и по делу.",
                "Я слежу за этой тестовой площадкой. Спроси меня о прототипе, модели или самой сцене.",
                "Press E / Interact to talk");

            return target;
        }

        private static ChatBundle CreateUi()
        {
            var font = Resources.GetBuiltinResource<Font>("Arial.ttf");

            var chatCanvasObject = new GameObject("ChatCanvas", typeof(Canvas), typeof(CanvasScaler), typeof(GraphicRaycaster));
            var chatCanvas = chatCanvasObject.GetComponent<Canvas>();
            chatCanvas.renderMode = RenderMode.ScreenSpaceOverlay;
            var scaler = chatCanvasObject.GetComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1920f, 1080f);

            var panel = CreateUiObject("ChatPanel", chatCanvasObject.transform);
            panel.gameObject.AddComponent<Image>().color = new Color(0.06f, 0.08f, 0.12f, 0.92f);
            var panelRect = panel.GetComponent<RectTransform>();
            panelRect.anchorMin = new Vector2(0.18f, 0.08f);
            panelRect.anchorMax = new Vector2(0.82f, 0.58f);
            panelRect.offsetMin = Vector2.zero;
            panelRect.offsetMax = Vector2.zero;

            var chatWindow = panel.gameObject.AddComponent<ChatWindowController>();

            var header = CreateText("Header", panel, font, 28, TextAnchor.MiddleLeft, FontStyle.Bold);
            var headerRect = header.rectTransform;
            headerRect.anchorMin = new Vector2(0f, 1f);
            headerRect.anchorMax = new Vector2(1f, 1f);
            headerRect.pivot = new Vector2(0.5f, 1f);
            headerRect.sizeDelta = new Vector2(0f, 42f);
            headerRect.anchoredPosition = new Vector2(0f, -16f);
            header.color = Color.white;

            var status = CreateText("Status", panel, font, 16, TextAnchor.MiddleLeft, FontStyle.Normal);
            var statusRect = status.rectTransform;
            statusRect.anchorMin = new Vector2(0f, 1f);
            statusRect.anchorMax = new Vector2(1f, 1f);
            statusRect.pivot = new Vector2(0.5f, 1f);
            statusRect.sizeDelta = new Vector2(0f, 30f);
            statusRect.anchoredPosition = new Vector2(0f, -58f);
            status.color = new Color(0.75f, 0.82f, 0.89f);

            var scrollRoot = CreateUiObject("ScrollView", panel);
            scrollRoot.gameObject.AddComponent<Image>().color = new Color(0.11f, 0.14f, 0.18f, 0.85f);
            var scrollRectTransform = scrollRoot.GetComponent<RectTransform>();
            scrollRectTransform.anchorMin = new Vector2(0.03f, 0.22f);
            scrollRectTransform.anchorMax = new Vector2(0.97f, 0.78f);
            scrollRectTransform.offsetMin = Vector2.zero;
            scrollRectTransform.offsetMax = Vector2.zero;

            var viewport = CreateUiObject("Viewport", scrollRoot);
            var viewportImage = viewport.gameObject.AddComponent<Image>();
            viewportImage.color = new Color(0f, 0f, 0f, 0.001f);
            viewport.gameObject.AddComponent<Mask>().showMaskGraphic = false;
            var viewportRect = viewport.GetComponent<RectTransform>();
            viewportRect.anchorMin = Vector2.zero;
            viewportRect.anchorMax = Vector2.one;
            viewportRect.offsetMin = new Vector2(12f, 12f);
            viewportRect.offsetMax = new Vector2(-12f, -12f);

            var content = CreateUiObject("Content", viewport);
            var contentRect = content.GetComponent<RectTransform>();
            contentRect.anchorMin = new Vector2(0f, 1f);
            contentRect.anchorMax = new Vector2(1f, 1f);
            contentRect.pivot = new Vector2(0.5f, 1f);
            contentRect.anchoredPosition = Vector2.zero;
            contentRect.sizeDelta = new Vector2(0f, 0f);

            var layout = content.gameObject.AddComponent<VerticalLayoutGroup>();
            layout.spacing = 10f;
            layout.padding = new RectOffset(8, 8, 8, 8);
            layout.childControlWidth = true;
            layout.childControlHeight = true;
            layout.childForceExpandHeight = false;
            layout.childForceExpandWidth = true;

            var contentSizeFitter = content.gameObject.AddComponent<ContentSizeFitter>();
            contentSizeFitter.verticalFit = ContentSizeFitter.FitMode.PreferredSize;
            contentSizeFitter.horizontalFit = ContentSizeFitter.FitMode.Unconstrained;

            var scrollRect = scrollRoot.gameObject.AddComponent<ScrollRect>();
            scrollRect.viewport = viewportRect;
            scrollRect.content = contentRect;
            scrollRect.horizontal = false;

            var messageTemplate = CreateText("MessageTemplate", content, font, 18, TextAnchor.UpperLeft, FontStyle.Normal);
            messageTemplate.horizontalOverflow = HorizontalWrapMode.Wrap;
            messageTemplate.verticalOverflow = VerticalWrapMode.Overflow;
            messageTemplate.color = Color.white;
            var messageFitter = messageTemplate.gameObject.AddComponent<ContentSizeFitter>();
            messageFitter.verticalFit = ContentSizeFitter.FitMode.PreferredSize;
            messageFitter.horizontalFit = ContentSizeFitter.FitMode.Unconstrained;
            messageTemplate.gameObject.SetActive(false);

            var inputRoot = CreateUiObject("InputRoot", panel);
            var inputRootRect = inputRoot.GetComponent<RectTransform>();
            inputRootRect.anchorMin = new Vector2(0.03f, 0.04f);
            inputRootRect.anchorMax = new Vector2(0.74f, 0.16f);
            inputRootRect.offsetMin = Vector2.zero;
            inputRootRect.offsetMax = Vector2.zero;

            var inputField = CreateInputField(inputRoot, font);
            var sendButton = CreateButton("SendButton", panel, font, "Send");
            var sendRect = sendButton.GetComponent<RectTransform>();
            sendRect.anchorMin = new Vector2(0.77f, 0.04f);
            sendRect.anchorMax = new Vector2(0.87f, 0.16f);
            sendRect.offsetMin = Vector2.zero;
            sendRect.offsetMax = Vector2.zero;

            var closeButton = CreateButton("CloseButton", panel, font, "Close");
            var closeRect = closeButton.GetComponent<RectTransform>();
            closeRect.anchorMin = new Vector2(0.88f, 0.04f);
            closeRect.anchorMax = new Vector2(0.98f, 0.16f);
            closeRect.offsetMin = Vector2.zero;
            closeRect.offsetMax = Vector2.zero;

            chatWindow.Configure(header, status, scrollRect, contentRect, messageTemplate, inputField, sendButton.GetComponent<Button>(), closeButton.GetComponent<Button>());
            panel.gameObject.SetActive(false);

            var promptCanvasObject = new GameObject("PromptCanvas", typeof(Canvas), typeof(CanvasScaler), typeof(GraphicRaycaster));
            var promptCanvas = promptCanvasObject.GetComponent<Canvas>();
            promptCanvas.renderMode = RenderMode.ScreenSpaceOverlay;
            var promptScaler = promptCanvasObject.GetComponent<CanvasScaler>();
            promptScaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            promptScaler.referenceResolution = new Vector2(1920f, 1080f);

            var promptRoot = CreateUiObject("Prompt", promptCanvasObject.transform);
            promptRoot.gameObject.AddComponent<Image>().color = new Color(0f, 0f, 0f, 0.65f);
            var promptRect = promptRoot.GetComponent<RectTransform>();
            promptRect.anchorMin = new Vector2(0.35f, 0.03f);
            promptRect.anchorMax = new Vector2(0.65f, 0.09f);
            promptRect.offsetMin = Vector2.zero;
            promptRect.offsetMax = Vector2.zero;

            var promptLabel = CreateText("PromptLabel", promptRoot, font, 20, TextAnchor.MiddleCenter, FontStyle.Bold);
            promptLabel.rectTransform.anchorMin = Vector2.zero;
            promptLabel.rectTransform.anchorMax = Vector2.one;
            promptLabel.rectTransform.offsetMin = Vector2.zero;
            promptLabel.rectTransform.offsetMax = Vector2.zero;
            promptLabel.color = Color.white;

            var promptView = promptRoot.gameObject.AddComponent<InteractionPromptView>();
            promptView.Configure(promptLabel);
            promptView.Hide();

            return new ChatBundle
            {
                Window = chatWindow,
                Prompt = promptView
            };
        }

        private static void EnsureSceneInBuildSettings(string scenePath)
        {
            var currentScenes = EditorBuildSettings.scenes.ToList();
            if (currentScenes.Any(scene => scene.path == scenePath))
            {
                return;
            }

            currentScenes.Add(new EditorBuildSettingsScene(scenePath, true));
            EditorBuildSettings.scenes = currentScenes.ToArray();
        }

        private static void CreateWall(string objectName, Vector3 position, Vector3 scale)
        {
            CreateProp(objectName, position, scale, new Color(0.3f, 0.33f, 0.38f));
        }

        private static GameObject CreateProp(string objectName, Vector3 position, Vector3 scale, Color color)
        {
            var prop = GameObject.CreatePrimitive(PrimitiveType.Cube);
            prop.name = objectName;
            prop.transform.position = position;
            prop.transform.localScale = scale;
            SetRendererColor(prop, color);
            return prop;
        }

        private static void SetRendererColor(GameObject target, Color color)
        {
            var renderer = target.GetComponent<Renderer>();
            if (renderer == null)
            {
                return;
            }

            renderer.sharedMaterial = new Material(Shader.Find("Universal Render Pipeline/Lit"));
            renderer.sharedMaterial.color = color;
        }

        private static RectTransform CreateUiObject(string objectName, Transform parent)
        {
            var uiObject = new GameObject(objectName, typeof(RectTransform));
            var rectTransform = uiObject.GetComponent<RectTransform>();
            rectTransform.SetParent(parent, false);
            rectTransform.localScale = Vector3.one;
            return rectTransform;
        }

        private static Text CreateText(string objectName, Transform parent, Font font, int fontSize, TextAnchor alignment, FontStyle fontStyle)
        {
            var textObject = CreateUiObject(objectName, parent);
            var text = textObject.gameObject.AddComponent<Text>();
            text.font = font;
            text.fontSize = fontSize;
            text.alignment = alignment;
            text.fontStyle = fontStyle;
            text.supportRichText = false;
            text.text = objectName;
            text.color = Color.white;
            return text;
        }

        private static GameObject CreateButton(string objectName, Transform parent, Font font, string buttonLabel)
        {
            var buttonObject = CreateUiObject(objectName, parent).gameObject;
            var image = buttonObject.AddComponent<Image>();
            image.color = new Color(0.17f, 0.31f, 0.41f, 0.95f);
            buttonObject.AddComponent<Button>();

            var label = CreateText("Label", buttonObject.transform, font, 18, TextAnchor.MiddleCenter, FontStyle.Bold);
            label.text = buttonLabel;
            label.rectTransform.anchorMin = Vector2.zero;
            label.rectTransform.anchorMax = Vector2.one;
            label.rectTransform.offsetMin = Vector2.zero;
            label.rectTransform.offsetMax = Vector2.zero;

            return buttonObject;
        }

        private static InputField CreateInputField(Transform parent, Font font)
        {
            var inputObject = CreateUiObject("InputField", parent).gameObject;
            var background = inputObject.AddComponent<Image>();
            background.color = new Color(0.14f, 0.17f, 0.22f, 0.98f);
            var inputField = inputObject.AddComponent<InputField>();
            inputField.lineType = InputField.LineType.SingleLine;
            inputField.textComponent = CreateText("Text", inputObject.transform, font, 18, TextAnchor.MiddleLeft, FontStyle.Normal);
            inputField.textComponent.rectTransform.anchorMin = Vector2.zero;
            inputField.textComponent.rectTransform.anchorMax = Vector2.one;
            inputField.textComponent.rectTransform.offsetMin = new Vector2(14f, 6f);
            inputField.textComponent.rectTransform.offsetMax = new Vector2(-14f, -7f);
            inputField.textComponent.color = Color.white;
            inputField.textComponent.text = string.Empty;

            var placeholder = CreateText("Placeholder", inputObject.transform, font, 18, TextAnchor.MiddleLeft, FontStyle.Italic);
            placeholder.rectTransform.anchorMin = Vector2.zero;
            placeholder.rectTransform.anchorMax = Vector2.one;
            placeholder.rectTransform.offsetMin = new Vector2(14f, 6f);
            placeholder.rectTransform.offsetMax = new Vector2(-14f, -7f);
            placeholder.text = "Ask the NPC something...";
            placeholder.color = new Color(1f, 1f, 1f, 0.35f);
            inputField.placeholder = placeholder;

            return inputField;
        }
    }
}