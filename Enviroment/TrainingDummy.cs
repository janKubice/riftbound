using UnityEngine;
using Unity.Netcode;
using System.Collections;

public class TrainingDummy : NetworkBehaviour
{
    [Header("Nastavení")]
    [SerializeField] private float _wobbleForce = 5f;
    [SerializeField] private float _resetSpeed = 2f;
    [SerializeField] private Rigidbody _rb; // Horní část panáka (musí mít HingeJoint k podstavci)
    
    [Header("Audio")]
    [SerializeField] private NetworkedAudioSource _netAudio;
    [SerializeField] private int _hitSoundIndex = 0;

    // Pokud nemáš systém pro FloatingText, zatím to necháme prázdné nebo jen Log
    // [SerializeField] private GameObject _damageTextPrefab; 

    private Quaternion _initialRot;

    private void Awake()
    {
        if (_rb) _initialRot = _rb.transform.localRotation;
    }

    // Tuto metodu volej z MeleeAttackLogic / Projectile místo TakeDamage
    public void TakeHit(int damage, Vector3 hitDirection)
    {
        if (!IsServer) return;

        // 1. Zvuk
        if (_netAudio) _netAudio.PlayOneShotNetworked(_hitSoundIndex);

        // 2. Fyzikální reakce (Server ovládá Rigidbody, pokud je NetworkRigidbody, jinak RPC)
        ApplyForceClientRpc(hitDirection * _wobbleForce);

        // 3. Informace o poškození (Floating Text)
        ShowDamageClientRpc(damage);
    }

    [ClientRpc]
    private void ApplyForceClientRpc(Vector3 force)
    {
        if (_rb)
        {
            _rb.AddForce(force, ForceMode.Impulse);
        }
    }

    [ClientRpc]
    private void ShowDamageClientRpc(int damage)
    {
        // Zde bys instancoval FloatingText prefab
        Debug.Log($"<color=yellow>Dummy hit for: {damage}</color>");
        
        // Příklad (pokud bys měl prefab):
        // var textObj = Instantiate(_damageTextPrefab, transform.position + Vector3.up * 2, Quaternion.identity);
        // textObj.GetComponent<TextMeshPro>().text = damage.ToString();
    }

    private void FixedUpdate()
    {
        // Pokud používáš čistě fyzikální HingeJoint, toto není třeba.
        // Pokud to "fejkuješ" přes rotaci, zde by se vracel do původní polohy:
        /*
        if (_rb)
        {
            _rb.transform.localRotation = Quaternion.Lerp(_rb.transform.localRotation, _initialRot, Time.deltaTime * _resetSpeed);
        }
        */
    }
}