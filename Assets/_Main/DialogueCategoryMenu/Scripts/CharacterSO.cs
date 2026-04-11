using UnityEngine;

namespace Dialogue
{
    [CreateAssetMenu(fileName = "CharacterSO", menuName = "Scriptable Objects/CharacterSO")]
    public class CharacterSO : ScriptableObject
    {
        [field: SerializeField] public string Name { get; private set; }
    }
}
