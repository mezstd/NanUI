﻿using NetDimension.Formium.Browser;
using NetDimension.Formium.Window;
using NetDimension.WinForm;
using System;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;
using System.Text;
using System.Windows.Forms;

namespace NetDimension.Formium
{
    public enum HostWindowType
    {
        Standard,
        Borderless,
        UserCustom
    }
    public abstract class HostWindow : IWebBrowser
    {
        const string JS_EVENT_RAISER_NAME = "__nanui_raiseHostWindowEvent";
        private readonly ChromeWidgetMessageInterceptor chromeWidgetMessageInterceptor = null;

        const string ABOUT_BLANK = "about:blank";
        abstract public string StartUrl { get; }
        abstract public HostWindowType WindowType { get; }
        private string title = "NanUI Host Window";
        private string subtitle = string.Empty;



        public string Title
        {
            get => title;
            set
            {
                title = value;
                RaiseTitleChanged();
            }
        }

        public string Subtitle
        {
            get => subtitle;
            set
            {
                subtitle = value;
                RaiseTitleChanged();

            }
        }

        protected abstract Control LaunchScreen { get; }
        internal protected BrowserCore WebBrowser => BrowserControl.WebBrowserCore;
        protected IntPtr BrowserHandle { get => WebBrowser?.BrowserHost?.OpenerWindowHandle ?? IntPtr.Zero; }
        protected IntPtr ContainerFormHandle { get; private set; }
        protected IntPtr BrowserControlHandle { get; private set; }
        public Form ContainerForm { get; }
        private HostBrowserControl BrowserControl { get; }
        private bool isFirstTimeRun = true;
        private Dictionary<string, string> delayedScripts = new Dictionary<string, string>();

        public BrowserCore GetWebBrowserCore()
        {
            return WebBrowser;
        }

        #region IWebBrowser
        public bool IsLoading => WebBrowser?.Browser?.IsLoading ?? false;
        public bool CanGoBack => WebBrowser?.Browser?.CanGoBack ?? false;
        public bool CanGoForward => WebBrowser?.Browser?.CanGoForward ?? false;

        public void GoBack()
        {
            WebBrowser?.Browser?.GoBack();
        }

        public void GoForward()
        {
            WebBrowser?.Browser?.GoForward();
        }

        public void LoadUrl(string url)
        {
            WebBrowser?.NavigateToUrl(url);
        }

        public void LoadString(string stringVal, string url)
        {
            WebBrowser?.LoadString(stringVal, url);
        }

        public void LoadString(string stringVal)
        {
            WebBrowser?.LoadString(stringVal, ABOUT_BLANK);
        }
        #endregion

        private bool isFormLoad = false;
        public bool EnableDragRegion { get; set; }


        public HostWindow()
        {
            BrowserControl = new HostBrowserControl(this) { Dock = DockStyle.Fill, Margin = new Padding(0), Padding = new Padding(0) };

            switch (WindowType)
            {
                case HostWindowType.Standard:
                    ContainerForm = new StandardHostWindow() { Width = 720, Height = 480, AutoScaleMode = AutoScaleMode.Dpi };
                    ContainerForm.SuspendLayout();
                    ContainerForm.Controls.Add(BrowserControl);
                    ContainerForm.ResumeLayout(false);
                    break;
                case HostWindowType.Borderless:
                    EnableDragRegion = true;
                    ContainerForm = new BorderlessHostWindow() { Width = 720, Height = 480, AutoScaleMode = AutoScaleMode.None }; ;
                    ContainerForm.SuspendLayout();
                    ContainerForm.Controls.Add(BrowserControl);
                    ContainerForm.ResumeLayout(false);
                    break;
                case HostWindowType.UserCustom:
                    ContainerForm = CreateCustomContainer(BrowserControl);
                    break;
            }

            BrowserControl.SendToBack();

            if (LaunchScreen != null)
            {
                LaunchScreen.Dock = DockStyle.Fill;
                ContainerForm.Controls.Add(LaunchScreen);
                LaunchScreen.BringToFront();
            }

            ContainerForm.Load += FormLoad;
            
            BrowserControl.HandleCreated += BrowserControlHandleCreated;
        }



        private void BrowserControlHandleCreated(object sender, EventArgs e)
        {
            BrowserControlHandle = BrowserControl.Handle;
            ContainerFormHandle = ContainerForm.Handle;
        }

