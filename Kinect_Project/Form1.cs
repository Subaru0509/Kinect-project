using Microsoft.Azure.Kinect.BodyTracking;
using Microsoft.Azure.Kinect.Sensor;
using System;
using System.Drawing;
using System.Drawing.Imaging;
using System.Numerics;
using System.Threading.Tasks;
using System.Windows.Forms;
using Image = Microsoft.Azure.Kinect.Sensor.Image;
using ImageFormat = Microsoft.Azure.Kinect.Sensor.ImageFormat;

namespace Kinect_Project
{
    public partial class Form1 : Form
    {
        private Device kinectDevice;
        private Tracker bodyTracker;
        private bool isRunning = true;

        // 摩天大楼吊篮及建筑状态管理
        private bool isGondolaMoving = false;
        private BuildingGrid buildingGrid;

        // 线程安全的渲染缓冲
        private Bitmap currentFrame = null;
        private readonly object frameLock = new object();

        // 交互状态追踪
        private PointF leftHandPos = new PointF(-1, -1);
        private PointF rightHandPos = new PointF(-1, -1);
        private float leftHandZ = 0;
        private float rightHandZ = 0;

        // Z轴判定区间（单位：毫米，模拟玻璃的位置：距离镜头100cm以内全算触碰，不设下限）
        private const float MaxTouchZ = 1000f;

        public Form1()
        {
            InitializeComponent();
            
            this.FormClosing += Form1_FormClosing;
            pictureBox1.Paint += PictureBox_Paint;
            
            buildingGrid = new BuildingGrid();

            InitializeGame();
            InitializeKinectAsync();
        }

        private void InitializeGame()
        {
            _ = GondolaDropAnimationAsync(true);
        }

        private async Task GondolaDropAnimationAsync(bool isInitial = false)
        {
            isGondolaMoving = true;

            if (isInitial)
            {
                // 游戏刚开始：模拟来到楼顶，从上到下刷出建筑（包含墙和玻璃）
                buildingGrid.ClearGrid();
                for (int y = 0; y < BuildingGrid.Rows; y++)
                {
                    buildingGrid.GenerateRow(y);
                    if (!pictureBox1.IsDisposed) pictureBox1.Invalidate();
                    await Task.Delay(30); 
                }
            }
            else
            {
                // 吊篮下降一层：画面相对向上移，底部生成新一层墙/玻璃
                for (int step = 0; step < BuildingGrid.Rows; step++)
                {
                    buildingGrid.DropAnimationStep();

                    if (!pictureBox1.IsDisposed) pictureBox1.Invalidate();
                    await Task.Delay(40);
                }
            }
            
            buildingGrid.ResetCleanCount();
            isGondolaMoving = false;
        }

        private async void InitializeKinectAsync()
        {
            try
            {
                kinectDevice = Device.Open();
                var deviceConfig = new DeviceConfiguration()
                {
                    ColorFormat = ImageFormat.ColorBGRA32,
                    ColorResolution = ColorResolution.R720p,
                    DepthMode = DepthMode.NFOV_Unbinned,
                    CameraFPS = FPS.FPS30,
                    SynchronizedImagesOnly = true,
                };

                kinectDevice.StartCameras(deviceConfig);
                
                var trackerConfig = new TrackerConfiguration()
                {
                    ProcessingMode = TrackerProcessingMode.Gpu,
                    SensorOrientation = SensorOrientation.Default
                };
                
                bodyTracker = Tracker.Create(kinectDevice.GetCalibration(), trackerConfig);

                await Task.Run(() => KinectTrackingLoop());
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Kinect初始化失败: {ex.Message}");
            }
        }

        private void KinectTrackingLoop()
        {
            Calibration calibration = kinectDevice.GetCalibration();

            while (isRunning)
            {
                try
                {
                    using (Capture capture = kinectDevice.GetCapture(TimeSpan.FromMilliseconds(500)))
                    {
                        if (capture == null) continue;

                        bodyTracker.EnqueueCapture(capture);

                        using (Image colorImage = capture.Color)
                        {
                            if (colorImage != null)
                            {
                                Bitmap newBitmap = BitmapFromColorImage(colorImage);
                                newBitmap.RotateFlip(RotateFlipType.RotateNoneFlipX); // 镜像画面

                                lock (frameLock)
                                {
                                    if (currentFrame != null) currentFrame.Dispose();
                                    currentFrame = newBitmap;
                                }
                            }
                        }

                        using (Frame frame = bodyTracker.PopResult(TimeSpan.Zero))
                        {
                            if (frame != null && frame.NumberOfBodies > 0)
                            {
                                ProcessBody(frame, calibration);
                            }
                            else
                            {
                                leftHandPos = new PointF(-1, -1);
                                rightHandPos = new PointF(-1, -1);
                            }
                        }

                        if (isRunning && !pictureBox1.IsDisposed)
                        {
                            pictureBox1.Invoke(new Action(() => pictureBox1.Invalidate()));
                        }
                    }
                }
                catch (TimeoutException) { }
                catch (ObjectDisposedException) { }
                catch (Exception ex) { 
                    System.Diagnostics.Debug.WriteLine($"帧循环异常: {ex.Message}");
                }
            }
        }

