using NaughtyAttributes;
using UnityEngine;

namespace Dialogue
{
    [CreateAssetMenu(fileName = "DialogueSO", menuName = "Scriptable Objects/DialogueSO")]
    public class DialogueSO : ScriptableObject
    {
        [field: Expandable] [field: SerializeField] public DialogueNodeSO StartNode { get; private set; }
        [field: Expandable] [field: SerializeField] public DialogueNodeSO[] Nodes { get; private set; }
    }
}