        FormWindowState windowLastState;

        private void FormLoad(object sender, EventArgs e)
        {
            switch (WindowType)
            {
                case HostWindowType.Standard:
                    SetStandardWindowStyle((StandardHostWindow)ContainerForm);
                    break;
                case HostWindowType.Borderless:
                    SetBorderlessWindowStyle((BorderlessHostWindow)ContainerForm);

                    break;
            }

            ContainerForm.Text = GetWindowTitle();
            RegisterHostWindowJavascriptEventHandler();
            windowLastState = ContainerForm.WindowState;
            isFormLoad = true;

            
        }

        private void RegisterHostWindowJavascriptEventHandler()
        {
            ContainerForm.Activated += (_, args) =>
            {
                var sb = new StringBuilder();
                sb.AppendLine("(function(){");

                sb.AppendLine($@"{JS_EVENT_RAISER_NAME}(""hostactived"", {{}});");
                sb.AppendLine($@"{JS_EVENT_RAISER_NAME}(""hostactivestatechange"", {{ actived: true }});");

                sb.AppendLine("})();");

                if (WebBrowser == null || !WebBrowser.IsMainFrameLoaded)
                {
                    delayedScripts["hostactivestatechange"] = sb.ToString();
                }

                WebBrowser?.ExecuteJavascript(sb.ToString());
            };

            ContainerForm.Deactivate += (_, args) =>
            {
                var sb = new StringBuilder();
                sb.AppendLine("(function(){");

                sb.AppendLine($@"{JS_EVENT_RAISER_NAME}(""hostdeactivate"", {{}});");
                sb.AppendLine($@"{JS_EVENT_RAISER_NAME}(""hostactivestatechange"", {{ actived: false }});");

                sb.AppendLine("})();");

                WebBrowser?.ExecuteJavascript(sb.ToString());
            };

            ContainerForm.Resize += (_, args) =>
            {
                var name = ContainerForm?.WindowState.ToString().ToLower();
                var state = ((int?)ContainerForm?.WindowState ?? 0);

                var rect = new RECT();
                var isMaximized = User32.IsZoomed(ContainerFormHandle);

                if (isMaximized)
                {
                    User32.GetClientRect(ContainerFormHandle, ref rect);
                }
                else
                {
                    User32.GetWindowRect(ContainerFormHandle, ref rect);
                }

                var sb = new StringBuilder();
                sb.AppendLine("(function(){");

                if (windowLastState != ContainerForm.WindowState)
                {
                    sb.AppendLine($@"{JS_EVENT_RAISER_NAME}(""hoststatechange"", {{ state: ""{name}"", code: {state} }});");
                    windowLastState = ContainerForm.WindowState;
                }

                sb.AppendLine($@"{JS_EVENT_RAISER_NAME}(""hostsizechange"", {{ width:{rect.Width}, height:{rect.Height} }});");
                sb.AppendLine("})();");

                if (WebBrowser == null || !WebBrowser.IsMainFrameLoaded)
                {
                    delayedScripts["hostsizechange"] = sb.ToString();
                }

                WebBrowser?.ExecuteJavascript(sb.ToString());
            };
        }

        internal void RaiseWebBrowserControlCreated()
        {
            ChromeWidgetMessageInterceptor.Setup(chromeWidgetMessageInterceptor, WebBrowser, OnWebBrowserMessage);

            ContainerForm.FormClosed += (_, e) => WebBrowser?.Close();

            WebBrowser.RemoteBrowserCreated += (_, e) =>
            {
                RegisterHostWindowJavascriptExtension();
            };



            WebBrowser.OnV8ContextCreated += (_, e) =>
            {
                e.Browser.MainFrame.ExecuteJavaScript(Properties.Resources.FrameGlobalRegistrationScript, null, 0);
                OnJavascriptContextReady(e.Context, e.Frame);
            };

            BrowserControl.DragHandler.OnDraggableRegionsChanged += BrowserOnDraggableRegionsChanged;

            BrowserControl.LoadHandler.OnLoadEnd += (_, args) =>
            {
                if (isFirstTimeRun && args.Frame.IsMain)
                {
                    ContainerForm.InvokeIfRequired(() =>
                    {

                        if (LaunchScreen != null)
                        {
                            ContainerForm.Controls.Remove(LaunchScreen);
                        }

                        BrowserControl.Enabled = true;
                        BrowserControl.BringToFront();
                    });

                    isFirstTimeRun = false;

                    foreach (var script in delayedScripts)
                    {
                        WebBrowser.ExecuteJavascript(script.Value);
                    }

                }
            };

            RaiseBrowserReady(BrowserControl, WebBrowser.GlobalObject);


        }

