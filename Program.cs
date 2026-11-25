using System;
using System.Windows.Forms;
using LibVLCSharp.Shared;

namespace IBOPlayer
{
    internal static class Program
    {
        public static bool VlcAvailable = false;

        [STAThread]
        static void Main()
        {
            Application.EnableVisualStyles();
            Application.SetCompatibleTextRenderingDefault(false);
            try
            {
                Core.Initialize();
                VlcAvailable = true;
            }
            catch (Exception)
            {
                VlcAvailable = false;
            }
            Application.Run(new MainForm());
        }
    }
}
