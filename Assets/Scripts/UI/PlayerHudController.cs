using MastersGame.Gameplay;
using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem;
using UnityEngine.UI;

namespace MastersGame.UI
{
    public class PlayerHudController : MonoBehaviour
    {
        [Header("References")]
        [SerializeField] private PlayerHealth trackedHealth;
        [SerializeField] private DayNightCycle trackedDayNightCycle;
        [SerializeField] private TextMeshProUGUI healthValueLabel;
        [SerializeField] private TextMeshProUGUI timeOfDayValueLabel;
        [SerializeField] private Image healthFillImage;
        [SerializeField] private Button testDamageButton;
        [SerializeField] private Button toggleDayNightButton;

        [Header("Display")]
        [SerializeField] private Color lowHealthColor = new Color(0.78f, 0.18f, 0.21f, 1f);
        [SerializeField] private Color highHealthColor = new Color(0.18f, 0.72f, 0.32f, 1f);

        [Header("Debug")]
        [SerializeField] private bool enableTestControls = true;
        [SerializeField] [Min(1f)] private float testDamageAmount = 10f;
        [SerializeField] private Key testDamageHotkey = Key.H;

        public void Configure(TextMeshProUGUI valueLabel, Image fillImage, Button damageButton, TextMeshProUGUI timeValueLabel, Button dayNightButton)
        {
            healthValueLabel = valueLabel;
            healthFillImage = fillImage;
            timeOfDayValueLabel = timeValueLabel;
            SetTestDamageButton(damageButton);
            SetToggleDayNightButton(dayNightButton);
            Refresh();
        }

        public void Bind(PlayerHealth health)
        {
            if (trackedHealth == health)
            {
                Refresh();
                return;
            }

            UnsubscribeFromHealth();
            trackedHealth = health;
            SubscribeToHealth();
            Refresh();
        }

        private void Awake()
        {
            SubscribeToButtons();
        }

        private void OnEnable()
        {
            SubscribeToHealth();
            SubscribeToDayNightCycle();
            SubscribeToButtons();
            Refresh();
        }

        private void Update()
        {
            if (!enableTestControls || trackedHealth == null || testDamageHotkey == Key.None || Keyboard.current == null)
            {
                return;
            }

            if (IsTextInputFocused())
            {
                return;
            }

            var hotkey = Keyboard.current[testDamageHotkey];
            if (hotkey != null && hotkey.wasPressedThisFrame)
            {
                ApplyTestDamage();
            }
        }

        private void OnDisable()
        {
            UnsubscribeFromHealth();
            UnsubscribeFromDayNightCycle();
            UnsubscribeFromButtons();
        }

        private void OnDestroy()
        {
            UnsubscribeFromHealth();
            UnsubscribeFromDayNightCycle();
            UnsubscribeFromButtons();
        }

        private void OnValidate()
        {
            testDamageAmount = Mathf.Max(1f, testDamageAmount);
        }

        private void ApplyTestDamage()
        {
            if (!enableTestControls || trackedHealth == null)
            {
                return;
            }

            trackedHealth.ApplyDamage(testDamageAmount);
        }

        public void BindTimeOfDay(DayNightCycle cycle)
        {
            if (trackedDayNightCycle == cycle)
            {
                Refresh();
                return;
            }

            UnsubscribeFromDayNightCycle();
            trackedDayNightCycle = cycle;
            SubscribeToDayNightCycle();
            Refresh();
        }

        private void HandleHealthChanged(PlayerHealth health)
        {
            if (health != trackedHealth)
            {
                return;
            }

            Refresh();
        }

        private void HandleDayNightCycleChanged(DayNightCycle cycle)
        {
            if (cycle != trackedDayNightCycle)
            {
                return;
            }

            Refresh();
        }

        private void Refresh()
        {
            RefreshHealth();
            RefreshTimeOfDay();
        }

