using System;
using System.Threading;
using System.Threading.Tasks;
using MastersGame.UI;
using MastersGame.Voice;
using UnityEngine;

namespace MastersGame.Gameplay
{
    public class NpcVoiceChatController : MonoBehaviour
    {
        [SerializeField] private NpcChatGameManager chatGameManager;
        [SerializeField] private ChatWindowController chatWindow;
        [SerializeField] private MicrophoneRecorder microphoneRecorder;
        [SerializeField] private WhisperHttpSpeechToText speechToTextService;
        [SerializeField] private HttpTextToSpeechService textToSpeechService;
        [SerializeField] private bool enableDebugLogging = true;

        private CancellationTokenSource voiceCancellation;
        private CancellationTokenSource speechCancellation;
        private bool isProcessing;
        private bool isSpeaking;

        public void Configure(
            NpcChatGameManager manager,
            ChatWindowController window,
            MicrophoneRecorder recorder,
            WhisperHttpSpeechToText stt,
            HttpTextToSpeechService tts)
        {
            chatGameManager = manager;
            chatWindow = window;
            microphoneRecorder = recorder;
            speechToTextService = stt;
            textToSpeechService = tts;
        }

        private void Awake()
        {
            ResolveReferences();

            if (chatWindow != null)
            {
                chatWindow.VoiceInputRequested += ToggleVoiceInput;
            }

            if (chatGameManager != null)
            {
                chatGameManager.NpcReplyReady += HandleNpcReplyReady;
                chatGameManager.ConversationClosed += HandleConversationClosed;
            }
        }

        private void OnDestroy()
        {
            if (chatWindow != null)
            {
                chatWindow.VoiceInputRequested -= ToggleVoiceInput;
            }

            if (chatGameManager != null)
            {
                chatGameManager.NpcReplyReady -= HandleNpcReplyReady;
                chatGameManager.ConversationClosed -= HandleConversationClosed;
            }

            CancelVoiceFlow();
            CancelSpeechFlow();
        }

        private void ToggleVoiceInput()
        {
            ResolveReferences();

            if (chatGameManager == null || !chatGameManager.ChatOpen)
            {
                LogDebug("Mic ignored: chat is not open.");
                return;
            }

            if (isSpeaking)
            {
                StopNpcSpeech();
                return;
            }

            if (microphoneRecorder == null || speechToTextService == null)
            {
                LogDebug("Mic ignored: voice input components are not configured.");
                chatWindow?.SetBusy(false, "Голосовой ввод не настроен.");
                return;
            }

            if (!speechToTextService.IsConfigured)
            {
                LogDebug("Mic ignored: speech-to-text backend is not configured.");
                chatWindow?.SetBusy(false, speechToTextService.StatusSummary);
                return;
            }

            if (microphoneRecorder.IsRecording)
            {
                _ = FinishRecordingAndSubmitAsync();
                return;
            }

            voiceCancellation?.Cancel();
            voiceCancellation?.Dispose();
            voiceCancellation = new CancellationTokenSource();
            if (!microphoneRecorder.BeginRecording())
            {
                LogDebug("Mic ignored: no microphone device or recording failed.");
                chatWindow?.SetBusy(false, "Микрофон не найден или недоступен.");
                return;
            }

            LogDebug("Voice recording started.");
            chatWindow?.SetVoiceState(true, false, false);
        }

        private async Task FinishRecordingAndSubmitAsync()
        {
            if (isProcessing || chatGameManager == null)
            {
                return;
            }

            isProcessing = true;
            byte[] audioData;
            try
            {
                audioData = microphoneRecorder.EndRecording(out _);
                LogDebug($"Voice recording stopped. Bytes={audioData?.Length ?? 0}");
            }
            catch (Exception exception)
            {
                Debug.LogException(exception);
                isProcessing = false;
                chatWindow?.SetVoiceState(false, false, false);
                return;
            }

            chatWindow?.SetVoiceState(false, true, false);

            try
            {
                var transcript = await speechToTextService.TranscribeAsync(audioData, "player_voice.wav", voiceCancellation?.Token ?? CancellationToken.None);
                if (string.IsNullOrWhiteSpace(transcript))
                {
                    LogDebug($"Speech-to-text returned empty transcript. Status='{speechToTextService.StatusSummary}'");
                    chatWindow?.SetBusy(false, speechToTextService.StatusSummary);
                    chatWindow?.SetVoiceState(false, false, false);
                    return;
                }

                LogDebug($"Speech-to-text transcript: {transcript}");
                await chatGameManager.SubmitPlayerMessageAsync(transcript, voiceCancellation?.Token ?? CancellationToken.None);
            }
            catch (OperationCanceledException)
            {
            }
            catch (Exception exception)
            {
                Debug.LogException(exception);
            }
            finally
            {
                isProcessing = false;
                if (!isSpeaking)
                {
                    chatWindow?.SetVoiceState(false, false, false);
                }

                CancelVoiceFlow();
            }
        }

