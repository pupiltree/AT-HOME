using Mediapipe.Unity;
using Mediapipe.Unity.CoordinateSystem;
using Mediapipe.Unity.Sample.FaceDetection;
using System.Text.RegularExpressions;
using UnityEngine;

public class MediaPipeNoseBridge : HierarchicalAnnotation
{
    public BasketController basketController;
    public FaceDetectionRunner detectionRunner;
    public RectTransform objects;

    void Update()
    {
        if (detectionRunner.currentResult.detections != null && detectionRunner.currentResult.detections.Count > 0)
            Move(detectionRunner.currentResult.detections[0].keypoints[2]);
    }
    public UnityEngine.Rect GetScreenRect()
    {
        return GetAnnotationLayer().rect;
    }
    public void Move(Mediapipe.Tasks.Components.Containers.NormalizedKeypoint target)
    {
        if (ActivateFor(target))
        {
            Vector2 vector = GetScreenRect().GetPoint(target, rotationAngle, isMirrored);
#if UNITY_EDITOR
            basketController.UpdateBasketPosition(target.x);
#elif UNITY_ANDROID
            basketController.UpdateBasketPosition(target.y);
#endif
        }
    }
    public RectTransform GetAnnotationLayer()
    {
        return objects.GetComponent<RectTransform>();
    }

}
