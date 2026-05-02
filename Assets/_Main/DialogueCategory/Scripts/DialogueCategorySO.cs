using DialogueMenu;
using NaughtyAttributes;
using UnityEngine;

namespace DialogueCategory
{
    [CreateAssetMenu(fileName = "DialogueCategorySO", menuName = "Scriptable Objects/DialogueCategorySO")]
    public class DialogueCategorySO : ScriptableObject
    {
        [field: SerializeField] public DialogueCategorySO[] ChildDialogueCategories { get; private set; }
        [field: Expandable] [field: SerializeField] public DialogueMenuItemSO[] DialogueMenuItems { get; private set; }
    }
}