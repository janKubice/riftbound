using UnityEngine;

public class PlayerSquashStretch : MonoBehaviour
{
    [Header("References")]
    [SerializeField] private PlayerController _controller;
    
    [Tooltip("SEM přetáhni objekt s grafikou (Mesh/Model), který je uvnitř hráče.")]
    [SerializeField] private Transform _visualModel; 

    [Header("Settings")]
    [SerializeField] private float _jumpStretch = 1.2f;    // Natažení při skoku (Y > 1)
    [SerializeField] private float _landSquash = 0.7f;     // Zmáčknutí při dopadu (Y < 1)
    [SerializeField] private float _returnSpeed = 10f;     // Rychlost návratu do normálu

    private Vector3 _originalScale;
    private Vector3 _targetScale;

    private void Start()
    {
        // 1. Automatické přiřazení modelu, pokud jsi zapomněl
        if (_visualModel == null)
        {
            // Zkusíme najít první child objekt (většinou grafika)
            if (transform.childCount > 0)
            {
                _visualModel = transform.GetChild(0);
                Debug.LogWarning($"[PlayerSquashStretch] _visualModel nebyl přiřazen! Automaticky používám: {_visualModel.name}. Pro jistotu to přiřaď v Inspektoru.");
            }
            else
            {
                // Krizový stav - nemáme co deformovat, deformujeme sami sebe (to způsobí drift, ale aspoň to nepadne)
                _visualModel = transform;
                Debug.LogError("[PlayerSquashStretch] POZOR! Deformuješ kořenový objekt. To rozbíjí fyziku. Přiřaď 'VisualModel'!");
            }
        }

        _originalScale = _visualModel.localScale;
        _targetScale = _originalScale;

        if (_controller == null) _controller = GetComponentInParent<PlayerController>();
    }

    private void Update()
    {
        // Plynulý návrat k původnímu měřítku - APLIKOVÁNO NA MODEL, NE NA ROOT
        _visualModel.localScale = Vector3.Lerp(_visualModel.localScale, _targetScale, Time.deltaTime * _returnSpeed);
        
        // Plynulý návrat targetu do normálu
        _targetScale = Vector3.Lerp(_targetScale, _originalScale, Time.deltaTime * 5f);
    }

    // Tyto metody volá PlayerController (jak jsme nastavili v HandleJump/OnLand)
    public void TriggerJumpSquash()
    {
        _targetScale = new Vector3(_originalScale.x * (1/_jumpStretch), _originalScale.y * _jumpStretch, _originalScale.z * (1/_jumpStretch));
    }

    public void TriggerLandSquash()
    {
        _targetScale = new Vector3(_originalScale.x * (1/_landSquash), _originalScale.y * _landSquash, _originalScale.z * (1/_landSquash));
    }
}