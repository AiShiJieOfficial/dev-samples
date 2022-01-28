using System.Windows;

namespace GazePoint
{
    /// <summary>
    /// App.xaml 的交互逻辑
    /// </summary>
    public partial class App : Application
    {
        internal static PointStreamer streamer;
        protected override void OnStartup(StartupEventArgs e)
        {
            base.OnStartup(e);

            streamer = new PointStreamer();
            streamer.start();
        }

        protected override void OnExit(ExitEventArgs e)
        {
            base.OnExit(e);

            streamer.stop();
        }
    }
}
