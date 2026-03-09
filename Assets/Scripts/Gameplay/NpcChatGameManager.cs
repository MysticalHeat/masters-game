using System;
using System.Collections.Generic;
using System.Threading;
using MastersGame.AI;
using MastersGame.UI;
using UnityEngine;

namespace MastersGame.Gameplay
{
    public class NpcChatGameManager : MonoBehaviour
    {
        [SerializeField] private PlayerController3D playerController;
        [SerializeField] private ChatWindowController chatWindow;
        [SerializeField] private SentisLocalLanguageModel sentisModel;
        [SerializeField] private StubLocalLanguageModel stubModel;
        [SerializeField] private int maxVisibleHistory = 12;
        [SerializeField] private bool enableDebugLogging = true;

        private readonly List<ChatMessage> history = new();

        private CancellationTokenSource generationCancellation;
        private NpcChatTarget currentNpc;
        private bool isBusy;

        public bool ChatOpen { get; private set; }

        public void Configure(PlayerController3D controller, ChatWindowController window, SentisLocalLanguageModel sentis, StubLocalLanguageModel stub)
        {
            playerController = controller;
            chatWindow = window;
            sentisModel = sentis;
            stubModel = stub;
        }

        private void Awake()
        {
            if (chatWindow != null)
            {
                chatWindow.SendRequested += HandleSendRequested;
                chatWindow.CloseRequested += CloseConversation;
            }
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

            AddPlayerMessage(trimmedMessage);

            isBusy = true;
            generationCancellation = new CancellationTokenSource();
            var languageModel = GetModel();
            LogDebug($"Sending message to {currentNpc.NpcName} via {languageModel.DisplayName}: {trimmedMessage}");
            chatWindow.SetBusy(true, $"Generating via {languageModel.DisplayName}...");

            try
            {
                var reply = await languageModel.GenerateReplyAsync(currentNpc.BuildRequest(new List<ChatMessage>(history), trimmedMessage), generationCancellation.Token);

                if (!ChatOpen || currentNpc == null)
                {
                    return;
                }

                LogDebug($"Reply from {languageModel.DisplayName}: {reply}");
                AddNpcMessage(reply);
                chatWindow.SetBusy(false, languageModel.StatusSummary);
                chatWindow.FocusInput();
            }
            catch (OperationCanceledException)
            {
                if (ChatOpen)
                {
                    chatWindow.SetBusy(false, "Generation cancelled.");
                }
            }
            catch (Exception exception)
            {
                Debug.LogException(exception);
                if (ChatOpen)
                {
                    AddNpcMessage($"Ошибка генерации: {exception.Message}");
                    chatWindow.SetBusy(false, "Generation failed.");
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
            if (sentisModel != null && sentisModel.IsConfigured)
            {
                return sentisModel;
            }

            return stubModel;
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

        private void LogDebug(string message)
        {
            if (!enableDebugLogging)
            {
                return;
            }

            Debug.Log($"[NpcChatGameManager] {message}");
        }
    }
}