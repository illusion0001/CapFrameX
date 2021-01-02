using CapFrameX.Data;
using DryIoc;
using Prism.DryIoc;
using Prism.Modularity;
using Prism.Mvvm;
using Prism.Events;
using Prism.Regions;
using System;
using System.Globalization;
using System.Reflection;
using System.Windows;
using CapFrameX.Contracts.Configuration;
using CapFrameX.Configuration;
using CapFrameX.Contracts.PresentMonInterface;
using CapFrameX.PresentMonInterface;
using CapFrameX.Contracts.Data;
using CapFrameX.Contracts.MVVM;
using CapFrameX.Contracts.Overlay;
using CapFrameX.Overlay;
using Serilog;
using Microsoft.Extensions.Logging;
using CapFrameX.Extensions;
using System.IO;
using CapFrameX.Contracts.UpdateCheck;
using CapFrameX.Updater;
using CapFrameX.EventAggregation.Messages;
using CapFrameX.Contracts.Sensor;
using CapFrameX.Sensor;
using CapFrameX.Statistics.NetStandard;
using CapFrameX.Statistics.NetStandard.Contracts;
using CapFrameX.Contracts.RTSS;
using CapFrameX.RTSSIntegration;
using OpenHardwareMonitor.Hardware;
using Prism.Ioc;
using CapFrameX.ViewModel;
using System.Linq;

namespace CapFrameX
{
	public class Bootstrapper : PrismBootstrapper
	{
		protected override DependencyObject CreateShell()
		{
			var shell = Container.Resolve<Shell>();
			Container.GetContainer().UseInstance<IShell>(shell);
			InitializeShell(shell);
			return this.Shell;
		}

		protected override void InitializeShell(DependencyObject obj)
		{
			LogAppInfo();

			if(!(obj is IShell shell))
            {
				throw new ArgumentException("Shell not Implementing IShell");
            }

			this.Shell = obj;
			// get config
			var config = Container.Resolve<CapFrameXConfiguration>();

			shell.IsGpuAccelerationActive = config.IsGpuAccelerationActive;

			Application.Current.MainWindow = (Window)Shell;

			if (config.StartMinimized)
				Application.Current.MainWindow.WindowState = WindowState.Minimized;

			Application.Current.MainWindow.Show();

			if (config.StartMinimized)
				Application.Current.MainWindow.Hide();
		}

		protected override void RegisterTypes(IContainerRegistry containerRegistry)
		{
			var container = containerRegistry.GetContainer();
			// Vertical components
			container.Register<IEventAggregator, EventAggregator>(Reuse.Singleton, null, null, IfAlreadyRegistered.Replace, "EventAggregator");
			container.Register<IAppConfiguration, CapFrameXConfiguration>(Reuse.Singleton);
			container.RegisterInstance<IFrametimeStatisticProviderOptions>(Container.Resolve<CapFrameXConfiguration>());
			container.ConfigureSerilogILogger(Log.Logger);

			// Prism
			container.Register<IRegionManager, RegionManager>(Reuse.Singleton, null, null, IfAlreadyRegistered.Replace, "RegionManager");

			// Core components
			container.Register<IRecordDirectoryObserver, RecordDirectoryObserver>(Reuse.Singleton);
			container.Register<IStatisticProvider, FrametimeStatisticProvider>(Reuse.Singleton);
			container.Register<IFrametimeAnalyzer, FrametimeAnalyzer>(Reuse.Singleton);
			container.Register<ICaptureService, PresentMonCaptureService>(Reuse.Singleton);
			container.Register<IRTSSService, RTSSService>(Reuse.Singleton);
			container.Register<IOverlayService, OverlayService>(Reuse.Singleton);
			container.Register<IOnlineMetricService, OnlineMetricService>(Reuse.Singleton);
			container.Register<ISensorService, SensorService>(Reuse.Singleton);
			container.Register<ISensorConfig, SensorConfig>(Reuse.Singleton);
			container.Register<IOverlayEntryProvider, OverlayEntryProvider>(Reuse.Singleton);
			container.Register<IRecordManager, RecordManager>(Reuse.Singleton);
			container.Register<ISystemInfo, SystemInfo>(Reuse.Singleton);
			container.Register<IAppVersionProvider, AppVersionProvider>(Reuse.Singleton);
			container.RegisterInstance<IWebVersionProvider>(new WebVersionProvider());
			container.Register<IUpdateCheck, UpdateCheck>(Reuse.Singleton);
			container.Register<LoginManager>(Reuse.Singleton);
			container.Register<ICloudManager, CloudManager>(Reuse.Singleton);
			container.Register<LoginWindow>(Reuse.Transient);
			container.RegisterInstance(ProcessList.Create(
				filename: Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments), @"CapFrameX\Resources\Processes.json"),
				appConfiguration: Container.Resolve<IAppConfiguration>()));
			container.Register<SoundManager>(Reuse.Singleton);
			container.Resolve<IEventAggregator>().GetEvent<PubSubEvent<AppMessages.OpenLoginWindow>>().Subscribe(_ => {
				var window = Container.Resolve<LoginWindow>();
				window.Show();
			});
			container.Register<CaptureManager>(Reuse.Singleton);
		}

		/// <summary>
		/// https://www.c-sharpcorner.com/forums/using-ioc-with-wpf-prism-in-mvvm
		/// </summary>
		protected override void ConfigureViewModelLocator()
		{
			base.ConfigureViewModelLocator();

			ViewModelLocationProvider.SetDefaultViewTypeToViewModelTypeResolver(viewType =>
			{
				var viewName = viewType.FullName;

				// Naming convention
				viewName = viewName.Replace(".View.", ".ViewModel.");
				viewName = viewName.Replace(".Views.", ".ViewModels.");
				var viewAssemblyName = viewType.GetTypeInfo().Assembly.FullName;

				// Saving ViewModels in another assembly.
				viewAssemblyName = viewAssemblyName.Replace("View", "ViewModel");
				var suffix = viewName.EndsWith("View", StringComparison.Ordinal) ? "Model" : "ViewModel";
				var viewModelName = string.Format(CultureInfo.InvariantCulture, "{0}{1}, {2}", viewName, suffix, viewAssemblyName);
				var t = Type.GetType(viewModelName);
				return t;
			});

			ViewModelLocationProvider.SetDefaultViewModelFactory(type => Container.Resolve(type));
		}

		protected override IModuleCatalog CreateModuleCatalog()
		{
			var moduleCatalog = base.CreateModuleCatalog();
			moduleCatalog.AddModule(typeof(CapFrameXViewRegion));
			return moduleCatalog;
		}

		private void LogAppInfo()
		{
			var loggerFactory = Container.Resolve<ILoggerFactory>();
			var version = Container.Resolve<IAppVersionProvider>().GetAppVersion().ToString();
			loggerFactory.CreateLogger<ILogger<Bootstrapper>>().LogInformation("CapFrameX {version} started", version);
		}
	}
}
