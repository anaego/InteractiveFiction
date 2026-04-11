using UnityEngine;

namespace Dialogue
{
    // TODO: do not like a SO but like that inline popup Malvis did and I then copied for al domains
    // or actually just use NaughtyAttributes
    [CreateAssetMenu(fileName = "DialogueNodeSO", menuName = "Scriptable Objects/DialogueNodeSO")]
    public class DialogueNodeSO : ScriptableObject
    {
        [field: SerializeField] public string Text { get; private set; }

    }
}