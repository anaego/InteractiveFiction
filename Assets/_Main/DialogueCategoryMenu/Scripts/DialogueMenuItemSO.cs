using UnityEngine;

namespace DialogueMenu
{
    [CreateAssetMenu(fileName = "DialogueMenuItemSO", menuName = "Scriptable Objects/DialogueMenuItemSO")]
    public class DialogueMenuItemSO : ScriptableObject
    {
        [field: SerializeField] public DialogueCategorySO[] DialogueCategories { get; private set; }
        [field: SerializeField] public DialogueSO[] Dialogues { get; private set; }
    }
}