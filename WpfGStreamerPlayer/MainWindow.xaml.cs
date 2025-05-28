using System;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Media.Imaging;
using System.Threading;
using System.Windows.Threading;

namespace WpfGStreamerPlayer
{
    public partial class MainWindow : Window
    {
        private IntPtr pipeline;
        private IntPtr appsink;
        private Thread videoThread;
        private bool isPlaying = false;
        private WriteableBitmap writeableBitmap;
        private int width = 1280;
        private int height = 720;
        private bool stopRequested = false;

        public MainWindow()
        {
            InitializeComponent();
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
            string pipelineStr = $"udpsrc port=5000 caps=\"application/x-rtp,encoding-name=H264,payload=96\" ! " +
                                 "rtph264depay ! h264parse ! avdec_h264 ! videoconvert ! video/x-raw,format=BGRx,width=1280,height=720 ! " +
                                 "appsink name=appsink emit-signals=false sync=false max-buffers=1 drop=true";

            pipeline = GStreamer.gst_parse_launch(pipelineStr, out IntPtr error);

            if (pipeline == IntPtr.Zero)
            {
                MessageBox.Show("GStreamer pipeline failed.");
                return;
            }

            appsink = GStreamer.gst_bin_get_by_name(pipeline, "appsink");
            GStreamer.gst_element_set_state(pipeline, GstState.GST_STATE_PLAYING);

            writeableBitmap = new WriteableBitmap(width, height, 96, 96, System.Windows.Media.PixelFormats.Bgr32, null);
            VideoImage.Source = writeableBitmap;

            stopRequested = false; // 重要: 開始時にフラグをリセット
            videoThread = new Thread(VideoLoop);
            videoThread.Start();

            StatusLabel.Content = "ストリーミング中...";
        }

        private void StopGStreamerPipeline()
        {
            // 1. まずstopフラグを設定
            stopRequested = true;

            // 2. パイプラインを停止状態にする（これによりappsinkからのデータ取得が停止）
            if (pipeline != IntPtr.Zero)
            {
                GStreamer.gst_element_set_state(pipeline, GstState.GST_STATE_NULL);
            }

            // 3. スレッドの終了を待つ（タイムアウト付き）
            if (videoThread != null && videoThread.IsAlive)
            {
                if (!videoThread.Join(2000)) // 2秒でタイムアウト
                {
                    // タイムアウトした場合は強制終了
                    try
                    {
                        videoThread.Abort();
                    }
                    catch (Exception ex)
                    {
                        System.Diagnostics.Debug.WriteLine($"Thread abort failed: {ex.Message}");
                    }
                }
                videoThread = null;
            }

            // 4. リソースを解放
            if (appsink != IntPtr.Zero)
            {
                GStreamer.gst_object_unref(appsink);
                appsink = IntPtr.Zero;
            }

            if (pipeline != IntPtr.Zero)
            {
                GStreamer.gst_object_unref(pipeline);
                pipeline = IntPtr.Zero;
            }

            StatusLabel.Content = "停止中";
        }

        private void VideoLoop()
        {
            while (!stopRequested)
            {
                try
                {
                    // タイムアウト付きでサンプルを取得
                    IntPtr sample = GStreamer.gst_app_sink_try_pull_sample(appsink, 100000000); // 100ms timeout (nanoseconds)

                    if (sample == IntPtr.Zero)
                    {
                        // タイムアウトまたはEOS - 少し待ってから次のループへ
                        Thread.Sleep(10);
                        continue;
                    }

                    if (stopRequested) break; // 停止要求があれば即座に終了

                    IntPtr buffer = GStreamer.gst_sample_get_buffer(sample);
                    IntPtr caps = GStreamer.gst_sample_get_caps(sample);

                    if (GStreamer.gst_buffer_map(buffer, out GStreamer.GstMapInfo map, true))
                    {
                        byte[] pixelData = new byte[width * height * 4];
                        Marshal.Copy(map.data, pixelData, 0, pixelData.Length);

                        // UI更新はDispatcher.BeginInvokeを使用（非同期）
                        Dispatcher.BeginInvoke(new Action(() =>
                        {
                            try
                            {
                                if (writeableBitmap != null && !stopRequested)
                                {
                                    writeableBitmap.Lock();
                                    Marshal.Copy(pixelData, 0, writeableBitmap.BackBuffer, pixelData.Length);
                                    writeableBitmap.AddDirtyRect(new Int32Rect(0, 0, width, height));
                                    writeableBitmap.Unlock();
                                }
                            }
                            catch (Exception ex)
                            {
                                System.Diagnostics.Debug.WriteLine($"UI update failed: {ex.Message}");
                            }
                        }), DispatcherPriority.Background);

                        GStreamer.gst_buffer_unmap(buffer, ref map);
                    }

                    GStreamer.gst_sample_unref(sample);
                }
                catch (Exception ex)
                {
                    System.Diagnostics.Debug.WriteLine($"VideoLoop error: {ex.Message}");
                    // エラーが発生した場合は少し待ってから続行
                    Thread.Sleep(100);
                }
            }

            System.Diagnostics.Debug.WriteLine("VideoLoop ended");
        }

        protected override void OnClosed(EventArgs e)
        {
            StopGStreamerPipeline();
            base.OnClosed(e);
        }
    }

    public static class GStreamer
    {
        private const string GstCore = "gstreamer-1.0-0.dll";
        private const string GstApp = "gstapp-1.0-0.dll";
        private const string GstVideo = "gstvideo-1.0-0.dll";

        [DllImport(GstCore)] public static extern void gst_init(IntPtr argc, IntPtr argv);
        [DllImport(GstCore)] public static extern IntPtr gst_parse_launch(string pipeline, out IntPtr error);
        [DllImport(GstCore)] public static extern int gst_element_set_state(IntPtr element, GstState state);
        [DllImport(GstCore)] public static extern IntPtr gst_bin_get_by_name(IntPtr bin, string name);
        [DllImport(GstCore)] public static extern void gst_object_unref(IntPtr obj);

        [DllImport(GstApp)] public static extern IntPtr gst_app_sink_pull_sample(IntPtr sink);
        [DllImport(GstApp)] public static extern IntPtr gst_app_sink_try_pull_sample(IntPtr sink, ulong timeout);
        [DllImport(GstCore)] public static extern void gst_sample_unref(IntPtr sample);

        [DllImport(GstCore)] public static extern IntPtr gst_sample_get_buffer(IntPtr sample);
        [DllImport(GstCore)] public static extern IntPtr gst_sample_get_caps(IntPtr sample);
        [DllImport(GstCore)] public static extern bool gst_buffer_map(IntPtr buffer, out GstMapInfo map, bool read);
        [DllImport(GstCore)] public static extern void gst_buffer_unmap(IntPtr buffer, ref GstMapInfo map);

        [StructLayout(LayoutKind.Sequential)]
        public struct GstMapInfo
        {
            public UIntPtr memory;
            public int flags;
            public IntPtr data;
            public UIntPtr size;
            public UIntPtr maxsize;
            public IntPtr user_data;
            public UIntPtr n_mini_objects;
        }
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