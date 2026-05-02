using LightSide;
using UnityEngine;

namespace Dialogue
{
    public class DialogueView : MonoBehaviour
    {
        [field: SerializeField] public GameObject Container { get; private set; }
        // TODO: private?
        [field: SerializeField] public UniText MainText { get; private set; }
        [field: SerializeField] public GameObject TextOptionsContainer { get; private set; }

        public void Setup(DialogueSO data)
        {
            MainText.Text = data.StartNode.Text;
        }
    }
}