        private void RegisterHostWindowJavascriptExtension()
        {
            hostWindowExtension = new HostWindowJavascriptExtension(this);
            Chromium.Remote.CfrRuntime.RegisterExtension("Formium/HostWindow", hostWindowExtension.DefinitionJavascriptCode, hostWindowExtension);
        }

        protected Region DraggableRegion
        {
            get;
            private set;
        }

        private float oldScaleFactor = 0f;
        private float scaleFactor = 1.0f;
        private HostWindowJavascriptExtension hostWindowExtension;

        protected bool Resizable => ContainerForm.FormBorderStyle == FormBorderStyle.SizableToolWindow || ContainerForm.FormBorderStyle == FormBorderStyle.Sizable;


        private void BrowserOnDraggableRegionsChanged(object sender, Chromium.Event.CfxOnDraggableRegionsChangedEventArgs e)
        {
            scaleFactor = User32.GetOriginalDeviceScaleFactor(ContainerFormHandle);

            if (scaleFactor != oldScaleFactor)
            {

                if (DraggableRegion != null)
                {
                    DraggableRegion.Dispose();
                    DraggableRegion = null;
                }
                oldScaleFactor = scaleFactor;
            }

            var regions = e.Regions;

            if (regions.Length > 0)
            {
                foreach (var region in regions)
                {
                    var rect = new Rectangle((int)(region.Bounds.X * scaleFactor), (int)(region.Bounds.Y * scaleFactor), (int)(region.Bounds.Width * scaleFactor), (int)(region.Bounds.Height * scaleFactor));
                    if (DraggableRegion == null)
                    {
                        DraggableRegion = new Region(rect);
                    }
                    else
                    {
                        if (region.Draggable)
                        {
                            DraggableRegion.Union(rect);
                        }
                        else
                        {
                            DraggableRegion.Exclude(rect);
                        }
                    }
                }
            }

        }

        private bool OnWebBrowserMessage(Message m)
        {
            if (WindowType != HostWindowType.Borderless)
                return false;

            var msg = (WindowsMessages)m.Msg;


            var handled = false;

            if (isFormLoad)
            {
                switch (msg)
                {
                    case WindowsMessages.WM_LBUTTONDOWN:
                        handled = OnWMLButtonDown(m);
                        break;
                    case WindowsMessages.WM_MOUSEMOVE:
                        if (Resizable)
                        {
                            handled = OnWMMouseMove(m);
                        }
                        break;
                }
            }



            return handled;
        }



        private bool OnWMMouseMove(Message m)
        {
            var point = Win32.GetPostionFromPtr(m.LParam);
            User32.ClientToScreen(BrowserControlHandle, ref point);

            User32.PostMessage(ContainerFormHandle, (uint)DefMessages.WM_BROWSER_MOUSE_MOVE, IntPtr.Zero, Win32.MakeParam((IntPtr)point.x, (IntPtr)point.y));

            return false;

        }

        private bool OnWMLButtonDown(Message m)
        {
            var point = Win32.GetPostionFromPtr(m.LParam);

            var inDragableArea = (DraggableRegion != null && DraggableRegion.IsVisible(new Point(point.x, point.y)));



            if (EnableDragRegion && inDragableArea && !(ContainerForm.FormBorderStyle == FormBorderStyle.None && ContainerForm.WindowState == FormWindowState.Maximized))
            {
                User32.PostMessage(ContainerFormHandle, (uint)DefMessages.WM_DRAG_APP_REGION, IntPtr.Zero, IntPtr.Zero);
                return true;
            }
            //else if (WindowType == HostWindowType.Borderless && Resizable)
            //{
            //    var form = (BorderlessHostWindow)ContainerForm;

            //    User32.ClientToScreen(BrowserControlHandle, ref point);


            //    var mode = form.GetSizeMode(ContainerFormHandle, point);

            //    if (mode != HitTest.HTNOWHERE)
            //    {
            //        User32.PostMessage(ContainerFormHandle, (uint)DefMessages.WM_BROWSER_MOUSE_LBUTTON_DOWN, IntPtr.Zero, Win32.MakeParam((IntPtr)point.x, (IntPtr)point.y));

            //        return true;
            //    }

            //}



            return false;
        }

