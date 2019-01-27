﻿using CapFrameX.Contracts.Configuration;
using CapFrameX.EventAggregation.Messages;
using CapFrameX.Extensions;
using CapFrameX.OcatInterface;
using CapFrameX.Statistics;
using LiveCharts;
using LiveCharts.Geared;
using LiveCharts.Wpf;
using MathNet.Numerics.Statistics;
using Prism.Commands;
using Prism.Events;
using Prism.Mvvm;
using Prism.Regions;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows.Input;
using System.Windows.Media;

namespace CapFrameX.ViewModel
{
	public class SynchronizationViewModel : BindableBase, INavigationAware
	{
		private readonly IStatisticProvider _frametimeStatisticProvider;
		private readonly IEventAggregator _eventAggregator;
		private readonly IAppConfiguration _appConfiguration;

		private ZoomingOptions _zoomingMode;
		private SeriesCollection _frameDisplayTimesCollection;
		private SeriesCollection _histogramCollection;
		private SeriesCollection _droppedFramesStatisticCollection;
		private string[] _droppedFramesLabels;
		private string[] _histogramLabels;
		private bool _useUpdateSession;
		private Session _session;
		private OcatRecordInfo _recordInfo;

		/// <summary>
		/// https://docs.microsoft.com/en-us/dotnet/standard/base-types/standard-numeric-format-strings
		/// </summary>
		public Func<double, string> HistogramFormatter { get; } = value => value.ToString("N");

		public Func<ChartPoint, string> PieChartPointLabel { get; } = chartPoint =>
				string.Format("{0} ({1:P})", chartPoint.Y, chartPoint.Participation);

		public SeriesCollection FrameDisplayTimesCollection
		{
			get { return _frameDisplayTimesCollection; }
			set
			{
				_frameDisplayTimesCollection = value;
				RaisePropertyChanged();
			}
		}

		public SeriesCollection HistogramCollection
		{
			get { return _histogramCollection; }
			set
			{
				_histogramCollection = value;
				RaisePropertyChanged();
			}
		}

		public SeriesCollection DroppedFramesStatisticCollection
		{
			get { return _droppedFramesStatisticCollection; }
			set
			{
				_droppedFramesStatisticCollection = value;
				RaisePropertyChanged();
			}
		}

		public string[] DroppedFramesLabels
		{
			get { return _droppedFramesLabels; }
			set
			{
				_droppedFramesLabels = value;
				RaisePropertyChanged();
			}
		}

		public string[] HistogramLabels
		{
			get { return _histogramLabels; }
			set
			{
				_histogramLabels = value;
				RaisePropertyChanged();
			}
		}

		public ZoomingOptions ZoomingMode
		{
			get { return _zoomingMode; }
			set
			{
				_zoomingMode = value;
				RaisePropertyChanged();
			}
		}

		public ICommand ToogleZoomingModeCommand { get; }

		public ICommand CopyDisplayChangeTimeValuesCommand { get; }

		public ICommand CopyHistogramDataCommand { get; }

		public SynchronizationViewModel(IStatisticProvider frametimeStatisticProvider,
										IEventAggregator eventAggregator,
										IAppConfiguration appConfiguration)
		{
			_frametimeStatisticProvider = frametimeStatisticProvider;
			_eventAggregator = eventAggregator;
			_appConfiguration = appConfiguration;

			ToogleZoomingModeCommand = new DelegateCommand(OnToogleZoomingMode);
			CopyDisplayChangeTimeValuesCommand = new DelegateCommand(OnCopyDisplayChangeTimeValues);
			CopyHistogramDataCommand = new DelegateCommand(CopyHistogramData);

			ZoomingMode = ZoomingOptions.Y;

			SubscribeToUpdateSession();
		}

		private void SubscribeToUpdateSession()
		{
			_eventAggregator.GetEvent<PubSubEvent<ViewMessages.UpdateSession>>()
							.Subscribe(msg =>
							{
								if (_useUpdateSession)
								{
									_session = msg.OcatSession;
									_recordInfo = msg.RecordInfo;

									// Do update actions
									UpdateCharts();
								}
							});
		}

