using UnityEngine;
using Unity.Netcode;
using TMPro;
using System.Collections;

public class DamageNumberManager : NetworkBehaviour
{
    public static DamageNumberManager Instance { get; private set; }

    [Header("Settings")]
    [SerializeField] private GameObject _textPrefab; // Prefab s TextMeshPro komponentou
    [SerializeField] private float _floatSpeed = 2f;
    [SerializeField] private float _fadeDuration = 1f;
    [SerializeField] private Vector3 _offset = new Vector3(0, 2, 0);

    private void Awake()
    {
        if (Instance != null && Instance != this)
        {
            Destroy(gameObject);
            return;
        }
        Instance = this;
    }

    /// <summary>
    /// Volá Server (např. EnemyHealth), aby zobrazil číslo všem klientům.
    /// </summary>
    public void SpawnDamageNumber(Vector3 position, int amount, bool isCrit)
    {
        if (!IsServer) return;
        SpawnDamageNumberClientRpc(position, amount, isCrit);
    }

    [ClientRpc]
    private void SpawnDamageNumberClientRpc(Vector3 position, int amount, bool isCrit)
    {
        // Optimalizace: Zde by měl být Object Pool, pro jednoduchost používám Instantiate/Destroy
        GameObject popup = Instantiate(_textPrefab, position + _offset + Random.insideUnitSphere * 0.5f, Quaternion.identity);
        
        // Nastavení textu
        var tmpro = popup.GetComponent<TextMeshPro>();
        if (tmpro != null)
        {
            tmpro.text = amount.ToString();
            tmpro.color = isCrit ? Color.yellow : Color.white;
            tmpro.fontSize = isCrit ? 6 : 4;
        }

        // Spuštění animace pohybu a zmizení
        StartCoroutine(AnimatePopup(popup, tmpro));
    }

    private IEnumerator AnimatePopup(GameObject obj, TextMeshPro tmpro)
    {
        float timer = 0;
        Vector3 startPos = obj.transform.position;
        Color startColor = tmpro.color;

        while (timer < _fadeDuration)
        {
            if (obj == null) yield break;

            timer += Time.deltaTime;
            float progress = timer / _fadeDuration;

            // Pohyb nahoru
            obj.transform.position = startPos + Vector3.up * (_floatSpeed * progress);

            // Fade out
            tmpro.color = new Color(startColor.r, startColor.g, startColor.b, 1 - progress);

            // Billboarding (otáčení na kameru)
            if (Camera.main != null)
            {
                obj.transform.rotation = Quaternion.LookRotation(obj.transform.position - Camera.main.transform.position);
            }

            yield return null;
        }

        Destroy(obj);
    }
}