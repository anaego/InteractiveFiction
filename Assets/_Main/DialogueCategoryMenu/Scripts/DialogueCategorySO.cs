using UnityEngine;

namespace DialogueMenu
{
    [CreateAssetMenu(fileName = "DialogueCategorySO", menuName = "Scriptable Objects/DialogueCategorySO")]
    public class DialogueCategorySO
    {
        [field: SerializeField] public DialogueCategorySO[] ChildDialogueCategories { get; private set; }
        
        [field: SerializeField] public DialogueSO[] Dialogues { get; private set; }
    }
}