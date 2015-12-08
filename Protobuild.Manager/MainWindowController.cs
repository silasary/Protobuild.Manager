﻿using System;
using System.Collections.Generic;
using System.Linq;
using Foundation;
using AppKit;
using System.Threading;
using WebKit;
using System.Web;

namespace Protobuild.Manager
{
	public partial class MainWindowController : NSWindowController, IWebUIDelegate
    {
		private IBrandingEngine _brandingEngine;

		private RuntimeServer _runtimeServer;

		private IAppHandlerManager _appManagerHandler;

        #region Constructors

        // Called when created from unmanaged code
        public MainWindowController(IntPtr handle) : base(handle)
        {
            Initialize();
        }

        // Called when created directly from a XIB file
        [Export("initWithCoder:")]
        public MainWindowController(NSCoder coder) : base(coder)
        {
            Initialize();
        }

        // Call to load from the XIB/NIB file
		[LightweightKernelInjectionPreferred]
        public MainWindowController() : base("MainWindow")
        {
            Initialize();
        }

        // Shared initialization code
        void Initialize()
        {
			_brandingEngine = MacOSUIManager.KernelReference.Get<IBrandingEngine>();
			_runtimeServer = MacOSUIManager.KernelReference.Get<RuntimeServer>();
			_appManagerHandler = MacOSUIManager.KernelReference.Get<IAppHandlerManager>();
        }

        #endregion

        public override void AwakeFromNib()
        {
            base.AwakeFromNib();

            this.Window.BackgroundColor = NSColor.Black;
			this.Window.Title = _brandingEngine.ProductName;

			this.Window.MaxSize = new CoreGraphics.CGSize(720, 400);
			this.Window.MinSize = new CoreGraphics.CGSize(720, 400);
			this.Window.ContentMaxSize = new CoreGraphics.CGSize(720, 400);
			this.Window.SetContentSize(new CoreGraphics.CGSize(720, 400));

			this.WebViewOutlet.DrawsBackground = true;

			_runtimeServer.RegisterRuntimeInjector(x => 
				this.WebViewOutlet.InvokeOnMainThread(() => this.WebViewOutlet.StringByEvaluatingJavaScriptFromString(x)));

			this.WebViewOutlet.CommitedLoad += (o, a) => this.Window.Title = _brandingEngine.ProductName;
			this.WebViewOutlet.FinishedLoad += (o, a) => this.Window.Title = _brandingEngine.ProductName;

			this.WebViewOutlet.UIDelegate = this; 

			this.WebViewOutlet.MainFrame.LoadRequest(new NSUrlRequest(new NSUrl(_runtimeServer.BaseUri)));

			this.WebViewOutlet.DecidePolicyForNavigation += (o, a) => {
				var url = a.Request.Url.ToString();
				var uri = new Uri(url);

				if (uri.Scheme != "app")
				{
					WebView.DecideUse(a.DecisionToken);
					return;
				}

				_appManagerHandler.Handle(uri.AbsolutePath, HttpUtility.ParseQueryString(uri.Query));

				WebView.DecideIgnore(a.DecisionToken);
			};
        }

		[Export("webView:addMessageToConsole:")]
		public void AddMessageToConsole(WebView wv, NSDictionary message)
		{
			var url = message["sourceURL"];
			var line = message["lineNumber"];
			var err = message["message"];

			Console.Error.WriteLine(url + ":" + line + ": " + err);
		}

        //strongly typed window accessor
        public new MainWindow Window
        {
            get
            {
                return (MainWindow)base.Window;
            }
        }
    }
}

