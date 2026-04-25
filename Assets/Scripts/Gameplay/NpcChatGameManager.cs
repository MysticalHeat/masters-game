using System;
using System.Collections.Generic;
using System.Threading;
using MastersGame.AI;
using MastersGame.UI;
using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem;
using UnityEngine.Rendering;

namespace MastersGame.Gameplay
{
    public class NpcChatGameManager : MonoBehaviour
    {
        [SerializeField] private PlayerController3D playerController;
        [SerializeField] private PlayerHealth playerHealth;
        [SerializeField] private DayNightCycle dayNightCycle;
        [SerializeField] private ChatWindowController chatWindow;
        [SerializeField] private LlamaCppHttpLanguageModel llamaModel;
        [SerializeField] private SentisLocalLanguageModel sentisModel;
        [SerializeField] private StubLocalLanguageModel stubModel;
        [SerializeField] private int maxVisibleHistory = 20;
        [SerializeField] private bool enableDebugLogging = true;

        private readonly List<ChatMessage> history = new();
        private readonly object streamingSync = new();

        private CancellationTokenSource generationCancellation;
        private NpcChatTarget currentNpc;
        private bool isBusy;
        private bool hasPendingStreamingUpdate;
        private string pendingStreamingText;

        public bool ChatOpen { get; private set; }

        public void Configure(PlayerController3D controller, ChatWindowController window, LlamaCppHttpLanguageModel llama, SentisLocalLanguageModel sentis, StubLocalLanguageModel stub)
        {
            playerController = controller;
            playerHealth = controller != null ? controller.GetComponent<PlayerHealth>() : null;
            chatWindow = window;
            llamaModel = llama;
            sentisModel = sentis;
            stubModel = stub;
        }

        public void SetWorldStateSources(PlayerHealth health, DayNightCycle cycle)
        {
            playerHealth = health;
            dayNightCycle = cycle;
        }

        private void Awake()
        {
            if (chatWindow != null)
            {
                chatWindow.SendRequested += HandleSendRequested;
                chatWindow.CloseRequested += CloseConversation;
            }
        }

        private void Update()
        {
            FlushPendingStreamingUpdate();
        }

        private void OnDestroy()
        {
            if (chatWindow != null)
            {
                chatWindow.SendRequested -= HandleSendRequested;
                chatWindow.CloseRequested -= CloseConversation;
            }

            CancelGeneration();
        }

        public void OpenConversation(NpcChatTarget npc)
        {
            if (npc == null || isBusy)
            {
                return;
            }

            currentNpc = npc;
            ChatOpen = true;
            history.Clear();

            playerController.SetInputEnabled(false);
            playerController.SetCursorLocked(false);

            chatWindow.Show(npc.NpcName, GetModel().StatusSummary);

            if (!string.IsNullOrWhiteSpace(npc.Greeting))
            {
                AddNpcMessage(npc.Greeting);
            }

            chatWindow.FocusInput();
        }

        public void CloseConversation()
        {
            CancelGeneration();
            ClearPendingStreamingUpdate();

            ChatOpen = false;
            isBusy = false;
            currentNpc = null;
            history.Clear();

            if (chatWindow != null)
            {
                chatWindow.Hide();
            }

            if (playerController != null)
            {
                playerController.SetInputEnabled(true);
                playerController.SetCursorLocked(true);
            }
        }

