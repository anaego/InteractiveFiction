using Dialogue;

namespace DialogueMenu
{
    public struct DialogueMenuMapItem
    {
        public DialogueMenuItemSO Data { get; private set; }
        public DialogueMenuItemView View { get; private set; }

        public DialogueMenuMapItem(DialogueMenuItemSO dialogue, DialogueMenuItemView dialogueMenuItem)
        {
            Data = dialogue;
            View = dialogueMenuItem;
        }
    }
}