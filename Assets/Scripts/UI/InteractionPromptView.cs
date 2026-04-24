using TMPro;
using UnityEngine;

namespace MastersGame.UI
{
    public class InteractionPromptView : MonoBehaviour
    {
        [SerializeField] private TextMeshProUGUI label;

        public void Configure(TextMeshProUGUI promptLabel)
        {
            label = promptLabel;
        }

        public void Show(string message)
        {
            if (label != null)
            {
                label.text = message;
            }

            if (!gameObject.activeSelf)
            {
                gameObject.SetActive(true);
            }
        }

        public void Hide()
        {
            if (gameObject.activeSelf)
            {
                gameObject.SetActive(false);
            }
        }
    }
}