        private async void HandleSendRequested(string message)
        {
            if (!ChatOpen || isBusy || currentNpc == null)
            {
                return;
            }

            var trimmedMessage = message?.Trim();
            if (string.IsNullOrEmpty(trimmedMessage))
            {
                return;
            }

            var chatRequest = currentNpc.BuildRequest(new List<ChatMessage>(history), trimmedMessage, BuildWorldContext());
            AddPlayerMessage(trimmedMessage);

            if (NpcConversationSupport.ShouldRefuseForLowAffinity(chatRequest))
            {
                var refusal = NpcConversationSupport.BuildLowAffinityRefusal(chatRequest);
                LogDebug($"{currentNpc.NpcName} refuses because affinity is {currentNpc.Affinity}: {refusal}");
                AddNpcMessage(refusal);
                chatWindow.SetBusy(false, GetModel().StatusSummary);
                chatWindow.FocusInput();
                return;
            }

            isBusy = true;
            generationCancellation = new CancellationTokenSource();
            var languageModel = GetModel();
            LogDebug($"Sending message to {currentNpc.NpcName} via {languageModel.DisplayName}: {trimmedMessage}");
            chatWindow.SetBusy(true, $"Генерация через {languageModel.DisplayName}...");

            try
            {
                if (languageModel is IStreamingLocalLanguageModel streamingModel)
                {
                    chatWindow.BeginStreamingNpcMessage(currentNpc.NpcName);
                    var streamedReply = await streamingModel.GenerateReplyStreamingAsync(chatRequest, HandleStreamingPartialText, generationCancellation.Token);
                    FlushPendingStreamingUpdate();

                    if (!ChatOpen || currentNpc == null)
                    {
                        return;
                    }

                    var sanitizedStreamedReply = NpcConversationSupport.SanitizeNpcReply(streamedReply, chatRequest, out var streamedRelationshipDelta);
                    ApplyRelationshipDelta(streamedRelationshipDelta);
                    LogDebug($"Reply from {languageModel.DisplayName}: {sanitizedStreamedReply}");
                    chatWindow.CompleteStreamingNpcMessage(sanitizedStreamedReply);
                    AddHistoryMessage(new ChatMessage(ChatRole.Npc, sanitizedStreamedReply));
                    chatWindow.SetBusy(false, languageModel.StatusSummary);
                    chatWindow.FocusInput();
                    return;
                }

                var reply = await languageModel.GenerateReplyAsync(chatRequest, generationCancellation.Token);

                if (!ChatOpen || currentNpc == null)
                {
                    return;
                }

                var sanitizedReply = NpcConversationSupport.SanitizeNpcReply(reply, chatRequest, out var relationshipDelta);
                ApplyRelationshipDelta(relationshipDelta);
                LogDebug($"Reply from {languageModel.DisplayName}: {sanitizedReply}");
                AddNpcMessage(sanitizedReply);
                chatWindow.SetBusy(false, languageModel.StatusSummary);
                chatWindow.FocusInput();
            }
            catch (OperationCanceledException)
            {
                if (ChatOpen)
                {
                    ClearPendingStreamingUpdate();
                    chatWindow.CancelStreamingNpcMessage();
                    chatWindow.SetBusy(false, "Генерация отменена.");
                }
            }
            catch (Exception exception)
            {
                Debug.LogException(exception);
                if (ChatOpen)
                {
                    ClearPendingStreamingUpdate();
                    chatWindow.CancelStreamingNpcMessage();
                    AddNpcMessage(NpcConversationSupport.BuildFallbackReply(chatRequest));
                    chatWindow.SetBusy(false, "Генерация не удалась.");
                }
            }
            finally
            {
                isBusy = false;
                generationCancellation?.Dispose();
                generationCancellation = null;
            }
        }

        private ILocalLanguageModel GetModel()
        {
            if (llamaModel != null && llamaModel.IsConfigured)
            {
                return llamaModel;
            }

            if (sentisModel != null && sentisModel.IsConfigured)
            {
                return sentisModel;
            }

            return stubModel;
        }

        private WorldContextSnapshot BuildWorldContext()
        {
            var phase = dayNightCycle != null ? dayNightCycle.PhaseDisplayName : "Unknown";
            var formattedTime = dayNightCycle != null ? dayNightCycle.FormattedTime : "--:--";
            var currentHealth = playerHealth != null ? playerHealth.CurrentHealth : 100f;
            var maxHealth = playerHealth != null ? playerHealth.MaxHealth : 100f;
            return new WorldContextSnapshot(phase, formattedTime, currentHealth, maxHealth);
        }

        private void AddPlayerMessage(string text)
        {
            AddHistoryMessage(new ChatMessage(ChatRole.Player, text));
            chatWindow.AppendMessage(ChatRole.Player, "Player", text);
        }

        private void AddNpcMessage(string text)
        {
            AddHistoryMessage(new ChatMessage(ChatRole.Npc, text));
            chatWindow.AppendMessage(ChatRole.Npc, currentNpc != null ? currentNpc.NpcName : "NPC", text);
        }

