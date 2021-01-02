using DryIoc;
using Microsoft.Extensions.Logging;
using Prism.Ioc;
using Serilog;
using System;
using System.Linq;

namespace CapFrameX.Extensions
{
	public static class ContainerExtensions
	{
		/// <summary>
		/// Configures the IOC Container for Loggins using Serilog and the ILogger<T> Interface
		/// </summary>
		/// <param name="container"></param>
		/// <param name="loggerConfiguration"></param>
		public static void ConfigureSerilogILogger(this IContainer container, Serilog.ILogger logger)
		{
			var loggerFactory = CreateLoggerFactory(logger);
			container.RegisterInstance(loggerFactory);
			var loggerFactoryMethod = typeof(LoggerFactoryExtensions).GetMethod("CreateLogger", new Type[] { typeof(ILoggerFactory) });
			container.Register(typeof(ILogger<>), made: Made.Of(req => {
				var t = req.ServiceType.GenericTypeArguments.First();
				return loggerFactoryMethod.MakeGenericMethod(t);
			}));
		}

		/// <summary>
		/// Creates the LoggerFactory implementing ILoggerfactory of Microsoft.Extensions.Loggins
		/// </summary>
		/// <returns></returns>
		private static ILoggerFactory CreateLoggerFactory(Serilog.ILogger logger)
		{
			ILoggerFactory loggerFactory = new LoggerFactory();
			loggerFactory.AddSerilog(logger);
			return loggerFactory;
		}
	}
}
