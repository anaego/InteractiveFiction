using UnityEngine;

namespace Dialogue
{
    [CreateAssetMenu(fileName = "DialogueNodeSO", menuName = "Scriptable Objects/DialogueNodeSO")]
    public class DialogueNodeSO : ScriptableObject
    {
        [field: SerializeField] public string Text { get; private set; }
    }
}