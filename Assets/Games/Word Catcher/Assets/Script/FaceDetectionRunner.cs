using System.Collections;
using UnityEngine;
using UnityEngine.Rendering;
using Mediapipe.Tasks.Vision.FaceDetector;
using Mediapipe.Tasks.Components.Containers;
using FaceDetectionResult = Mediapipe.Tasks.Components.Containers.DetectionResult;

namespace Mediapipe.Unity.Sample.FaceDetection
{
    public class FaceDetectionRunner : VisionTaskApiRunner<FaceDetector>
    {
        [SerializeField] private DetectionResultAnnotationController _detectionResultAnnotationController;

        private Experimental.TextureFramePool _textureFramePool;
        public readonly FaceDetectionConfig config = new FaceDetectionConfig();

        public FaceDetectionResult currentResult { get; private set; }

        // Optional hook if you want external components to subscribe to result
        public System.Action<FaceDetectionResult> OnResultReceived;

        protected override IEnumerator Run()
        {
            Debug.Log($"[FaceDetection] Config:\nDelegate = {config.Delegate}\nImageReadMode = {config.ImageReadMode}\nModel = {config.ModelName}");

            yield return AssetLoader.PrepareAssetAsync(config.ModelPath);

            var options = config.GetFaceDetectorOptions(
                config.RunningMode == Tasks.Vision.Core.RunningMode.LIVE_STREAM
                ? OnFaceDetectionsOutput : null
            );

            taskApi = FaceDetector.CreateFromOptions(options, GpuManager.GpuResources);
            var imageSource = ImageSourceProvider.ImageSource;

            yield return imageSource.Play();
            if (!imageSource.isPrepared)
            {
                Debug.LogError("Image source failed to start.");
                yield break;
            }

            _textureFramePool = new Experimental.TextureFramePool(
                imageSource.textureWidth, imageSource.textureHeight, TextureFormat.RGBA32, 10
            );

            screen.Initialize(imageSource);
            SetupAnnotationController(_detectionResultAnnotationController, imageSource);

            var transformationOptions = imageSource.GetTransformationOptions();
            var flipHorizontally = transformationOptions.flipHorizontally;
            var flipVertically = transformationOptions.flipVertically;
            var imageProcessingOptions = new Tasks.Vision.Core.ImageProcessingOptions(rotationDegrees: (int)transformationOptions.rotationAngle);

            AsyncGPUReadbackRequest req = default;
            var waitUntilReqDone = new WaitUntil(() => req.done);
            var waitForEndOfFrame = new WaitForEndOfFrame();
            var result = FaceDetectionResult.Alloc(options.numFaces);

            var canUseGpuImage = SystemInfo.graphicsDeviceType == GraphicsDeviceType.OpenGLES3 && GpuManager.GpuResources != null;
            using var glContext = canUseGpuImage ? GpuManager.GetGlContext() : null;

            while (true)
            {
                if (isPaused)
                    yield return new WaitWhile(() => isPaused);

                if (!_textureFramePool.TryGetTextureFrame(out var textureFrame))
                {
                    yield return null;
                    continue;
                }

                Image image;
                switch (config.ImageReadMode)
                {
                    case ImageReadMode.GPU:
                        if (!canUseGpuImage) throw new System.Exception("GPU mode not supported");
                        textureFrame.ReadTextureOnGPU(imageSource.GetCurrentTexture(), flipHorizontally, flipVertically);
                        image = textureFrame.BuildGPUImage(glContext);
                        yield return waitForEndOfFrame;
                        break;

                    case ImageReadMode.CPU:
                        yield return waitForEndOfFrame;
                        textureFrame.ReadTextureOnCPU(imageSource.GetCurrentTexture(), flipHorizontally, flipVertically);
                        image = textureFrame.BuildCPUImage();
                        textureFrame.Release();
                        break;

                    case ImageReadMode.CPUAsync:
                    default:
                        req = textureFrame.ReadTextureAsync(imageSource.GetCurrentTexture(), flipHorizontally, flipVertically);
                        yield return waitUntilReqDone;
                        if (req.hasError)
                        {
                            Debug.LogWarning("Async readback error");
                            continue;
                        }
                        image = textureFrame.BuildCPUImage();
                        textureFrame.Release();
                        break;
                }

                switch (taskApi.runningMode)
                {
                    case Tasks.Vision.Core.RunningMode.IMAGE:
                        if (taskApi.TryDetect(image, imageProcessingOptions, ref result))
                            _detectionResultAnnotationController.DrawNow(result);
                        else
                            _detectionResultAnnotationController.DrawNow(default);
                        break;

                    case Tasks.Vision.Core.RunningMode.VIDEO:
                        if (taskApi.TryDetectForVideo(image, GetCurrentTimestampMillisec(), imageProcessingOptions, ref result))
                            _detectionResultAnnotationController.DrawNow(result);
                        else
                            _detectionResultAnnotationController.DrawNow(default);
                        break;

                    case Tasks.Vision.Core.RunningMode.LIVE_STREAM:
                        taskApi.DetectAsync(image, GetCurrentTimestampMillisec(), imageProcessingOptions);
                        break;
                }
            }
        }

      
        private void OnFaceDetectionsOutput(FaceDetectionResult result, Image image, long timestamp)
        {
            currentResult = result;
            _detectionResultAnnotationController.DrawLater(result);
        }

      
    }
}
