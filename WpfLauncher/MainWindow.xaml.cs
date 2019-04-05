using DotNext.Native;
using Microsoft.Windows.Media;
using Microsoft.WpfPerformance.Data;
using Microsoft.WpfPerformance.Diagnostics;
using Microsoft.WpfPerformance.Threading;
using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;
using System.Windows;
using System.Windows.Data;
using System.Windows.Media;
using System.Windows.Threading;

namespace WpfLauncher
{
    /// <summary>
    /// Interaction logic for MainWindow.xaml
    /// </summary>
    public partial class MainWindow : Window
    {
        public static readonly DependencyProperty CpuLimitPercentProperty =
            DependencyProperty.Register("CpuLimitPercent", typeof(int), typeof(Window), new PropertyMetadata(100, new PropertyChangedCallback(CpuLimitChanged)));
        
        public static readonly DependencyProperty FPSProperty =
            DependencyProperty.Register("FPS", typeof(double), typeof(MainWindow));
        public static readonly DependencyProperty AverageFPSProperty =
            DependencyProperty.Register("AverageFPS", typeof(double), typeof(MainWindow));
        public static readonly DependencyProperty PercentElapsedTimeForCompositionProperty =
            DependencyProperty.Register("PercentElapsedTimeForComposition", typeof(double), typeof(MainWindow));
        public static readonly DependencyProperty PathDataProperty =
            DependencyProperty.Register("PathData", typeof(Geometry), typeof(MainWindow));

        public static readonly IntPtr HKEY_LOCAL_MACHINE = (IntPtr)(-2147483646);
        Process attachedProcess = null;
        ClrVersion clrVersion = ClrVersion.AnyClr;
        Job job;
        double fpsTotal = 0.0;

        MediaControl mediaControl;
        private DispatcherTimer updateTimer;

        public MainWindow()
        {
            InitializeComponent();
            IsDebugControlEnabled = true;
            Loaded += MainWindow_Loaded;
            fpsData = new Queue<double>();
            for (int i = 0; i < 100; i++) fpsData.Enqueue(0.0);
        }

        bool AttachToProcess(SelectProcessArgs selectProcessArgs)
        {
            attachedProcess = selectProcessArgs.Process;
            mediaControl = MediaControl.Attach(selectProcessArgs.Process.Id, clrVersion);
            return true;
        }

        private static void CpuLimitChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
        {
            MainWindow window = d as MainWindow;
            window.SetCPULimit();
        }

        private void Init()
        {
            var args = Environment.GetCommandLineArgs();
            if (args.Length < 2)
            {
                MessageBox.Show("Please provide path to a wpf application");
                return;
            }
            ProcessStartInfo processStartInfo = new ProcessStartInfo(args[1], string.Empty);
            processStartInfo.ErrorDialog = true;
            processStartInfo.WorkingDirectory = string.Empty;

            try
            {
                string directoryName = Path.GetDirectoryName(processStartInfo.FileName);
                if (Path.IsPathRooted(directoryName) && Directory.Exists(directoryName))
                {
                    processStartInfo.WorkingDirectory = directoryName;
                }
                ProcessProxy proxy = (ProcessProxy)Activator.CreateInstance(typeof(ProcessProxy),
                                                       BindingFlags.NonPublic | BindingFlags.Instance, null,
                                                       new object[] { processStartInfo, false, clrVersion }, null);
                SyncWorkItem syncWorkItem = new SyncWorkItem(ProcessHelper.WorkUntilWpfLoaded, proxy, null);
                SyncWorkItem syncWorkItem3 = syncWorkItem.Next = new SyncWorkItem(WorkUntilCanAttach, proxy, null);
                if (ProcessHelper.StartProxy(proxy, syncWorkItem) && AttachToProcess(new SelectProcessArgs(proxy.RemoteProcess, clrVersion)))
                {
                }

                IsAttached = mediaControl != null;
            }
            catch (ArgumentException)
            {
                IsAttached = false;
            }
            finally
            {
                if (IsAttached) updateTimer.Start();
                else updateTimer.Stop();
            }
        }

        private void MainWindow_Loaded(object sender, RoutedEventArgs e)
        {
            Init();
        }

        private void OnUpdateTimer(object sender, EventArgs e)
        {
            UpdateCompositorOwnedData();
        }
        void SetCPULimit()
        {
            if (attachedProcess == null) return;
            try
            {
                if (job == null)
                {
                    job = Job.Create();
                    job.AddProcess(attachedProcess);
                }
                job.SetCpuLimit(CpuLimitPercent);
            }
            catch (Exception ex)
            {
            }
        }

