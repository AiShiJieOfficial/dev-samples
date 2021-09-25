using System;
using System.Collections.Generic;
using System.Configuration;
using System.Data;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;

namespace HeadPointer
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