        private void ProcessBody(Frame frame, Calibration calibration)
        {
            var skeleton = frame.GetBodySkeleton(0);

            var leftHand = skeleton.GetJoint(JointId.HandLeft);
            var rightHand = skeleton.GetJoint(JointId.HandRight);

            leftHandZ = leftHand.Position.Z;
            rightHandZ = rightHand.Position.Z;

            var leftPos2D = calibration.TransformTo2D(leftHand.Position, CalibrationDeviceType.Depth, CalibrationDeviceType.Color);
            var rightPos2D = calibration.TransformTo2D(rightHand.Position, CalibrationDeviceType.Depth, CalibrationDeviceType.Color);

            if (leftPos2D.HasValue)
            {
                leftHandPos = new PointF(1280f - leftPos2D.Value.X, leftPos2D.Value.Y);
                TryWipe(leftHandPos, leftHandZ, isLeftHand: true);
            }
            if (rightPos2D.HasValue)
            {
                rightHandPos = new PointF(1280f - rightPos2D.Value.X, rightPos2D.Value.Y);
                TryWipe(rightHandPos, rightHandZ, isLeftHand: false);
            }
        }

        private void TryWipe(PointF handPos2D, float zAxis, bool isLeftHand)
        {
            if (isGondolaMoving) return;

            if (zAxis <= MaxTouchZ)
            {
                // 解析坐标比例尺
                float xRatio = handPos2D.X / 1280f;
                float yRatio = handPos2D.Y / 720f;

                buildingGrid.ProcessWipe(xRatio, yRatio, isLeftHand);

                // 判断是否清洁达标以触发下降
                if (!isGondolaMoving && buildingGrid.IsAreaCleaned(0.95f))
                {
                    _ = GondolaDropAnimationAsync();
                }
            }
        }

        private void PictureBox_Paint(object sender, PaintEventArgs e)
        {
            Graphics g = e.Graphics;
            float viewWidth = pictureBox1.Width;
            float viewHeight = pictureBox1.Height;

            // 1. 渲染RGB视频底层
            lock (frameLock)
            {
                if (currentFrame != null)
                {
                    g.DrawImage(currentFrame, 0, 0, viewWidth, viewHeight);
                }
            }

            // 2. 渲染建筑墙壁与脏污玻璃
            buildingGrid.Draw(g, viewWidth, viewHeight);

            // 3. 绘制手部反馈标识符
            DrawHandIndicator(g, leftHandPos, leftHandZ, viewWidth, viewHeight, isLeftHand: true);
            DrawHandIndicator(g, rightHandPos, rightHandZ, viewWidth, viewHeight, isLeftHand: false);
        }

        private void DrawHandIndicator(Graphics g, PointF pos, float z, float viewWidth, float viewHeight, bool isLeftHand)
        {
            if (pos.X < 0 || pos.Y < 0) return;

            float uiX = (pos.X / 1280f) * viewWidth;
            float uiY = (pos.Y / 720f) * viewHeight;

            Brush activeBrush = isLeftHand ? Brushes.DeepSkyBlue : Brushes.Gold;
            Brush brush = (z <= MaxTouchZ) ? activeBrush : Brushes.Red;
            int size = 20;
            g.FillEllipse(brush, uiX - size / 2, uiY - size / 2, size, size);
        }

        private Bitmap BitmapFromColorImage(Image image)
        {
            int width = image.WidthPixels;
            int height = image.HeightPixels;

            Bitmap bitmap = new Bitmap(width, height, PixelFormat.Format32bppArgb);
            BitmapData bitmapData = bitmap.LockBits(
                new Rectangle(0, 0, width, height),
                ImageLockMode.WriteOnly,
                PixelFormat.Format32bppArgb);

            try
            {
                unsafe
                {
                    byte* ptr = (byte*)bitmapData.Scan0;
                    Span<byte> imageSpan = image.Memory.Span;

                    int stride = bitmapData.Stride;
                    int bytesPerPixel = 4;

                    for (int y = 0; y < height; y++)
                    {
                        int srcRowStart = y * width * bytesPerPixel;
                        int destRowStart = y * stride;

                        for (int x = 0; x < width * bytesPerPixel; x++)
                        {
                            ptr[destRowStart + x] = imageSpan[srcRowStart + x];
                        }
                    }
                }
            }
            finally
            {
                bitmap.UnlockBits(bitmapData);
            }
            return bitmap;
        }

        private void Form1_FormClosing(object sender, FormClosingEventArgs e)
        {
            isRunning = false;

            if (bodyTracker != null)
                bodyTracker.Dispose();

            if (kinectDevice != null)
            {
                kinectDevice.StopCameras();
                kinectDevice.Dispose();
            }
            
            buildingGrid?.Dispose();

            lock (frameLock)
            {
                if (currentFrame != null) currentFrame.Dispose();
            }
        }
    }
}
