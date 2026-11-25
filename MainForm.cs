using System;
using System.Drawing;
using System.IO;
using System.Reflection;
using System.Threading.Tasks;
using System.Windows.Forms;
using Microsoft.Web.WebView2.Core;
using Microsoft.Web.WebView2.WinForms;

namespace ZaghlolPlayer
{
    public partial class MainForm : Form
    {
        private readonly WebView2 _web;
        private Bridge? _bridge;
        public static MainForm? Instance { get; private set; }

        public MainForm()
        {
            Instance = this;
            Text = "Zaghlol Player";
            MinimumSize = new Size(1120, 720);
            Size = new Size(1280, 800);
            StartPosition = FormStartPosition.CenterScreen;
            BackColor = Color.FromArgb(11, 14, 26);
            try
            {
                using var s = Assembly.GetExecutingAssembly().GetManifestResourceStream("ZaghlolPlayer.z.ico");
                if (s != null) Icon = new Icon(s);
            }
            catch { /* icon is cosmetic */ }

            _web = new WebView2 { Dock = DockStyle.Fill };
            Controls.Add(_web);
            Load += async (_, __) => await InitWebAsync();
            FormClosing += (_, __) => { try { _web.Dispose(); } catch { } };
        }

        private async Task InitWebAsync()
        {
            try
            {
                var userData = Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                    "ZaghlolPlayer", "WebView2");
                // --disable-web-security lets the in-app HLS player fetch streams from
                // any IPTV server (most panels send no CORS headers). The WebView only
                // ever loads our own embedded UI, never arbitrary web pages.
                var options = new CoreWebView2EnvironmentOptions(
                    "--disable-web-security --autoplay-policy=no-user-gesture-required");
                var env = await CoreWebView2Environment.CreateAsync(null, userData, options);
                await _web.EnsureCoreWebView2Async(env);
                _bridge = new Bridge(this);
                _web.CoreWebView2.AddHostObjectToScript("zaghlol", _bridge);
                _web.CoreWebView2.Settings.AreDefaultContextMenusEnabled = false;
                _web.CoreWebView2.Settings.AreDevToolsEnabled = false;
                _web.CoreWebView2.Settings.IsStatusBarEnabled = false;
                _web.CoreWebView2.NavigateToString(LoadUiHtml());
            }
            catch (Exception ex)
            {
                MessageBox.Show(
                    "تعذر تشغيل واجهة Zaghlol Player.\nتأكد من تثبيت Microsoft Edge WebView2 Runtime (موجود افتراضياً في Windows 11).\n\n" + ex.Message,
                    "Zaghlol Player", MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
        }

        private static string LoadUiHtml()
        {
            try
            {
                using var s = Assembly.GetExecutingAssembly().GetManifestResourceStream("ZaghlolPlayer.ui.html");
                if (s == null) return "<html><body style='background:#0b0e1a;color:#fff'>UI missing</body></html>";
                using var r = new StreamReader(s);
                return r.ReadToEnd();
            }
            catch
            {
                return "<html><body style='background:#0b0e1a;color:#fff'>UI load failed</body></html>";
            }
        }

        /// <summary>Shows an Open File dialog on the UI thread and returns the chosen path ("" if cancelled).</summary>
        public string ShowOpenFileDialog(string filter, string title)
        {
            string result = "";
            if (InvokeRequired)
            {
                Invoke(new Action(() => { result = ShowOpenFileDialog(filter, title); }));
                return result;
            }
            using var d = new OpenFileDialog { Filter = filter, Title = title };
            if (d.ShowDialog(this) == DialogResult.OK) result = d.FileName;
            return result;
        }
    }
}