        private void UpdateCompositorOwnedData() {
            int num = 0;
            int num2 = 0;
            if(IsAttached) {
                PercentElapsedTimeForComposition = mediaControl.PercentElapsedTimeForComposition;
                num = mediaControl.FrameRate;
                num2 = mediaControl.DirtyRectAddRate;
            }
            FPS = num;
            UpdatePathData(FPS);
        }
        Queue<double> fpsData;
        protected void UpdatePathData(double fps) {
            fpsTotal -= fpsData.Dequeue();
            fpsTotal += fps;
            fpsData.Enqueue(fps);
            AverageFPS = fpsTotal / 100;
            StringBuilder sb = new StringBuilder();
            sb.Append("M 0,0 L 0,100 ");
            int counter = 0;
            foreach(var cfps in fpsData) {
                sb.Append(string.Format("{0},{1} ", counter++, 100- Math.Min(100, cfps)));
            }
            PathData = Geometry.Parse(sb.ToString());
        }

        private static WorkStatus WorkUntilCanAttach(object arg, WorkItem workItem)
        {
            WorkStatus result = WorkStatus.NotDone;
            ProcessProxy processProxy = (ProcessProxy)arg;
            Process remoteProcess = processProxy.RemoteProcess;
            try
            {
                if (remoteProcess != null)
                {
                    remoteProcess.Refresh();
                    if (!remoteProcess.HasExited)
                    {
                        if (!MediaControl.CanAttach(new SelectProcessArgs(processProxy.RemoteProcess, processProxy.CLRVersion)))
                        {
                            Thread.Sleep(500);
                            return result;
                        }
                        return WorkStatus.Done;
                    }
                    return WorkStatus.Error;
                }
                return WorkStatus.Error;
            }
            catch (Exception)
            {
                return WorkStatus.Error;
            }
        }

        private bool IsDebugControlEnabled {
            set {
                int lpData = value ? 1 : 0;
                int[] samDesired;
                if (IntPtr.Size != 8)
                {
                    int[] array = samDesired = new int[1];
                }
                else
                {
                    samDesired = new int[2]
                    {
            512,
            256
                    };
                }
                for (int i = 0; i < samDesired.Length; i++)
                {

                    RegCreateKeyEx(HKEY_LOCAL_MACHINE, "SOFTWARE\\Microsoft\\Avalon.Graphics", 0, null, 0, samDesired[i] | 0x20006, IntPtr.Zero, out IntPtr phkResult, IntPtr.Zero);
                    if (phkResult != IntPtr.Zero)
                    {
                        RegSetValueEx(phkResult, "EnableDebugControl", 0, 4, ref lpData, 4);
                        RegCloseKey(phkResult);
                    }
                }
            }
        }

        protected override void OnClosing(CancelEventArgs e)
        {
            base.OnClosing(e);
            if (attachedProcess != null) attachedProcess.Close();
            IsDebugControlEnabled = false;
        }

        protected override void OnInitialized(EventArgs e)
        {
            base.DataContext = this;
            updateTimer = new DispatcherTimer(DispatcherPriority.Normal);
            updateTimer.Interval = new TimeSpan(0, 0, 0, 0, 1000/100);
            updateTimer.Tick += OnUpdateTimer;
            base.OnInitialized(e);
        }

        public void DoEvents()
        {
            DispatcherFrame frame = new DispatcherFrame();
            Dispatcher.CurrentDispatcher.BeginInvoke(DispatcherPriority.Background,
                new DispatcherOperationCallback(ExitFrame), frame);
            Dispatcher.PushFrame(frame);
        }

        public object ExitFrame(object f)
        {
            ((DispatcherFrame)f).Continue = false;

            return null;
        }

        [DllImport("AdvApi32.dll", SetLastError = true)]
        public static extern int RegCloseKey(IntPtr hKey);

        [DllImport("AdvApi32.dll", SetLastError = true)]
        public static extern int RegCreateKeyEx(IntPtr hKey, string lpSubKey, int reserved, string lpClass, int dwOptions, int samDesired, IntPtr lpSecurityAttributes, out IntPtr phkResult, IntPtr lpdwDisposition);
        [DllImport("AdvApi32.dll", SetLastError = true)]
        public static extern int RegSetValueEx(IntPtr hKey, string lpValueName, int reserved, int dwType, ref int lpData, int cbData);


        public int CpuLimitPercent {
            get { return (int)GetValue(CpuLimitPercentProperty); }
            set { SetValue(CpuLimitPercentProperty, value); }
        }


        public double FPS {
            get { return (double)GetValue(FPSProperty); }
            set { SetValue(FPSProperty, value); }
        }
        public double AverageFPS {
            get { return (double)GetValue(AverageFPSProperty); }
            set { SetValue(AverageFPSProperty, value); }
        }

        public double PercentElapsedTimeForComposition {
            get { return (double)GetValue(PercentElapsedTimeForCompositionProperty); }
            set { SetValue(PercentElapsedTimeForCompositionProperty, value); }
        }
        public Geometry PathData {
            get { return (Geometry)GetValue(PathDataProperty); }
            set { SetValue(PathDataProperty, value); }
        }

        public bool IsAttached { get; private set; }
    }

    public class FpsToColorConverter : IValueConverter {
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture) {
            double fps = (double)value;
            return fps < 10 ? Brushes.Red : Brushes.Green;
        }
        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) {
            throw new NotImplementedException();
        }
    }
}