        private async void HandleNpcReplyReady(NpcChatTarget npc, string replyText)
        {
            if (npc == null || string.IsNullOrWhiteSpace(replyText) || textToSpeechService == null || !chatGameManager.ChatOpen)
            {
                return;
            }

            var audioSource = npc.VoiceAudioSource;
            if (audioSource == null)
            {
                return;
            }

            CancelSpeechPlayback(audioSource);

            isSpeaking = true;
            chatWindow?.SetVoiceState(false, false, true);

            speechCancellation?.Cancel();
            speechCancellation?.Dispose();
            speechCancellation = new CancellationTokenSource();

            try
            {
                var clip = await textToSpeechService.SynthesizeAsync(replyText, npc.TtsVoiceId, speechCancellation.Token);
                if (clip == null)
                {
                    return;
                }

                audioSource.clip = clip;
                audioSource.Play();
                while (audioSource.isPlaying)
                {
                    await Task.Yield();
                }
            }
            catch (OperationCanceledException)
            {
            }
            catch (Exception exception)
            {
                Debug.LogException(exception);
            }
            finally
            {
                isSpeaking = false;
                if (!isProcessing)
                {
                    chatWindow?.SetVoiceState(false, false, false);
                }

                CancelSpeechFlow();
            }
        }

        private void HandleConversationClosed()
        {
            CancelVoiceFlow();
            CancelSpeechFlow();
            StopNpcSpeech();
            chatWindow?.SetVoiceState(false, false, false);
        }

        private void CancelVoiceFlow()
        {
            if (microphoneRecorder != null)
            {
                microphoneRecorder.CancelRecording();
            }

            voiceCancellation?.Cancel();
            voiceCancellation?.Dispose();
            voiceCancellation = null;
            isProcessing = false;
        }

        private void CancelSpeechFlow()
        {
            speechCancellation?.Cancel();
            speechCancellation?.Dispose();
            speechCancellation = null;
            isSpeaking = false;
        }

        private void StopNpcSpeech()
        {
            if (chatGameManager?.CurrentNpc?.VoiceAudioSource != null)
            {
                chatGameManager.CurrentNpc.VoiceAudioSource.Stop();
            }

            isSpeaking = false;
            chatWindow?.SetVoiceState(false, false, false);
        }

        private static void CancelSpeechPlayback(AudioSource audioSource)
        {
            if (audioSource == null)
            {
                return;
            }

            if (audioSource.isPlaying)
            {
                audioSource.Stop();
            }
        }

        private void LogDebug(string message)
        {
            if (!enableDebugLogging)
            {
                return;
            }

            Debug.Log($"[NpcVoiceChatController] {message}");
        }

        private void ResolveReferences()
        {
            if (microphoneRecorder == null)
            {
                microphoneRecorder = GetComponent<MicrophoneRecorder>();
            }

            if (microphoneRecorder == null)
            {
                microphoneRecorder = gameObject.AddComponent<MicrophoneRecorder>();
                LogDebug("Added missing MicrophoneRecorder at runtime.");
            }

            if (speechToTextService == null)
            {
                speechToTextService = GetComponent<WhisperHttpSpeechToText>();
            }

            if (speechToTextService == null)
            {
                speechToTextService = gameObject.AddComponent<WhisperHttpSpeechToText>();
                LogDebug("Added missing WhisperHttpSpeechToText at runtime.");
            }

            if (textToSpeechService == null)
            {
                textToSpeechService = GetComponent<HttpTextToSpeechService>();
            }

            if (textToSpeechService == null)
            {
                textToSpeechService = gameObject.AddComponent<HttpTextToSpeechService>();
                LogDebug("Added missing HttpTextToSpeechService at runtime.");
            }

            if (chatGameManager == null)
            {
                chatGameManager = GetComponent<NpcChatGameManager>();
            }
        }
    }
}
