using System;
using UnityEngine;

namespace MastersGame.Gameplay
{
    public class PlayerHealth : MonoBehaviour
    {
        [SerializeField] [Min(1f)] private float maxHealth = 100f;
        [SerializeField] [Min(0f)] private float startingHealth = 100f;

        private float currentHealth;
        private bool initialized;

        public event Action<PlayerHealth> HealthChanged;

        public float CurrentHealth
        {
            get
            {
                EnsureInitialized();
                return currentHealth;
            }
        }

        public float MaxHealth => Mathf.Max(1f, maxHealth);

        public float NormalizedHealth
        {
            get
            {
                var maxValue = MaxHealth;
                return maxValue <= 0f ? 0f : CurrentHealth / maxValue;
            }
        }

        public bool IsAlive => CurrentHealth > 0f;

        private void Awake()
        {
            EnsureInitialized();
        }

        public void ApplyDamage(float amount)
        {
            if (amount <= 0f)
            {
                return;
            }

            SetCurrentHealth(CurrentHealth - amount);
        }

        public void RestoreHealth(float amount)
        {
            if (amount <= 0f)
            {
                return;
            }

            SetCurrentHealth(CurrentHealth + amount);
        }

        public void RestoreFullHealth()
        {
            SetCurrentHealth(MaxHealth);
        }

        private void OnValidate()
        {
            maxHealth = Mathf.Max(1f, maxHealth);
            startingHealth = Mathf.Clamp(startingHealth, 0f, maxHealth);

            if (Application.isPlaying)
            {
                if (!initialized)
                {
                    return;
                }

                SetCurrentHealth(currentHealth);
                return;
            }

            currentHealth = startingHealth;
        }

        private void EnsureInitialized()
        {
            if (initialized)
            {
                return;
            }

            maxHealth = Mathf.Max(1f, maxHealth);
            startingHealth = Mathf.Clamp(startingHealth, 0f, maxHealth);
            currentHealth = startingHealth;
            initialized = true;
        }

        private void SetCurrentHealth(float value)
        {
            EnsureInitialized();

            var clampedValue = Mathf.Clamp(value, 0f, MaxHealth);
            if (Mathf.Approximately(currentHealth, clampedValue))
            {
                return;
            }

            currentHealth = clampedValue;
            HealthChanged?.Invoke(this);
        }
    }
}
