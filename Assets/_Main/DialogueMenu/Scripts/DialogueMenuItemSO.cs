using Dialogue;
using UnityEngine;

namespace DialogueMenu
{
    [CreateAssetMenu(fileName = "DialogueMenuItemSO", menuName = "Scriptable Objects/DialogueMenuItemSO")]
    public class DialogueMenuItemSO : ScriptableObject
    {
        [field: SerializeField] public string Title { get; private set; }
        [field: SerializeField] public string Description { get; private set; }
        [field: SerializeField] public DialogueSO Dialogue { get; private set; }
    }
}