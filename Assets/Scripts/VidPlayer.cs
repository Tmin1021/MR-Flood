using UnityEngine;
using UnityEngine.Video;

[RequireComponent(typeof(VideoPlayer))]
public sealed class VidPlayer : MonoBehaviour
{
    [SerializeField]
    private VideoPlayer targetVideoPlayer;

    [SerializeField]
    private string relativeVideoPath = "Videos/20260713_141944_HoloLens.mp4";

    private VideoPlayer videoPlayer;

    private void Awake()
    {
        videoPlayer = targetVideoPlayer != null ? targetVideoPlayer : GetComponent<VideoPlayer>();
        videoPlayer.playOnAwake = false;
        videoPlayer.url = BuildStreamingAssetsUrl(relativeVideoPath);
        videoPlayer.prepareCompleted += OnPrepareCompleted;
        videoPlayer.errorReceived += OnErrorReceived;
        videoPlayer.Prepare();
    }

    private void OnDestroy()
    {
        if (videoPlayer == null)
            return;

        videoPlayer.prepareCompleted -= OnPrepareCompleted;
        videoPlayer.errorReceived -= OnErrorReceived;
    }

    private static string BuildStreamingAssetsUrl(string relativePath)
    {
        string basePath = Application.streamingAssetsPath.TrimEnd('/', '\\');
        string cleanRelativePath = relativePath.TrimStart('/', '\\').Replace('\\', '/');
        return $"{basePath}/{cleanRelativePath}";
    }

    private static void OnPrepareCompleted(VideoPlayer source)
    {
        source.Play();
    }

    private static void OnErrorReceived(VideoPlayer source, string message)
    {
        Debug.LogError($"Unable to load the local MR Flood video: {message}", source);
    }
}