        private void AddHistoryMessage(ChatMessage message)
        {
            history.Add(message);
            var overflow = history.Count - maxVisibleHistory;
            if (overflow > 0)
            {
                history.RemoveRange(0, overflow);
            }
        }

        private void ApplyRelationshipDelta(int delta)
        {
            if (currentNpc == null || delta == 0)
            {
                return;
            }

            var previousAffinity = currentNpc.Affinity;
            currentNpc.ApplyAffinityDelta(delta);
            LogDebug($"{currentNpc.NpcName} affinity changed by {delta}: {previousAffinity} -> {currentNpc.Affinity}");
        }

        private void CancelGeneration()
        {
            if (generationCancellation == null)
            {
                return;
            }

            generationCancellation.Cancel();
            generationCancellation.Dispose();
            generationCancellation = null;
        }

        private void HandleStreamingPartialText(string text)
        {
            if (string.IsNullOrWhiteSpace(text))
            {
                return;
            }

            lock (streamingSync)
            {
                pendingStreamingText = SanitizeStreamingPreview(text);
                hasPendingStreamingUpdate = true;
            }
        }

        private void FlushPendingStreamingUpdate()
        {
            string textToApply = null;

            lock (streamingSync)
            {
                if (!hasPendingStreamingUpdate)
                {
                    return;
                }

                textToApply = pendingStreamingText;
                hasPendingStreamingUpdate = false;
            }

            if (!ChatOpen || chatWindow == null)
            {
                return;
            }

            chatWindow.UpdateStreamingNpcMessage(textToApply);
        }

        private void ClearPendingStreamingUpdate()
        {
            lock (streamingSync)
            {
                pendingStreamingText = null;
                hasPendingStreamingUpdate = false;
            }
        }

        private string SanitizeStreamingPreview(string text)
        {
            var previewRequest = currentNpc != null
                ? currentNpc.BuildRequest(Array.Empty<ChatMessage>(), string.Empty, BuildWorldContext())
                : new ChatRequest("NPC", string.Empty, string.Empty, Array.Empty<ChatMessage>(), string.Empty, BuildWorldContext(), 0);

            var sanitized = NpcConversationSupport.SanitizeNpcReply(text, previewRequest);
            return string.IsNullOrWhiteSpace(sanitized)
                ? NpcConversationSupport.BuildFallbackReply(previewRequest)
                : sanitized;
        }

        private void LogDebug(string message)
        {
            if (!enableDebugLogging)
            {
                return;
            }

            Debug.Log($"[NpcChatGameManager] {message}");
        }
    }

    public enum DayNightPhase
    {
        Dawn,
        Day,
        Dusk,
        Night
    }

    public class DayNightCycle : MonoBehaviour
    {
        [Header("References")]
        [SerializeField] private Light sunLight;
        [SerializeField] private Camera sceneCamera;

        [Header("Cycle")]
        [SerializeField] private bool autoCycle = true;
        [SerializeField] [Min(30f)] private float fullCycleDurationSeconds = 240f;
        [SerializeField] [Range(0f, 1f)] private float startingTimeNormalized = 0.35f;
        [SerializeField] private Key toggleDayNightHotkey = Key.N;

        [Header("Lighting")]
        [SerializeField] private float sunAzimuth = -35f;
        [SerializeField] [Min(0f)] private float daySunIntensity = 1.1f;
        [SerializeField] [Min(0f)] private float nightSunIntensity = 0.08f;
        [SerializeField] private Color daySunColor = new Color(1f, 0.96f, 0.84f, 1f);
        [SerializeField] private Color nightSunColor = new Color(0.30f, 0.42f, 0.65f, 1f);
        [SerializeField] private Color dayAmbientColor = new Color(0.62f, 0.67f, 0.75f, 1f);
        [SerializeField] private Color nightAmbientColor = new Color(0.08f, 0.11f, 0.18f, 1f);

