using UnityEngine;
using Unity.Netcode;

public class FireworksLauncher : NetworkBehaviour, IInteractable
{
    [SerializeField] private GameObject _rocketPrefab; // Jednoduchý objekt s TrailRendererem
    [SerializeField] private GameObject _explosionVFXPrefab; // Particle System exploze
    [SerializeField] private Transform _launchPoint;
    [SerializeField] private float _flySpeed = 20f;
    [SerializeField] private float _fuseTime = 2f; // Jak dlouho letí

    private NetworkVariable<bool> _isLaunching = new NetworkVariable<bool>(false);

    public string InteractionPrompt => "E - Odpálit ohňostroj";

    public void Interact(NetworkObject interactor)
    {
        if (!IsServer || _isLaunching.Value) return;
        
        StartCoroutine(LaunchRoutine());
    }

    private System.Collections.IEnumerator LaunchRoutine()
    {
        _isLaunching.Value = true;
        
        // 1. Spawn Rakety (NetworkObject, aby ji viděli všichni letět)
        GameObject rocket = Instantiate(_rocketPrefab, _launchPoint.position, Quaternion.identity);
        rocket.GetComponent<NetworkObject>().Spawn(true);

        // Jednoduchý pohyb rakety nahoru (Můžeš použít RB, ale Translate je jistota pro vizuál)
        float timer = 0;
        while (timer < _fuseTime)
        {
            if (rocket == null) break;
            rocket.transform.Translate(Vector3.up * _flySpeed * Time.deltaTime);
            timer += Time.deltaTime;
            yield return null;
        }

        // 2. Exploze
        if (rocket != null)
        {
            Vector3 explosionPos = rocket.transform.position;
            // Spawn VFX (Jako NetworkObject, který se sám zničí, nebo přes RPC)
            SpawnExplosionClientRpc(explosionPos);
            
            // Zničení rakety
            rocket.GetComponent<NetworkObject>().Despawn();
            Destroy(rocket);
        }

        // Cooldown
        yield return new WaitForSeconds(5f);
        _isLaunching.Value = false;
    }

    [ClientRpc]
    private void SpawnExplosionClientRpc(Vector3 pos)
    {
        Instantiate(_explosionVFXPrefab, pos, Quaternion.identity);
        // + Zvuk exploze
    }
}