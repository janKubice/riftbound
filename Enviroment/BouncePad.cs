using UnityEngine;
using Unity.Netcode;

public class BouncePad : MonoBehaviour
{
    [SerializeField] private float _bounceForce = 20f;

    private void OnTriggerEnter(Collider other)
    {
        // Hledáme PlayerController na objektu, který do nás vstoupil
        if (other.TryGetComponent(out PlayerController player))
        {
            // Voláme novou metodu
            // (Zavolá se to jen u lokálního hráče, protože PlayerController má kontrolu IsOwner)
            player.ApplyVerticalImpulse(_bounceForce);
        }
    }
}