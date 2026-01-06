using UnityEngine;

public class CloudMovement : MonoBehaviour
{
    private float _speed;
    private float _resetX; // Hranice, kde mrak "končí"
    private float _startX; // Hranice, kde se mrak "zrodí znovu"

    public void Initialize(float speed, float startX, float resetX)
    {
        _speed = speed;
        _startX = startX;
        _resetX = resetX;
    }

    private void Update()
    {
        // 1. Pohyb v LOKÁLNÍM prostoru rodiče (CloudManageru)
        // Díky tomu se mraky hýbou vždy ve směru šipky Manageru
        transform.localPosition += Vector3.right * _speed * Time.deltaTime;

        // 2. Kontrola hranice (taky v lokálních souřadnicích)
        if (transform.localPosition.x > _resetX)
        {
            RecycleCloud();
        }
    }

    private void RecycleCloud()
    {
        // Teleport zpátky na začátek
        Vector3 newPos = transform.localPosition;
        newPos.x = _startX;
        transform.localPosition = newPos;
    }
}