        [Header("Atmosphere")]
        [SerializeField] private bool enableFog = true;
        [SerializeField] private Color daySkyColor = new Color(0.50f, 0.74f, 0.97f, 1f);
        [SerializeField] private Color twilightSkyColor = new Color(0.94f, 0.48f, 0.32f, 1f);
        [SerializeField] private Color nightSkyColor = new Color(0.05f, 0.07f, 0.13f, 1f);
        [SerializeField] private Color dayFogColor = new Color(0.76f, 0.84f, 0.92f, 1f);
        [SerializeField] private Color twilightFogColor = new Color(0.68f, 0.44f, 0.40f, 1f);
        [SerializeField] private Color nightFogColor = new Color(0.08f, 0.11f, 0.18f, 1f);
        [SerializeField] [Min(0f)] private float dayFogDensity = 0.004f;
        [SerializeField] [Min(0f)] private float nightFogDensity = 0.02f;

        private float currentTimeNormalized;
        private bool initialized;

        public event Action<DayNightCycle> StateChanged;

        public float CurrentTimeNormalized => currentTimeNormalized;

        public string FormattedTime => FormatTime(currentTimeNormalized);

        public DayNightPhase CurrentPhase => EvaluatePhase(currentTimeNormalized);

        public string PhaseDisplayName => GetPhaseDisplayName(CurrentPhase);

        public bool IsNightPhase => CurrentPhase == DayNightPhase.Night;

        public string PhaseDisplayNameRu => GetLocalizedPhaseDisplayName(CurrentPhase);

        public void Configure(Light directionalLight, Camera targetCamera)
        {
            sunLight = directionalLight;
            sceneCamera = targetCamera;
            EnsureInitialized();
            ApplyLighting();
            NotifyStateChanged();
        }

        private void Awake()
        {
            EnsureInitialized();
            ApplyLighting();
        }

        private void OnEnable()
        {
            ApplyLighting();
            NotifyStateChanged();
        }

        private void Update()
        {
            var lightingChanged = false;

            if (autoCycle && fullCycleDurationSeconds > 0f)
            {
                currentTimeNormalized = Mathf.Repeat(currentTimeNormalized + (Time.deltaTime / fullCycleDurationSeconds), 1f);
                lightingChanged = true;
            }

            if (toggleDayNightHotkey != Key.None && Keyboard.current != null && !IsTextInputFocused())
            {
                var toggleKey = Keyboard.current[toggleDayNightHotkey];
                if (toggleKey != null && toggleKey.wasPressedThisFrame)
                {
                    ToggleDayNight();
                    return;
                }
            }

            if (!lightingChanged)
            {
                return;
            }

            ApplyLighting();
            NotifyStateChanged();
        }

        private void OnValidate()
        {
            fullCycleDurationSeconds = Mathf.Max(30f, fullCycleDurationSeconds);
            startingTimeNormalized = Mathf.Repeat(startingTimeNormalized, 1f);
            dayFogDensity = Mathf.Max(0f, dayFogDensity);
            nightFogDensity = Mathf.Max(0f, nightFogDensity);

            if (!Application.isPlaying)
            {
                currentTimeNormalized = startingTimeNormalized;
                return;
            }

            if (!initialized)
            {
                return;
            }

            currentTimeNormalized = Mathf.Repeat(currentTimeNormalized, 1f);
            ApplyLighting();
            NotifyStateChanged();
        }

        public void ToggleDayNight()
        {
            SetTimeNormalized(IsNightPhase ? 0.5f : 0f);
        }

        private void EnsureInitialized()
        {
            if (initialized)
            {
                return;
            }

            fullCycleDurationSeconds = Mathf.Max(30f, fullCycleDurationSeconds);
            currentTimeNormalized = Mathf.Repeat(startingTimeNormalized, 1f);
            initialized = true;
        }

        private void SetTimeNormalized(float value)
        {
            EnsureInitialized();

            var normalizedValue = Mathf.Repeat(value, 1f);
            if (Mathf.Approximately(currentTimeNormalized, normalizedValue))
            {
                return;
            }

            currentTimeNormalized = normalizedValue;
            ApplyLighting();
            NotifyStateChanged();
        }

