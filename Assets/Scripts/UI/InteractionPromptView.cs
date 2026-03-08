using UnityEngine;
using UnityEngine.UI;

namespace MastersGame.UI
{
    public class InteractionPromptView : MonoBehaviour
    {
        [SerializeField] private Text label;

        public void Configure(Text promptLabel)
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