		private void OnToogleZoomingMode()
		{
			switch (ZoomingMode)
			{
				case ZoomingOptions.None:
					ZoomingMode = ZoomingOptions.X;
					break;
				case ZoomingOptions.X:
					ZoomingMode = ZoomingOptions.Y;
					break;
				case ZoomingOptions.Y:
					ZoomingMode = ZoomingOptions.Xy;
					break;
				case ZoomingOptions.Xy:
					ZoomingMode = ZoomingOptions.None;
					break;
				default:
					throw new ArgumentOutOfRangeException();
			}
		}

		private void OnCopyDisplayChangeTimeValues()
		{
			throw new NotImplementedException();
		}

		private void CopyHistogramData()
		{
			throw new NotImplementedException();
		}

		private void UpdateCharts()
		{
			if (_session == null)
				return;

			SetFrameDisplayTimesChart(_session.FrameTimes, _session.Displaytimes);
			SetHistogramChart(_session.Displaytimes);
			SetDroppedFramesChart(_session.AppMissed);
		}

		private void SetFrameDisplayTimesChart(List<double> frametimes, List<double> displaytimes)
		{
			var frametimeValues = new GearedValues<double>();
			frametimeValues.AddRange(frametimes);
			frametimeValues.WithQuality(_appConfiguration.ChartQualityLevel.ConverToEnum<Quality>());

			FrameDisplayTimesCollection = new SeriesCollection
			{
				new GLineSeries
				{
					Title = "Frametimes",
					Values = frametimeValues,
					Fill = Brushes.Transparent,
					Stroke = new SolidColorBrush(Color.FromRgb(139, 35, 35)),
					StrokeThickness = 1,
					LineSmoothness = 0,
					PointGeometrySize = 0
				}
			};

			if (displaytimes.Any())
			{
				var displaytimeValues = new GearedValues<double>();
				displaytimeValues.AddRange(displaytimes);
				displaytimeValues.WithQuality(_appConfiguration.ChartQualityLevel.ConverToEnum<Quality>());

				FrameDisplayTimesCollection.Add(new GLineSeries
				{
					Title = "Display changed times",
					Values = displaytimeValues,
					Fill = Brushes.Transparent,
					Stroke = new SolidColorBrush(Color.FromArgb(128, 35, 139, 123)),
					StrokeThickness = 1,
					LineSmoothness = 0,
					PointGeometrySize = 0
				});
			}
		}

		private void SetHistogramChart(List<double> displaytimes)
		{
			var optimalBinCount = _frametimeStatisticProvider.GetOptimalHistogramBinCount(displaytimes);
			var histogram = new Histogram(displaytimes, optimalBinCount);

			var histogramValues = new ChartValues<double>();
			var bins = new List<double>();
			for (int i = 0; i < optimalBinCount; i++)
			{
				var bucket = histogram[i];
				histogramValues.Add(bucket.Count);
				bins.Add(bucket.LowerBound);
			}

			HistogramCollection = new SeriesCollection
			{
				new ColumnSeries
				{
					Title = "Display changed time distribution",
					Values = histogramValues,
					// Kind of pink
					Fill = new SolidColorBrush(Color.FromRgb(139, 35, 102)),
					DataLabels = true,
				}
			};

			HistogramLabels = bins.Select(bin => Math.Round(bin, 2).ToString()).ToArray();
		}

		private void SetDroppedFramesChart(List<bool> appMissed)
		{
			if (!appMissed.Any())
				return;

			DroppedFramesStatisticCollection = new SeriesCollection()
			{
				new PieSeries
				{
					Title = "Synced frames",
					Values = new ChartValues<int>(){ appMissed.Count(flag => flag == false) },
					DataLabels = true,
					Foreground = Brushes.Black,
					LabelPosition=PieLabelPosition.InsideSlice,
					LabelPoint = PieChartPointLabel,
				},
				new PieSeries
				{
					Title = "Dropped frames",
					Values = new ChartValues<int>(){ appMissed.Count(flag => flag == true) },
					DataLabels = true,
					Foreground = Brushes.Black,
					LabelPosition=PieLabelPosition.InsideSlice,
					LabelPoint = PieChartPointLabel,
				}
			};
		}

		public void OnNavigatedTo(NavigationContext navigationContext)
		{
			_useUpdateSession = true;
		}

		public bool IsNavigationTarget(NavigationContext navigationContext)
		{
			return true;
		}

		public void OnNavigatedFrom(NavigationContext navigationContext)
		{
			_useUpdateSession = false;
		}
	}
}