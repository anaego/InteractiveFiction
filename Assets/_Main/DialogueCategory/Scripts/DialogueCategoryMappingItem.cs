using System;
using DialogueMenu;
using UnityEngine;

namespace DialogueCategory
{
    [Serializable]
    public struct DialogueCategoryMappingItem
    {
        [field: SerializeField] public DialogueCategorySO Data { get; private set; }
        [field: SerializeField] public DialogueCategoryView View { get; private set; }
    }
}