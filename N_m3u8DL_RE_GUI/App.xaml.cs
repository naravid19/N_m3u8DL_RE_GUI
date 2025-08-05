using System;
using System.Collections.Generic;
using System.Configuration;
using System.Data;
using System.Globalization;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using N_m3u8DL_RE_GUI.ViewModels;

namespace N_m3u8DL_RE_GUI
{
    /// <summary>
    /// App.xaml 的交互逻辑
    /// </summary>
    public partial class App : System.Windows.Application
    {
        protected override void OnStartup(StartupEventArgs e)
        {
            // Initialize ViewModelLocator
            ViewModelLocator.Initialize();
            
            // Set language
            string loc = "en-US";
            string currLoc = Thread.CurrentThread.CurrentUICulture.Name;
            if (currLoc == "zh-TW" || currLoc == "zh-HK" || currLoc == "zh-MO") loc = "zh-TW";
            else if (currLoc == "zh-CN" || currLoc == "zh-SG") loc = "zh-CN";
            Thread.CurrentThread.CurrentUICulture = CultureInfo.GetCultureInfo(loc);
            
            base.OnStartup(e);
        }

        protected override void OnExit(ExitEventArgs e)
        {
            // Cleanup ViewModelLocator
            ViewModelLocator.Cleanup();
            base.OnExit(e);
        }
    }
}
