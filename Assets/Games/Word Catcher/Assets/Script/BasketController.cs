using UnityEngine;
using UnityEngine.TestTools;

public class BasketController : MonoBehaviour
{

    public bool allowMovement = false;
    public float smoothing = 2;
    public void UpdateBasketPosition(float normalizedX)
    {
        if (!allowMovement)
        {
            return;
        }

        normalizedX = Mathf.Clamp01(normalizedX);

        float screenWidth = Screen.width;

        float screenPosX = normalizedX * screenWidth;

        float screenPosY = Camera.main.WorldToScreenPoint(transform.position).y;
        float screenPosZ = Camera.main.WorldToScreenPoint(transform.position).z;

        Vector3 screenPosition = new Vector3(screenPosX, screenPosY, screenPosZ);

        Vector3 worldPosition = Camera.main.ScreenToWorldPoint(screenPosition);

        transform.position = Vector3.Lerp(transform.position, new Vector3(worldPosition.x, transform.position.y, transform.position.z), Time.deltaTime * smoothing);
    }

}