        private void RefreshHealth()
        {
            if (trackedHealth == null)
            {
                if (healthValueLabel != null)
                {
                    healthValueLabel.text = "--";
                }

                if (healthFillImage != null)
                {
                    healthFillImage.fillAmount = 0f;
                    healthFillImage.color = lowHealthColor;
                }

                if (testDamageButton != null)
                {
                    testDamageButton.interactable = false;
                }

                return;
            }

            var currentHealth = Mathf.RoundToInt(trackedHealth.CurrentHealth);
            var maxHealth = Mathf.RoundToInt(trackedHealth.MaxHealth);
            var normalizedHealth = trackedHealth.NormalizedHealth;

            if (healthValueLabel != null)
            {
                healthValueLabel.text = $"{currentHealth} / {maxHealth}";
            }

            if (healthFillImage != null)
            {
                healthFillImage.fillAmount = normalizedHealth;
                healthFillImage.color = Color.Lerp(lowHealthColor, highHealthColor, normalizedHealth);
            }

            if (testDamageButton != null)
            {
                testDamageButton.interactable = enableTestControls && trackedHealth.IsAlive;
            }
        }

        private void RefreshTimeOfDay()
        {
            if (trackedDayNightCycle == null)
            {
                if (timeOfDayValueLabel != null)
                {
                    timeOfDayValueLabel.text = "--";
                }

                if (toggleDayNightButton != null)
                {
                    toggleDayNightButton.interactable = false;
                }

                return;
            }

            if (timeOfDayValueLabel != null)
            {
                timeOfDayValueLabel.text = $"{trackedDayNightCycle.FormattedTime} • {trackedDayNightCycle.PhaseDisplayName}";
            }

            if (toggleDayNightButton != null)
            {
                toggleDayNightButton.interactable = true;
            }
        }

        private void ToggleDayNight()
        {
            trackedDayNightCycle?.ToggleDayNight();
        }

        private void SetTestDamageButton(Button button)
        {
            if (testDamageButton == button)
            {
                return;
            }

            UnsubscribeFromTestDamageButton();
            testDamageButton = button;
            SubscribeToTestDamageButton();
        }

        private void SetToggleDayNightButton(Button button)
        {
            if (toggleDayNightButton == button)
            {
                return;
            }

            UnsubscribeFromToggleDayNightButton();
            toggleDayNightButton = button;
            SubscribeToToggleDayNightButton();
        }

        private void SubscribeToHealth()
        {
            if (trackedHealth == null)
            {
                return;
            }

            trackedHealth.HealthChanged -= HandleHealthChanged;
            trackedHealth.HealthChanged += HandleHealthChanged;
        }

        private void UnsubscribeFromHealth()
        {
            if (trackedHealth == null)
            {
                return;
            }

            trackedHealth.HealthChanged -= HandleHealthChanged;
        }

        private void SubscribeToDayNightCycle()
        {
            if (trackedDayNightCycle == null)
            {
                return;
            }

            trackedDayNightCycle.StateChanged -= HandleDayNightCycleChanged;
            trackedDayNightCycle.StateChanged += HandleDayNightCycleChanged;
        }

        private void UnsubscribeFromDayNightCycle()
        {
            if (trackedDayNightCycle == null)
            {
                return;
            }

            trackedDayNightCycle.StateChanged -= HandleDayNightCycleChanged;
        }

        private void SubscribeToButtons()
        {
            SubscribeToTestDamageButton();
            SubscribeToToggleDayNightButton();
        }

        private void UnsubscribeFromButtons()
        {
            UnsubscribeFromTestDamageButton();
            UnsubscribeFromToggleDayNightButton();
        }

        private void SubscribeToTestDamageButton()
        {
            if (testDamageButton == null)
            {
                return;
            }

            testDamageButton.onClick.RemoveListener(ApplyTestDamage);
            testDamageButton.onClick.AddListener(ApplyTestDamage);
        }

        private void UnsubscribeFromTestDamageButton()
        {
            if (testDamageButton == null)
            {
                return;
            }

            testDamageButton.onClick.RemoveListener(ApplyTestDamage);
        }

        private void SubscribeToToggleDayNightButton()
        {
            if (toggleDayNightButton == null)
            {
                return;
            }

            toggleDayNightButton.onClick.RemoveListener(ToggleDayNight);
            toggleDayNightButton.onClick.AddListener(ToggleDayNight);
        }

        private void UnsubscribeFromToggleDayNightButton()
        {
            if (toggleDayNightButton == null)
            {
                return;
            }

            toggleDayNightButton.onClick.RemoveListener(ToggleDayNight);
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
