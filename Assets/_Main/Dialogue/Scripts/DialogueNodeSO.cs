using NaughtyAttributes;
using UnityEngine;

namespace Dialogue
{
    [CreateAssetMenu(fileName = "DialogueNodeSO", menuName = "Scriptable Objects/DialogueNodeSO")]
    public class DialogueNodeSO : ScriptableObject
    {
        [field: SerializeField] public string Text { get; private set; }
        [field: SerializeField] public DialogueNodeCondition Condition { get; private set; }
        [field: Expandable] [field: SerializeField] public DialogueNodeSO[] NextNodes { get; private set; }
    }
}