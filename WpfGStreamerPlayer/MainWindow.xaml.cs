using System;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Interop;
using System.Windows.Media;

namespace WpfGStreamerPlayer
{
    /// <summary>
    /// MainWindow.xaml の相互作用ロジック
    /// </summary>
    public partial class MainWindow : Window
    {
        private IntPtr pipeline;
        private IntPtr videoSink;
        private HwndSource hwndSource;
        private bool isPlaying = false;

        public MainWindow()
        {
            InitializeComponent();
            InitializeGStreamer();
        }

        private void InitializeGStreamer()
        {
            // GStreamerの初期化
            GStreamer.gst_init(IntPtr.Zero, IntPtr.Zero);
        }

        private void StartStreaming_Click(object sender, RoutedEventArgs e)
        {
            if (!isPlaying)
            {
                StartGStreamerPipeline();
                StartButton.Content = "停止";
                isPlaying = true;
            }
            else
            {
                StopGStreamerPipeline();
                StartButton.Content = "開始";
                isPlaying = false;
            }
        }

        private void StartGStreamerPipeline()
        {
            try
            {
                // パイプライン文字列の作成
                string pipelineString =
                    "udpsrc port=5000 caps=\"application/x-rtp,encoding-name=H264,payload=96\" ! " +
                    "rtph264depay ! h264parse ! avdec_h264 ! videoconvert ! d3dvideosink name=videosink";

                // パイプラインの作成
                pipeline = GStreamer.gst_parse_launch(pipelineString, out IntPtr error);

                if (pipeline == IntPtr.Zero)
                {
                    MessageBox.Show("パイプラインの作成に失敗しました");
                    return;
                }

                // ビデオシンクの取得
                videoSink = GStreamer.gst_bin_get_by_name(pipeline, "videosink");

                if (videoSink != IntPtr.Zero)
                {
                    // HwndSourceの取得
                    hwndSource = HwndSource.FromVisual(VideoPanel) as HwndSource;
                    if (hwndSource != null)
                    {
                        // ウィンドウハンドルの設定
                        GStreamer.gst_video_overlay_set_window_handle(videoSink, hwndSource.Handle);
                    }
                }

                // パイプラインの開始
                GStreamer.gst_element_set_state(pipeline, GstState.GST_STATE_PLAYING);

                StatusLabel.Content = "ストリーミング中...";
            }
            catch (Exception ex)
            {
                MessageBox.Show($"エラー: {ex.Message}");
            }
        }

        private void StopGStreamerPipeline()
        {
            if (pipeline != IntPtr.Zero)
            {
                GStreamer.gst_element_set_state(pipeline, GstState.GST_STATE_NULL);
                GStreamer.gst_object_unref(pipeline);
                pipeline = IntPtr.Zero;
            }

            if (videoSink != IntPtr.Zero)
            {
                GStreamer.gst_object_unref(videoSink);
                videoSink = IntPtr.Zero;
            }

            StatusLabel.Content = "停止中";
        }

        protected override void OnClosed(EventArgs e)
        {
            StopGStreamerPipeline();
            base.OnClosed(e);
        }
    }

    // GStreamer P/Invoke宣言
    public static class GStreamer
    {
        private const string GStreamerCore = "gstreamer-1.0-0.dll";
        private const string GStreamerVideo = "gstvideo-1.0-0.dll";

        [DllImport(GStreamerCore, CallingConvention = CallingConvention.Cdecl)]
        public static extern void gst_init(IntPtr argc, IntPtr argv);

        [DllImport(GStreamerCore, CallingConvention = CallingConvention.Cdecl)]
        public static extern IntPtr gst_parse_launch([MarshalAs(UnmanagedType.LPStr)] string pipeline_description, out IntPtr error);

        [DllImport(GStreamerCore, CallingConvention = CallingConvention.Cdecl)]
        public static extern int gst_element_set_state(IntPtr element, GstState state);

        [DllImport(GStreamerCore, CallingConvention = CallingConvention.Cdecl)]
        public static extern IntPtr gst_bin_get_by_name(IntPtr bin, [MarshalAs(UnmanagedType.LPStr)] string name);

        [DllImport(GStreamerCore, CallingConvention = CallingConvention.Cdecl)]
        public static extern void gst_object_unref(IntPtr obj);

        [DllImport(GStreamerVideo, CallingConvention = CallingConvention.Cdecl)]
        public static extern void gst_video_overlay_set_window_handle(IntPtr overlay, IntPtr window_handle);
    }

    public enum GstState
    {
        GST_STATE_VOID_PENDING = 0,
        GST_STATE_NULL = 1,
        GST_STATE_READY = 2,
        GST_STATE_PAUSED = 3,
        GST_STATE_PLAYING = 4
    }
}