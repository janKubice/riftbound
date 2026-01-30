using UnityEngine;
using System.Collections.Generic;

[CreateAssetMenu(fileName = "CharacterDB", menuName = "Riftbound/Character Database")]
public class CharacterDatabase : ScriptableObject
{
    public List<CharacterData> Characters;

    public CharacterData GetCharacter(int id)
    {
        if (id < 0 || id >= Characters.Count) return null;
        return Characters[id];
    }
}