        private void SetStandardWindowStyle(IStandardHostWindowStyle style)
        {
            OnStandardFormStyle(style);
        }

        private void SetBorderlessWindowStyle(IBorderlessHostWindowStyle style)
        {
            OnBorderlessFormStyle(style);
        }

        protected virtual void OnStandardFormStyle(IStandardHostWindowStyle style)
        {

        }

        protected virtual void OnBorderlessFormStyle(IBorderlessHostWindowStyle style)
        {

        }

        private void RaiseTitleChanged()
        {
            if (!isFormLoad) return;
            ContainerForm.InvokeIfRequired(() =>
            {
                if (ContainerForm != null)
                {
                    ContainerForm.Text = GetWindowTitle();
                }
            });
        }
        protected virtual string GetWindowTitle()
        {
            if (string.IsNullOrEmpty(subtitle))
            {
                return title;
            }
            else if (string.IsNullOrEmpty(title))
            {
                return subtitle;

            }
            else
            {
                return $"{subtitle} | {title}";
            }
        }

        protected virtual Form CreateCustomContainer(HostBrowserControl browserControl)
        {
            var form = new Form();
            form.Controls.Add(browserControl);
            return form;
        }
        abstract protected void OnHostWindowReady(IWebBrowserHandler browserClient);
        abstract protected void OnRegisterGlobalObject(JSObject global);

        private void RaiseBrowserReady(IWebBrowserHandler browserClient, JSObject global)
        {
            OnHostWindowReady(browserClient);
            OnRegisterGlobalObject(global);
        }

        protected virtual void OnJavascriptContextReady(Chromium.Remote.CfrV8Context context, Chromium.Remote.CfrFrame frame)
        {

        }

        public void Show()
        {
            ContainerForm.Show();
        }

        public void Show(IWin32Window owner)
        {
            ContainerForm.Show(owner);
        }

        public void Show(HostWindow owner)
        {
            ContainerForm.Show(owner.ContainerForm);
        }

        public void ShowDialog()
        {
            ContainerForm.ShowDialog();
        }

        public void ShowDialog(IWin32Window owner)
        {
            ContainerForm.ShowDialog(owner);
        }

        public void ShowDialog(HostWindow owner)
        {
            ContainerForm.ShowDialog(owner.ContainerForm);
        }

        public void Close()
        {
            ContainerForm.Close();
        }

        public void Hide()
        {
            ContainerForm.Hide();
        }
    }

    //public class CustomContainerFormStyleEventArgs : EventArgs
    //{
    //    public IStandardHostWindowStyle StandardWindowStyle { get; }
    //    public IBorderlessHostWindowStyle BorderlessWindowStyle { get; }
    //    internal CustomContainerFormStyleEventArgs(IStandardHostWindowStyle style1, IBorderlessHostWindowStyle style2)
    //    {
    //        StandardWindowStyle = style1;
    //        BorderlessWindowStyle = style2;
    //    }
    //}

    //public class OnCustomizeStandardWindowStyleEventArgs : EventArgs
    //{
    //    public IStandardHostWindowStyle WindowStyle { get; }
    //    public OnCustomizeStandardWindowStyleEventArgs(IStandardHostWindowStyle style)
    //    {
    //        WindowStyle = style;
    //    }
    //}

    //public class OnCustomizeBorderlessWindowStyleEventArgs : EventArgs
    //{
    //    public IBorderlessHostWindowStyle WindowStyle { get; }
    //    public OnCustomizeBorderlessWindowStyleEventArgs(IBorderlessHostWindowStyle style)
    //    {
    //        WindowStyle = style;
    //    }
    //}


    public class OnBrowserReadyEventArgs : EventArgs
    {
        internal OnBrowserReadyEventArgs(IWebBrowserHandler browserHandler, JSObject global)
        {
            this.ChromiumBrowserHandlers = browserHandler;
            this.Global = global;
        }

        public IWebBrowserHandler ChromiumBrowserHandlers { get; }
        public JSObject Global { get; }
    }

    public static class FormControlExtensions
    {
        public static void InvokeIfRequired(this Control control, Action action)
        {
            if (control.InvokeRequired)
            {
                control.Invoke(new MethodInvoker(action));
            }
            else
            {
                action();
            }
        }
    }
}
