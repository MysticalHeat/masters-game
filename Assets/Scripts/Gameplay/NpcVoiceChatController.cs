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
            if (chatGameManager == null || !chatGameManager.ChatOpen)
            {
                return;
            }

            if (isSpeaking)
            {
                StopNpcSpeech();
                return;
            }

            if (microphoneRecorder == null || speechToTextService == null || textToSpeechService == null)
            {
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
            microphoneRecorder.BeginRecording();
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
                    chatWindow?.SetVoiceState(false, false, false);
                    return;
                }

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
    }
}
