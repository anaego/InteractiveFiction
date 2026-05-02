using System.Collections.Generic;
using Dialogue;
using UnityEngine;

namespace DialogueMenu
{
    public class DialogueMenuView : MonoBehaviour
    {
        [field: SerializeField] public GameObject Container { get; private set; }

        [SerializeField] private DialogueMenuItemView _dialogueMenuItemPrefab;
        [SerializeField] private Transform _dialogueMenuItemContainer;
        
        public List<DialogueMenuMapItem> SetupDialogueList(DialogueMenuItemSO[] dialogues)
        {
            var dialogueMenuItemViewDataMap = new List<DialogueMenuMapItem>();
            foreach (var dialogue in dialogues)
            {
                var dialogueMenuItem = Instantiate(_dialogueMenuItemPrefab, _dialogueMenuItemContainer);
                dialogueMenuItem.Setup(dialogue);
                dialogueMenuItemViewDataMap.Add(new DialogueMenuMapItem(dialogue, dialogueMenuItem));
            }
            return dialogueMenuItemViewDataMap;
        }
    }
}
