using Dialogue;
using LightSide;
using UnityEngine;
using UnityEngine.UI;

namespace DialogueMenu
{
    public class DialogueMenuItemView : MonoBehaviour
    {
        // TODO: public?
        [field: SerializeField] public UniText Title { get; private set; }
        [field: SerializeField] public UniText Description { get; private set; }
        [field: SerializeField] public Button Button { get; private set; }

        public void Setup(DialogueMenuItemSO dialogueMenuItem)
        {
            Title.Text = dialogueMenuItem.Title;
            Description.Text = dialogueMenuItem.Description;
        }
    }
}
