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

namespace CapFrameX
{
	public class Bootstrapper : PrismBootstrapper
	{
		protected override DependencyObject CreateShell()
		{
			var shell = Container.Resolve<Shell>();
			InitializeShell();
			ConfigureModuleCatalog();
			return shell;
		}

		private void InitializeShell()
		{
			LogAppInfo();

			// get config
			var config = Container.Resolve<CapFrameXConfiguration>();

			// get Shell to set the hardware acceleration
			var shell = Container.Resolve<IShell>();
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
			// Vertical components
			containerRegistry.RegisterSingleton<IEventAggregator, EventAggregator>();
			containerRegistry.Register<IAppConfiguration, CapFrameXConfiguration>();
			containerRegistry.RegisterInstance<IFrametimeStatisticProviderOptions>(Container.Resolve<CapFrameXConfiguration>());
			containerRegistry.ConfigureSerilogILogger(Log.Logger);

			// Prism
			containerRegistry.Register<IRegionManager, RegionManager>();

			// Core components
			containerRegistry.RegisterSingleton<IRecordDirectoryObserver, RecordDirectoryObserver>();
			containerRegistry.RegisterSingleton<IStatisticProvider, FrametimeStatisticProvider>();
			containerRegistry.RegisterSingleton<IFrametimeAnalyzer, FrametimeAnalyzer>();
			containerRegistry.RegisterSingleton<ICaptureService, PresentMonCaptureService>();
			containerRegistry.RegisterSingleton<IRTSSService, RTSSService>();
			containerRegistry.RegisterSingleton<IOverlayService, OverlayService>();
			containerRegistry.RegisterSingleton<IOnlineMetricService, OnlineMetricService>();
			containerRegistry.RegisterSingleton<ISensorService, SensorService>();
			containerRegistry.RegisterSingleton<ISensorConfig, SensorConfig>();
			containerRegistry.RegisterSingleton<IOverlayEntryProvider, OverlayEntryProvider>();
			containerRegistry.RegisterSingleton<IRecordManager, RecordManager>();
			containerRegistry.RegisterSingleton<ISystemInfo, SystemInfo>();
			containerRegistry.RegisterSingleton<IAppVersionProvider, AppVersionProvider>();
			containerRegistry.RegisterInstance<IWebVersionProvider>(new WebVersionProvider());
			containerRegistry.RegisterSingleton<IUpdateCheck, UpdateCheck>();
			containerRegistry.RegisterSingleton<LoginManager>();
			containerRegistry.RegisterSingleton<ICloudManager, CloudManager>();
			containerRegistry.Register<LoginWindow>();
			containerRegistry.RegisterInstance(ProcessList.Create(
				filename: Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments), @"CapFrameX\Resources\Processes.json"),
				appConfiguration: Container.Resolve<IAppConfiguration>()));
			containerRegistry.Register<SoundManager>();
			Container.Resolve<IEventAggregator>().GetEvent<PubSubEvent<AppMessages.OpenLoginWindow>>().Subscribe(_ => {
				var window = Container.Resolve<LoginWindow>();
				window.Show();
			});
			containerRegistry.Register<CaptureManager>();
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

		private void ConfigureModuleCatalog()
		{
			var moduleCatalog = CreateModuleCatalog();
			moduleCatalog.AddModule(typeof(CapFrameXViewRegion));
		}

		private void LogAppInfo()
		{
			var loggerFactory = Container.Resolve<ILoggerFactory>();
			var version = Container.Resolve<IAppVersionProvider>().GetAppVersion().ToString();
			loggerFactory.CreateLogger<ILogger<Bootstrapper>>().LogInformation("CapFrameX {version} started", version);
		}
	}
}