        private void ApplyLighting()
        {
            var daylightFactor = EvaluateDaylightFactor(currentTimeNormalized);
            var twilightFactor = EvaluateTwilightFactor(currentTimeNormalized);

            if (sunLight != null)
            {
                sunLight.transform.rotation = Quaternion.Euler((currentTimeNormalized * 360f) - 90f, sunAzimuth, 0f);
                sunLight.color = Color.Lerp(nightSunColor, daySunColor, daylightFactor);
                sunLight.intensity = Mathf.Lerp(nightSunIntensity, daySunIntensity, daylightFactor);
                sunLight.shadows = LightShadows.Soft;
            }

            RenderSettings.ambientMode = AmbientMode.Flat;
            RenderSettings.ambientLight = Color.Lerp(nightAmbientColor, dayAmbientColor, daylightFactor);
            RenderSettings.fog = enableFog;
            RenderSettings.fogMode = FogMode.Exponential;
            RenderSettings.fogColor = EvaluateAtmosphereColor(dayFogColor, twilightFogColor, nightFogColor, daylightFactor, twilightFactor);
            RenderSettings.fogDensity = Mathf.Lerp(nightFogDensity, dayFogDensity, daylightFactor);

            if (sceneCamera != null)
            {
                sceneCamera.clearFlags = CameraClearFlags.SolidColor;
                sceneCamera.backgroundColor = EvaluateAtmosphereColor(daySkyColor, twilightSkyColor, nightSkyColor, daylightFactor, twilightFactor);
            }
        }

        private void NotifyStateChanged()
        {
            StateChanged?.Invoke(this);
        }

        private static float EvaluateDaylightFactor(float timeNormalized)
        {
            return Mathf.Max(0f, Mathf.Sin((timeNormalized - 0.25f) * Mathf.PI * 2f));
        }

        private static float EvaluateTwilightFactor(float timeNormalized)
        {
            var hours = Mathf.Repeat(timeNormalized, 1f) * 24f;
            var dawnFactor = Mathf.Clamp01(1f - (Mathf.Abs(hours - 6.5f) / 2.5f));
            var duskFactor = Mathf.Clamp01(1f - (Mathf.Abs(hours - 19f) / 2.5f));
            return Mathf.Max(dawnFactor, duskFactor);
        }

        private static Color EvaluateAtmosphereColor(Color dayColor, Color twilightColor, Color nightColor, float daylightFactor, float twilightFactor)
        {
            var baseColor = Color.Lerp(nightColor, dayColor, daylightFactor);
            return Color.Lerp(baseColor, twilightColor, twilightFactor);
        }

        private static DayNightPhase EvaluatePhase(float timeNormalized)
        {
            var hours = Mathf.Repeat(timeNormalized, 1f) * 24f;

            if (hours >= 6f && hours < 8.5f)
            {
                return DayNightPhase.Dawn;
            }

            if (hours >= 8.5f && hours < 18f)
            {
                return DayNightPhase.Day;
            }

            if (hours >= 18f && hours < 20.5f)
            {
                return DayNightPhase.Dusk;
            }

            return DayNightPhase.Night;
        }

        private static string FormatTime(float timeNormalized)
        {
            var totalMinutes = Mathf.FloorToInt(Mathf.Repeat(timeNormalized, 1f) * 24f * 60f);
            var hours = totalMinutes / 60;
            var minutes = totalMinutes % 60;
            return $"{hours:00}:{minutes:00}";
        }

        private static string GetPhaseDisplayName(DayNightPhase phase)
        {
            switch (phase)
            {
                case DayNightPhase.Dawn:
                    return "Dawn";
                case DayNightPhase.Day:
                    return "Day";
                case DayNightPhase.Dusk:
                    return "Dusk";
                default:
                    return "Night";
            }
        }

        private static string GetLocalizedPhaseDisplayName(DayNightPhase phase)
        {
            switch (phase)
            {
                case DayNightPhase.Dawn:
                    return "Рассвет";
                case DayNightPhase.Day:
                    return "День";
                case DayNightPhase.Dusk:
                    return "Сумерки";
                default:
                    return "Ночь";
            }
        }

        private static bool IsTextInputFocused()
        {
            var selectedObject = EventSystem.current?.currentSelectedGameObject;
            if (selectedObject == null)
            {
                return false;
            }

            return selectedObject.GetComponentInParent<TMP_InputField>() != null;
        }
    }
}
