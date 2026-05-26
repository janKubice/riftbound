using UnityEngine;
using Unity.Netcode;
using TMPro;
using System.Collections;

// Přidán Enum pro lepší rozlišení typů (můžeš snadno přidávat další, např. Poison, Mana)
public enum PopupType
{
    Damage,
    Critical,
    Heal,
    Experience,
    Gold
}

public class DamageNumberManager : NetworkBehaviour
{
    public static DamageNumberManager Instance { get; private set; }

    [Header("Settings")]
    [SerializeField] private GameObject _textPrefab; // Prefab s TextMeshPro komponentou
    [SerializeField] private float _floatSpeed = 2f;
    [SerializeField] private float _fadeDuration = 1f;
    [SerializeField] private Vector3 _offset = new Vector3(0, 2, 0);

    [Header("Colors")]
    [SerializeField] private Color _damageColor = Color.red;
    [SerializeField] private Color _critColor = new Color(1f, 0.6f, 0f); // Oranžovo-žlutá
    [SerializeField] private Color _healColor = Color.green;
    [Header("Loot Colors")]
    [SerializeField] private Color _xpColor = new Color(0.2f, 0.8f, 1f); // Světle modrá
    [SerializeField] private Color _goldColor = new Color(1f, 0.8f, 0f); // Zlatá

    private Camera _mainCamera;

    private void Awake()
    {
        if (Instance != null && Instance != this)
        {
            Destroy(gameObject);
            return;
        }
        Instance = this;
    }

    private void Start()
    {
        // Uložení reference na kameru je mnohem efektivnější, než volat Camera.main v Update/Coroutině
        _mainCamera = Camera.main;
    }

    /// <summary>
    /// Volá Server (např. EnemyHealth nebo PlayerAttributes), aby zobrazil číslo všem klientům.
    /// </summary>
    public void SpawnDamageNumber(Vector3 position, int amount, PopupType type)
    {
        if (!IsServer) return;
        SpawnDamageNumberClientRpc(position, amount, type);
    }

    [ClientRpc]
    private void SpawnDamageNumberClientRpc(Vector3 position, int amount, PopupType type)
    {
        // Větší, ale kontrolovaný náhodný rozptyl, aby se čísla nepřekrývala
        Vector3 randomOffset = new Vector3(Random.Range(-0.6f, 0.6f), Random.Range(-0.2f, 0.2f), Random.Range(-0.6f, 0.6f));
        GameObject popup = Instantiate(_textPrefab, position + _offset + randomOffset, Quaternion.identity);

        var tmpro = popup.GetComponent<TextMeshPro>();
        if (tmpro != null)
        {
            // Nastavení textu (Heal dostane "+" před číslo)
            tmpro.text = type == PopupType.Heal ? $"+{amount}" : amount.ToString();

            // Nastavení barvy a velikosti podle typu
            switch (type)
            {
                case PopupType.Damage:
                    tmpro.color = _damageColor;
                    tmpro.fontSize = 4f;
                    break;
                case PopupType.Critical:
                    tmpro.color = _critColor;
                    tmpro.fontSize = 6f;
                    // Crit můžeme posunout mírně do popředí
                    popup.transform.position += new Vector3(0, 0.5f, 0);
                    break;
                case PopupType.Heal:
                    tmpro.color = _healColor;
                    tmpro.fontSize = 4.5f;
                    break;
            }
        }

        // Spuštění animace
        StartCoroutine(AnimatePopup(popup, tmpro));
    }

    private IEnumerator AnimatePopup(GameObject obj, TextMeshPro tmpro)
    {
        float timer = 0;
        Vector3 startPos = obj.transform.position;
        Color startColor = tmpro.color;
        Vector3 targetScale = obj.transform.localScale;

        // Postava začíná s nulovou velikostí kvůli "pop-up" efektu
        obj.transform.localScale = Vector3.zero;

        while (timer < _fadeDuration)
        {
            if (obj == null) yield break;

            timer += Time.deltaTime;
            float progress = timer / _fadeDuration;

            // 1. POHYB: Nelineární pohyb (na začátku vyletí rychleji, pak zpomaluje - Ease Out)
            float moveProgress = Mathf.Pow(progress, 0.5f);
            obj.transform.position = startPos + Vector3.up * (_floatSpeed * moveProgress);

            // 2. ŠKÁLOVÁNÍ (Pop-up efekt - číslo na moment přeroste svou velikost a pak se usadí)
            if (progress < 0.15f) // Rychlé zvětšení na 120%
            {
                obj.transform.localScale = Vector3.Lerp(Vector3.zero, targetScale * 1.2f, progress / 0.15f);
            }
            else if (progress < 0.3f) // Zmenšení zpět na 100%
            {
                obj.transform.localScale = Vector3.Lerp(targetScale * 1.2f, targetScale, (progress - 0.15f) / 0.15f);
            }

            // 3. FADE OUT: Zmizí až ve druhé polovině animace, aby bylo číslo déle čitelné
            if (progress > 0.5f)
            {
                float fadeProgress = (progress - 0.5f) / 0.5f;
                tmpro.color = new Color(startColor.r, startColor.g, startColor.b, 1 - fadeProgress);
            }

            // 4. BILLBOARDING: Otáčení na kameru (používáme kešovanou kameru)
            if (_mainCamera != null)
            {
                // Otočíme číslo tak, aby vždy směřovalo přesně na kameru
                obj.transform.rotation = Quaternion.LookRotation(obj.transform.position - _mainCamera.transform.position);
            }

            yield return null;
        }

        Destroy(obj);
    }

    /// <summary>
    /// Zobrazí vizuální číslo čistě lokálně, bez využití sítě. Ideální pro XP a Gold.
    /// </summary>
    public void SpawnPopupLocal(Vector3 position, int amount, PopupType type)
    {
        // Náhodný rozptyl, aby se čísla nepřekrývala
        Vector3 randomOffset = new Vector3(Random.Range(-0.6f, 0.6f), Random.Range(-0.2f, 0.2f), Random.Range(-0.6f, 0.6f));
        GameObject popup = Instantiate(_textPrefab, position + _offset + randomOffset, Quaternion.identity);

        var tmpro = popup.GetComponent<TextMeshPro>();
        if (tmpro != null)
        {
            // Znaménko + pro pozitivní hodnoty (Heal, XP, Gold)
            if (type == PopupType.Heal || type == PopupType.Experience || type == PopupType.Gold)
            {
                tmpro.text = $"+{amount}";
            }
            else
            {
                tmpro.text = amount.ToString();
            }

            // Nastavení vizuálu podle typu
            switch (type)
            {
                case PopupType.Damage:
                    tmpro.color = _damageColor;
                    tmpro.fontSize = 4f;
                    break;
                case PopupType.Critical:
                    tmpro.color = _critColor;
                    tmpro.fontSize = 6f;
                    // Zvýraznění kritického zásahu (mírně nahoru)
                    popup.transform.position += new Vector3(0, 0.5f, 0);
                    break;
                case PopupType.Heal:
                    tmpro.color = _healColor;
                    tmpro.fontSize = 4.5f;
                    break;
                case PopupType.Experience:
                    tmpro.color = _xpColor;
                    tmpro.fontSize = 3.5f;
                    break;
                case PopupType.Gold:
                    tmpro.color = _goldColor;
                    tmpro.fontSize = 3.5f;
                    break;
            }
        }

        // Znovu využijeme stávající animační Coroutinu
        StartCoroutine(AnimatePopup(popup, tmpro));
    }

}