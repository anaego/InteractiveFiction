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
        
        private List<DialogueMenuItemView> _currentMenuItems = new();
        
        public List<DialogueMenuMapItem> SetupDialogueList(DialogueMenuItemSO[] dialogues)
        {
            var dialogueMenuItemViewDataMap = new List<DialogueMenuMapItem>();
            foreach (var dialogue in dialogues)
            {
                var dialogueMenuItem = Instantiate(_dialogueMenuItemPrefab, _dialogueMenuItemContainer);
                dialogueMenuItem.Setup(dialogue);
                dialogueMenuItemViewDataMap.Add(new DialogueMenuMapItem(dialogue, dialogueMenuItem));
                _currentMenuItems.Add(dialogueMenuItem);
            }
            return dialogueMenuItemViewDataMap;
        }

        public void Reset()
        {
            foreach (var dialogueMenuItemView in _currentMenuItems)
            {
                Destroy(dialogueMenuItemView.gameObject);
            }
            _currentMenuItems.Clear();
        }
    }
}
