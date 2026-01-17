using UnityEngine;
using System.Collections.Generic;

[CreateAssetMenu(menuName = "NPC/Dialogue Node")]
public class DialogueNode : ScriptableObject
{
    [TextArea(3, 10)] public string NpcText;
    public List<DialogueOption> Options;
}

[System.Serializable]
public struct DialogueOption
{
    public string ButtonText;
    public DialogueNode NextNode; // Pokud null, dialog končí
    public DialogueAction Action; // Co se stane po kliknutí
}

public enum DialogueAction
{
    None,
    OpenWeaponShop,
    OpenUpgradeShop,
    HealPlayer,
    Exit
}