// Copyright (c) 2023 homuler
//
// Use of this source code is governed by an MIT-style
// license that can be found in the LICENSE file or at
// https://opensource.org/licenses/MIT.

using System.Collections;
using Mediapipe.Tasks.Vision.FaceLandmarker;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Android;

namespace Mediapipe.Unity.Sample.FaceLandmarkDetection
{
  public class FaceLandmarkerRunner : VisionTaskApiRunner<FaceLandmarker>
  {
    [SerializeField] private FaceLandmarkerResultAnnotationController _faceLandmarkerResultAnnotationController;

    private Experimental.TextureFramePool _textureFramePool;

    public readonly FaceLandmarkDetectionConfig config = new FaceLandmarkDetectionConfig();

    public override void Stop()
    {
      base.Stop();
      _textureFramePool?.Dispose();
      _textureFramePool = null;
    }

    protected override IEnumerator Run()
    {
        Debug.Log($"Delegate = {config.Delegate}");
        Debug.Log($"Image Read Mode = {config.ImageReadMode}");
        Debug.Log($"Running Mode = {config.RunningMode}");
        Debug.Log($"NumFaces = {config.NumFaces}");
        Debug.Log($"MinFaceDetectionConfidence = {config.MinFaceDetectionConfidence}");
        Debug.Log($"MinFacePresenceConfidence = {config.MinFacePresenceConfidence}");
        Debug.Log($"MinTrackingConfidence = {config.MinTrackingConfidence}");
        Debug.Log($"OutputFaceBlendshapes = {config.OutputFaceBlendshapes}");
        Debug.Log($"OutputFacialTransformationMatrixes = {config.OutputFacialTransformationMatrixes}");

        yield return AssetLoader.PrepareAssetAsync(config.ModelPath);

        var options = config.GetFaceLandmarkerOptions(config.RunningMode == Tasks.Vision.Core.RunningMode.LIVE_STREAM ? OnFaceLandmarkDetectionOutput : null);
        taskApi = FaceLandmarker.CreateFromOptions(options, GpuManager.GpuResources);

        var imageSource = ImageSourceProvider.ImageSource;

        //  NEW FIX: Wait for permission and camera device to be available
        yield return new WaitUntil(() =>
        {
    #if UNITY_ANDROID && !UNITY_EDITOR
            return WebCamTexture.devices.Length > 0 && Permission.HasUserAuthorizedPermission(Permission.Camera);
    #else
            return WebCamTexture.devices.Length > 0;
    #endif
        });

        yield return imageSource.Play();

        // Wait until camera is ready before proceeding
        yield return new WaitUntil(() => imageSource.isPrepared);

        // If still not ready, exit
        if (!imageSource.isPrepared)
        {
            Debug.LogError("Failed to start ImageSource, exiting...");
            yield break;
        }

        _textureFramePool = new Experimental.TextureFramePool(imageSource.textureWidth, imageSource.textureHeight, TextureFormat.RGBA32, 10);
        screen.Initialize(imageSource);
        SetupAnnotationController(_faceLandmarkerResultAnnotationController, imageSource);

        var transformationOptions = imageSource.GetTransformationOptions();
        var flipHorizontally = transformationOptions.flipHorizontally;
        var flipVertically = transformationOptions.flipVertically;
        var imageProcessingOptions = new Tasks.Vision.Core.ImageProcessingOptions(rotationDegrees: (int)transformationOptions.rotationAngle);

        AsyncGPUReadbackRequest req = default;
        var waitUntilReqDone = new WaitUntil(() => req.done);
        var waitForEndOfFrame = new WaitForEndOfFrame();
        var result = FaceLandmarkerResult.Alloc(options.numFaces);

        var canUseGpuImage = SystemInfo.graphicsDeviceType == GraphicsDeviceType.OpenGLES3 && GpuManager.GpuResources != null;
        using var glContext = canUseGpuImage ? GpuManager.GetGlContext() : null;

        while (true)
        {
            if (isPaused)
            {
                yield return new WaitWhile(() => isPaused);
            }

            if (!_textureFramePool.TryGetTextureFrame(out var textureFrame))
            {
                yield return null;
                continue;
            }

            Image image;
            switch (config.ImageReadMode)
            {
                case ImageReadMode.GPU:
                    if (!canUseGpuImage)
                    {
                        throw new System.Exception("ImageReadMode.GPU is not supported");
                    }
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
                        Debug.LogWarning($"Failed to read texture from the image source");
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
                        _faceLandmarkerResultAnnotationController.DrawNow(result);
                    else
                        _faceLandmarkerResultAnnotationController.DrawNow(default);
                    break;

                case Tasks.Vision.Core.RunningMode.VIDEO:
                    if (taskApi.TryDetectForVideo(image, GetCurrentTimestampMillisec(), imageProcessingOptions, ref result))
                        _faceLandmarkerResultAnnotationController.DrawNow(result);
                    else
                        _faceLandmarkerResultAnnotationController.DrawNow(default);
                    break;

                case Tasks.Vision.Core.RunningMode.LIVE_STREAM:
                    taskApi.DetectAsync(image, GetCurrentTimestampMillisec(), imageProcessingOptions);
                    break;
            }
        }
    }

        public MediaPipeNoseBridge mediaPipeNoseBridge;

        private void OnFaceLandmarkDetectionOutput(FaceLandmarkerResult result, Image image, long timestamp)
        {
            _faceLandmarkerResultAnnotationController.DrawLater(result);

            if (result.faceLandmarks == null || result.faceLandmarks.Count == 0)
                return;

            var landmarks = result.faceLandmarks[0].landmarks;
            if (landmarks == null || landmarks.Count < 300)
                return;

            if (landmarks[1].visibility < 0.8f) // Nose visibility check
                return;

            var leftEye = landmarks[33];
            var rightEye = landmarks[263];
            var nose = landmarks[1];

            float avgX = (leftEye.x + rightEye.x + nose.x) / 3f;

            // ✅ Flip horizontally for Android front camera
        #if UNITY_ANDROID && !UNITY_EDITOR
            avgX = 1f - avgX;
        #endif

            // 🧠 We ONLY care about X movement (horizontal left-right)
            int screenX = (int)(avgX * MediaPipeNoseBridge.screenWidth);

            // ⚠️ Do NOT use Y axis (up/down)
            // You can hardcode or ignore Y — we pass 0 for now
            string faceCenterData = screenX + "," + 0;

            MainThreadDispatcher.Enqueue(() =>
            {
                mediaPipeNoseBridge.OnReceiveFaceData(faceCenterData);
            });
        }
    }
}
