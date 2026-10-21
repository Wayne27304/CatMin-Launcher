using System;
using System.Windows;

namespace CatMin
{
    public partial class App : Application
    {
        protected override void OnStartup(StartupEventArgs e)
        {
            // 捕捉所有未處理的異常，防止程式閃退並顯示原因
            this.DispatcherUnhandledException += (s, ex) =>
            {
                MessageBox.Show($"啟動器發生錯誤:\n{ex.Exception.Message}\n\n詳細資訊:\n{ex.Exception.StackTrace}", "錯誤");
                ex.Handled = true;
                Shutdown();
            };

            base.OnStartup(e);
        }
    }
}