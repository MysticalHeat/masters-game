using System.Collections.Generic;
using System.Linq;
using MastersGame.AI;
using MastersGame.Gameplay;
using MastersGame.UI;
using TMPro;
using Unity.InferenceEngine;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.UI;
using UnityEngine.Rendering;
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
            public PlayerHealth Health;
        }

        private sealed class ChatBundle
        {
            public ChatWindowController Window;
            public InteractionPromptView Prompt;
            public PlayerHudController Hud;
        }

        [MenuItem("Tools/LLM Chat/Create MVP Scene")]
        public static void CreateMvpScene()
        {
            EnsureScenesFolderExists();

            var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);

            var actionAsset = AssetDatabase.LoadAssetAtPath<InputActionAsset>("Assets/InputSystem_Actions.inputactions");
            if (actionAsset == null)
            {
                ShowDialog("Missing Input Actions", "Assets/InputSystem_Actions.inputactions was not found.");
                return;
            }

            var sunLight = CreateEnvironment();
            CreateEventSystem();

            var systems = new GameObject("Systems");
            var gameManager = systems.AddComponent<NpcChatGameManager>();
            var dayNightCycle = systems.AddComponent<DayNightCycle>();
            var llamaModel = systems.AddComponent<LlamaCppHttpLanguageModel>();
            var sentisModel = systems.AddComponent<SentisLocalLanguageModel>();
            var stubModel = systems.AddComponent<StubLocalLanguageModel>();
            llamaModel.Configure("http://127.0.0.1:8080", "qwen", true);
            sentisModel.modelAsset = AssetDatabase.LoadAssetAtPath<ModelAsset>("Assets/Models/Qwen/model.onnx");
            sentisModel.tokenizerJson = AssetDatabase.LoadAssetAtPath<TextAsset>("Assets/Models/Qwen/tokenizer.json");

            var chatBundle = CreateUi();
            var player = CreatePlayer(actionAsset);
            var npc = CreateNpc();

            gameManager.Configure(player.Controller, chatBundle.Window, llamaModel, sentisModel, stubModel);
            gameManager.SetWorldStateSources(player.Health, dayNightCycle);
            player.Interaction.Configure(gameManager, chatBundle.Prompt);
            dayNightCycle.Configure(sunLight, player.Controller.PlayerCamera);
            chatBundle.Hud.Bind(player.Health);
            chatBundle.Hud.BindTimeOfDay(dayNightCycle);

            Selection.activeGameObject = npc.gameObject;
            var saveSucceeded = EditorSceneManager.SaveScene(scene, ScenePath);
            if (!saveSucceeded)
            {
                ShowDialog("Scene Save Failed", $"Unity did not save the MVP scene to {ScenePath}.");
                return;
            }

            EnsureSceneInBuildSettings(ScenePath);
            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();
            EditorSceneManager.OpenScene(ScenePath, OpenSceneMode.Single);

            ShowDialog("MVP Scene Created", $"Scene saved to {ScenePath}. Open it and press Play to test the player, NPC and chat flow.");
        }

        private static void EnsureScenesFolderExists()
        {
            if (AssetDatabase.IsValidFolder("Assets/Scenes"))
            {
                return;
            }

            AssetDatabase.CreateFolder("Assets", "Scenes");
        }

        private static Light CreateEnvironment()
        {
            var sun = new GameObject("Sun");
            sun.transform.rotation = Quaternion.Euler(50f, -35f, 0f);
            var light = sun.AddComponent<Light>();
            light.type = LightType.Directional;
            light.intensity = 1.1f;
            light.color = new Color(1f, 0.96f, 0.84f, 1f);
            light.shadows = LightShadows.Soft;

            RenderSettings.ambientMode = AmbientMode.Flat;
            RenderSettings.ambientLight = new Color(0.62f, 0.67f, 0.75f, 1f);

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

            return light;
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
            playerInput.notificationBehavior = PlayerNotifications.InvokeCSharpEvents;

            var movement = root.AddComponent<PlayerController3D>();
            var interaction = root.AddComponent<PlayerInteractionController>();
            var health = root.GetComponent<PlayerHealth>();
            if (health == null)
            {
                health = root.AddComponent<PlayerHealth>();
            }

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
                Interaction = interaction,
                Health = health
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
                "Элдрик",
                "Ты — Элдрик, уставший стражник города Рифт. Ты ненавидишь магию, любишь крепкий эль, говоришь грубо, коротко и недоверчиво. Ты сторожишь ворота и улицы по ночам, презираешь пустую болтовню и замечаешь слабость, кровь и странности в людях. Никогда не выходи из роли стражника и отвечай как житель этого мира.",
                "Стой. Если есть дело — говори быстро. Если дела нет, не путайся под ногами.",
                "Press E / Interact to talk");

            return target;
        }

        private static ChatBundle CreateUi()
        {
            var chatCanvasObject = new GameObject("ChatCanvas", typeof(Canvas), typeof(CanvasScaler), typeof(GraphicRaycaster));
            var chatCanvas = chatCanvasObject.GetComponent<Canvas>();
            chatCanvas.renderMode = RenderMode.ScreenSpaceOverlay;
            chatCanvas.sortingOrder = 10;
            var scaler = chatCanvasObject.GetComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1920f, 1080f);

            var panel = CreateUiObject("ChatPanel", chatCanvasObject.transform);
            panel.gameObject.AddComponent<Image>().color = new Color(0.07f, 0.08f, 0.12f, 0.96f);
            var panelRect = panel.GetComponent<RectTransform>();
            panelRect.anchorMin = new Vector2(0.18f, 0.05f);
            panelRect.anchorMax = new Vector2(0.82f, 0.62f);
            panelRect.offsetMin = Vector2.zero;
            panelRect.offsetMax = Vector2.zero;

            var chatWindow = panel.gameObject.AddComponent<ChatWindowController>();
            var playerHud = CreatePlayerHud(chatCanvasObject.transform);

            var headerBar = CreateUiObject("HeaderBar", panel);
            headerBar.gameObject.AddComponent<Image>().color = new Color(0.09f, 0.11f, 0.16f, 1f);
            var headerBarRect = headerBar.GetComponent<RectTransform>();
            headerBarRect.anchorMin = new Vector2(0f, 1f);
            headerBarRect.anchorMax = new Vector2(1f, 1f);
            headerBarRect.pivot = new Vector2(0.5f, 1f);
            headerBarRect.sizeDelta = new Vector2(0f, 56f);

            var title = CreateTmpText("Title", headerBar, 22, TextAlignmentOptions.MidlineLeft, FontStyles.Bold);
            var titleRect = title.rectTransform;
            titleRect.anchorMin = new Vector2(0f, 0.5f);
            titleRect.anchorMax = new Vector2(0.8f, 1f);
            titleRect.offsetMin = new Vector2(18f, 0f);
            titleRect.offsetMax = new Vector2(0f, -4f);
            title.color = Color.white;

            var status = CreateTmpText("Status", headerBar, 12, TextAlignmentOptions.MidlineLeft, FontStyles.Normal);
            var statusRect = status.rectTransform;
            statusRect.anchorMin = new Vector2(0f, 0f);
            statusRect.anchorMax = new Vector2(0.8f, 0.5f);
            statusRect.offsetMin = new Vector2(18f, 4f);
            statusRect.offsetMax = Vector2.zero;
            status.color = new Color(0.55f, 0.63f, 0.72f);

            var closeButton = CreateTmpButton("CloseButton", headerBar, "\u2715", new Color(0.50f, 0.15f, 0.18f, 0.95f));
            var closeRect = closeButton.GetComponent<RectTransform>();
            closeRect.anchorMin = new Vector2(1f, 0.1f);
            closeRect.anchorMax = new Vector2(1f, 0.9f);
            closeRect.pivot = new Vector2(1f, 0.5f);
            closeRect.sizeDelta = new Vector2(50f, 0f);
            closeRect.anchoredPosition = new Vector2(-6f, 0f);

            var scrollRoot = CreateUiObject("ScrollView", panel);
            scrollRoot.gameObject.AddComponent<Image>().color = new Color(0.05f, 0.06f, 0.09f, 0.90f);
            var scrollRootRect = scrollRoot.GetComponent<RectTransform>();
            scrollRootRect.anchorMin = new Vector2(0f, 0f);
            scrollRootRect.anchorMax = new Vector2(1f, 1f);
            scrollRootRect.offsetMin = new Vector2(0f, 52f);
            scrollRootRect.offsetMax = new Vector2(0f, -56f);

            var viewport = CreateUiObject("Viewport", scrollRoot);
            viewport.gameObject.AddComponent<Image>().color = new Color(0f, 0f, 0f, 0.001f);
            viewport.gameObject.AddComponent<RectMask2D>();
            var viewportRect = viewport.GetComponent<RectTransform>();
            viewportRect.anchorMin = Vector2.zero;
            viewportRect.anchorMax = Vector2.one;
            viewportRect.offsetMin = new Vector2(8f, 8f);
            viewportRect.offsetMax = new Vector2(-8f, -8f);

            var content = CreateUiObject("Content", viewport);
            var contentRect = content.GetComponent<RectTransform>();
            contentRect.anchorMin = new Vector2(0f, 1f);
            contentRect.anchorMax = new Vector2(1f, 1f);
            contentRect.pivot = new Vector2(0.5f, 1f);
            contentRect.sizeDelta = new Vector2(0f, 0f);

            var contentLayout = content.gameObject.AddComponent<VerticalLayoutGroup>();
            contentLayout.spacing = 8f;
            contentLayout.padding = new RectOffset(6, 6, 6, 6);
            contentLayout.childControlWidth = true;
            contentLayout.childControlHeight = true;
            contentLayout.childForceExpandHeight = false;
            contentLayout.childForceExpandWidth = true;

            var contentFitter = content.gameObject.AddComponent<ContentSizeFitter>();
            contentFitter.verticalFit = ContentSizeFitter.FitMode.PreferredSize;
            contentFitter.horizontalFit = ContentSizeFitter.FitMode.Unconstrained;

            var scrollRect = scrollRoot.gameObject.AddComponent<ScrollRect>();
            scrollRect.viewport = viewportRect;
            scrollRect.content = contentRect;
            scrollRect.horizontal = false;
            scrollRect.scrollSensitivity = 30f;

            var inputBar = CreateUiObject("InputBar", panel);
            inputBar.gameObject.AddComponent<Image>().color = new Color(0.09f, 0.10f, 0.15f, 1f);
            var inputBarRect = inputBar.GetComponent<RectTransform>();
            inputBarRect.anchorMin = new Vector2(0f, 0f);
            inputBarRect.anchorMax = new Vector2(1f, 0f);
            inputBarRect.pivot = new Vector2(0.5f, 0f);
            inputBarRect.sizeDelta = new Vector2(0f, 52f);

            var inputBarLayout = inputBar.gameObject.AddComponent<HorizontalLayoutGroup>();
            inputBarLayout.spacing = 6f;
            inputBarLayout.padding = new RectOffset(10, 10, 8, 8);
            inputBarLayout.childControlWidth = true;
            inputBarLayout.childControlHeight = true;
            inputBarLayout.childForceExpandWidth = false;
            inputBarLayout.childForceExpandHeight = true;

            var inputField = CreateTmpInputField(inputBar);
            var inputFieldElement = inputField.gameObject.AddComponent<LayoutElement>();
            inputFieldElement.flexibleWidth = 1f;
            inputFieldElement.minWidth = 100f;

            var sendButton = CreateTmpButton("SendButton", inputBar, "Send", new Color(0.20f, 0.45f, 0.65f, 1f));
            var sendElement = sendButton.gameObject.AddComponent<LayoutElement>();
            sendElement.preferredWidth = 72f;
            sendElement.flexibleWidth = 0f;

            chatWindow.Configure(title, status, scrollRect, contentRect, inputField, sendButton, closeButton);
            panel.gameObject.SetActive(false);

            var promptCanvasObject = new GameObject("PromptCanvas", typeof(Canvas), typeof(CanvasScaler), typeof(GraphicRaycaster));
            var promptCanvas = promptCanvasObject.GetComponent<Canvas>();
            promptCanvas.renderMode = RenderMode.ScreenSpaceOverlay;
            var promptScaler = promptCanvasObject.GetComponent<CanvasScaler>();
            promptScaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            promptScaler.referenceResolution = new Vector2(1920f, 1080f);

            var promptRoot = CreateUiObject("Prompt", promptCanvasObject.transform);
            promptRoot.gameObject.AddComponent<Image>().color = new Color(0.06f, 0.07f, 0.10f, 0.82f);
            var promptRect = promptRoot.GetComponent<RectTransform>();
            promptRect.anchorMin = new Vector2(0.35f, 0.03f);
            promptRect.anchorMax = new Vector2(0.65f, 0.08f);
            promptRect.offsetMin = Vector2.zero;
            promptRect.offsetMax = Vector2.zero;

            var promptLabel = CreateTmpText("PromptLabel", promptRoot, 17, TextAlignmentOptions.Center, FontStyles.Bold);
            promptLabel.rectTransform.anchorMin = Vector2.zero;
            promptLabel.rectTransform.anchorMax = Vector2.one;
            promptLabel.rectTransform.offsetMin = Vector2.zero;
            promptLabel.rectTransform.offsetMax = Vector2.zero;
            promptLabel.color = new Color(0.90f, 0.93f, 1f);

            var promptView = promptRoot.gameObject.AddComponent<InteractionPromptView>();
            promptView.Configure(promptLabel);
            promptView.Hide();

            return new ChatBundle
            {
                Window = chatWindow,
                Prompt = promptView,
                Hud = playerHud
            };
        }

        private static PlayerHudController CreatePlayerHud(Transform parent)
        {
            var hudPanel = CreateUiObject("PlayerHudPanel", parent);
            hudPanel.gameObject.AddComponent<Image>().color = new Color(0.07f, 0.08f, 0.12f, 0.92f);
            var hudPanelRect = hudPanel.GetComponent<RectTransform>();
            hudPanelRect.anchorMin = new Vector2(0.02f, 0.68f);
            hudPanelRect.anchorMax = new Vector2(0.24f, 0.96f);
            hudPanelRect.offsetMin = Vector2.zero;
            hudPanelRect.offsetMax = Vector2.zero;

            var hudLayout = hudPanel.gameObject.AddComponent<VerticalLayoutGroup>();
            hudLayout.padding = new RectOffset(14, 14, 12, 12);
            hudLayout.spacing = 8f;
            hudLayout.childControlWidth = true;
            hudLayout.childControlHeight = true;
            hudLayout.childForceExpandWidth = true;
            hudLayout.childForceExpandHeight = false;

            var title = CreateTmpText("PlayerLabel", hudPanel, 16, TextAlignmentOptions.MidlineLeft, FontStyles.Bold);
            title.text = "Player";
            title.color = Color.white;

            var healthRow = CreateUiObject("HealthRow", hudPanel);
            var healthRowLayout = healthRow.gameObject.AddComponent<HorizontalLayoutGroup>();
            healthRowLayout.spacing = 6f;
            healthRowLayout.childControlWidth = true;
            healthRowLayout.childControlHeight = true;
            healthRowLayout.childForceExpandWidth = false;
            healthRowLayout.childForceExpandHeight = false;

            var healthLabel = CreateTmpText("HealthLabel", healthRow, 13, TextAlignmentOptions.MidlineLeft, FontStyles.Bold);
            healthLabel.text = "Health";
            healthLabel.color = new Color(0.70f, 0.76f, 0.86f);
            var healthLabelElement = healthLabel.gameObject.AddComponent<LayoutElement>();
            healthLabelElement.flexibleWidth = 1f;

            var healthValue = CreateTmpText("HealthValue", healthRow, 13, TextAlignmentOptions.MidlineRight, FontStyles.Bold);
            healthValue.text = "--";
            healthValue.color = Color.white;

            var healthBar = CreateUiObject("HealthBar", hudPanel);
            healthBar.gameObject.AddComponent<Image>().color = new Color(0.14f, 0.16f, 0.22f, 1f);
            var healthBarElement = healthBar.gameObject.AddComponent<LayoutElement>();
            healthBarElement.preferredHeight = 18f;

            var healthFill = CreateUiObject("Fill", healthBar);
            var healthFillRect = healthFill.GetComponent<RectTransform>();
            healthFillRect.anchorMin = Vector2.zero;
            healthFillRect.anchorMax = Vector2.one;
            healthFillRect.offsetMin = new Vector2(2f, 2f);
            healthFillRect.offsetMax = new Vector2(-2f, -2f);

            var healthFillImage = healthFill.gameObject.AddComponent<Image>();
            healthFillImage.color = new Color(0.18f, 0.72f, 0.32f, 1f);
            healthFillImage.type = Image.Type.Filled;
            healthFillImage.fillMethod = Image.FillMethod.Horizontal;
            healthFillImage.fillOrigin = (int)Image.OriginHorizontal.Left;
            healthFillImage.fillAmount = 1f;

            var timeRow = CreateUiObject("TimeRow", hudPanel);
            var timeRowLayout = timeRow.gameObject.AddComponent<HorizontalLayoutGroup>();
            timeRowLayout.spacing = 6f;
            timeRowLayout.childControlWidth = true;
            timeRowLayout.childControlHeight = true;
            timeRowLayout.childForceExpandWidth = false;
            timeRowLayout.childForceExpandHeight = false;

            var timeLabel = CreateTmpText("TimeLabel", timeRow, 13, TextAlignmentOptions.MidlineLeft, FontStyles.Bold);
            timeLabel.text = "Time";
            timeLabel.color = new Color(0.70f, 0.76f, 0.86f);
            var timeLabelElement = timeLabel.gameObject.AddComponent<LayoutElement>();
            timeLabelElement.flexibleWidth = 1f;

            var timeValue = CreateTmpText("TimeValue", timeRow, 13, TextAlignmentOptions.MidlineRight, FontStyles.Bold);
            timeValue.text = "--";
            timeValue.color = Color.white;

            var damageButton = CreateTmpButton("DamageButton", hudPanel, "-10 HP [H]", new Color(0.57f, 0.18f, 0.20f, 0.98f));
            var damageButtonElement = damageButton.gameObject.AddComponent<LayoutElement>();
            damageButtonElement.preferredHeight = 34f;

            var toggleDayNightButton = CreateTmpButton("DayNightButton", hudPanel, "Toggle Day/Night [N]", new Color(0.20f, 0.33f, 0.56f, 0.98f));
            var toggleDayNightElement = toggleDayNightButton.gameObject.AddComponent<LayoutElement>();
            toggleDayNightElement.preferredHeight = 34f;

            var playerHud = hudPanel.gameObject.AddComponent<PlayerHudController>();
            playerHud.Configure(healthValue, healthFillImage, damageButton, timeValue, toggleDayNightButton);
            return playerHud;
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

        private static TextMeshProUGUI CreateTmpText(string objectName, Transform parent, int fontSize, TextAlignmentOptions alignment, FontStyles fontStyle)
        {
            var textObject = CreateUiObject(objectName, parent);
            var text = textObject.gameObject.AddComponent<TextMeshProUGUI>();
            text.fontSize = fontSize;
            text.alignment = alignment;
            text.fontStyle = fontStyle;
            text.color = Color.white;
            text.text = objectName;
            return text;
        }

        private static Button CreateTmpButton(string objectName, Transform parent, string label, Color backgroundColor)
        {
            var buttonObject = CreateUiObject(objectName, parent).gameObject;
            var image = buttonObject.AddComponent<Image>();
            image.color = backgroundColor;
            var button = buttonObject.AddComponent<Button>();

            var labelObj = CreateUiObject("Label", buttonObject.transform);
            var labelRect = labelObj.GetComponent<RectTransform>();
            labelRect.anchorMin = Vector2.zero;
            labelRect.anchorMax = Vector2.one;
            labelRect.offsetMin = Vector2.zero;
            labelRect.offsetMax = Vector2.zero;

            var labelText = labelObj.gameObject.AddComponent<TextMeshProUGUI>();
            labelText.text = label;
            labelText.fontSize = 15;
            labelText.fontStyle = FontStyles.Bold;
            labelText.color = Color.white;
            labelText.alignment = TextAlignmentOptions.Center;

            return button;
        }

        private static TMP_InputField CreateTmpInputField(Transform parent)
        {
            var inputObject = CreateUiObject("InputField", parent).gameObject;
            var background = inputObject.AddComponent<Image>();
            background.color = new Color(0.14f, 0.16f, 0.22f, 1f);

            var inputField = inputObject.AddComponent<TMP_InputField>();

            var textArea = CreateUiObject("Text Area", inputObject.transform);
            textArea.gameObject.AddComponent<RectMask2D>();
            var textAreaRect = textArea.GetComponent<RectTransform>();
            textAreaRect.anchorMin = Vector2.zero;
            textAreaRect.anchorMax = Vector2.one;
            textAreaRect.offsetMin = new Vector2(10f, 0f);
            textAreaRect.offsetMax = new Vector2(-10f, 0f);

            var placeholder = CreateUiObject("Placeholder", textArea);
            var placeholderRect = placeholder.GetComponent<RectTransform>();
            placeholderRect.anchorMin = Vector2.zero;
            placeholderRect.anchorMax = Vector2.one;
            placeholderRect.offsetMin = Vector2.zero;
            placeholderRect.offsetMax = Vector2.zero;

            var placeholderText = placeholder.gameObject.AddComponent<TextMeshProUGUI>();
            placeholderText.text = "Type a message...";
            placeholderText.fontSize = 15;
            placeholderText.fontStyle = FontStyles.Italic;
            placeholderText.color = new Color(1f, 1f, 1f, 0.35f);
            placeholderText.textWrappingMode = TextWrappingModes.NoWrap;
            placeholderText.alignment = TextAlignmentOptions.MidlineLeft;

            var textObj = CreateUiObject("Text", textArea);
            var textRect = textObj.GetComponent<RectTransform>();
            textRect.anchorMin = Vector2.zero;
            textRect.anchorMax = Vector2.one;
            textRect.offsetMin = Vector2.zero;
            textRect.offsetMax = Vector2.zero;

            var text = textObj.gameObject.AddComponent<TextMeshProUGUI>();
            text.text = string.Empty;
            text.fontSize = 15;
            text.color = Color.white;
            text.textWrappingMode = TextWrappingModes.NoWrap;
            text.alignment = TextAlignmentOptions.MidlineLeft;

            inputField.textViewport = textAreaRect;
            inputField.textComponent = text;
            inputField.placeholder = placeholderText;
            inputField.lineType = TMP_InputField.LineType.SingleLine;
            inputField.caretColor = Color.white;
            inputField.selectionColor = new Color(0.25f, 0.45f, 0.65f, 0.45f);

            return inputField;
        }

        private static void ShowDialog(string title, string message)
        {
            if (Application.isBatchMode)
            {
                Debug.Log($"[{nameof(NpcChatMvpSceneBuilder)}] {title}: {message}");
                return;
            }

            EditorUtility.DisplayDialog(title, message, "OK");
        }
    }
}
