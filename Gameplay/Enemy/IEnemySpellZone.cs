using UnityEngine;

public interface IEnemySpellZone
{
    void InitializeFromSpell(
        EnemySpellDefinition spell,
        ulong sourceClientId,
        Vector3 castDirection
    );
}