﻿using CapFrameX.Capture.Contracts;
using CapFrameX.Contracts.Configuration;
using CapFrameX.Contracts.Overlay;
using CapFrameX.EventAggregation.Messages;
using CapFrameX.PMD;
using CapFrameX.Statistics.NetStandard.Contracts;
using Microsoft.Extensions.Logging;
using Prism.Events;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Reactive.Concurrency;
using System.Reactive.Linq;

namespace CapFrameX.PresentMonInterface
{
    public class OnlineMetricService : IOnlineMetricService
    {
		private const double STUTTERING_THRESHOLD = 2d;
        private const int LIST_CAPACITY = 20000;
        private const int PMD_BUFFER_CAPACITY = 2200;

        private readonly object _currentProcessLock = new object();

        private readonly IStatisticProvider _frametimeStatisticProvider;
        private readonly ICaptureService _captureService;
        private readonly IEventAggregator _eventAggregator;
        private readonly IOverlayEntryCore _overlayEntryCore;
        private readonly IPmdService _pmdService;
        private readonly ILogger<OnlineMetricService> _logger;
        private readonly IAppConfiguration _appConfiguration;
        private readonly object _lockRealtimeMetric = new object();
        private readonly object _lock5SecondsMetric = new object();
        private readonly object _lockPmdMetrics = new object();
        private List<double> _frametimesRealtimeSeconds = new List<double>(LIST_CAPACITY);
		private List<double> _gpuActiveTimesRealtimeSeconds = new List<double>(LIST_CAPACITY);
        private List<double> _cpuActiveTimesRealtimeSeconds = new List<double>(LIST_CAPACITY);
        private List<double> _frametimes5Seconds = new List<double>(LIST_CAPACITY / 4);
        private List<double> _measuretimesRealtimeSeconds = new List<double>(LIST_CAPACITY);
        private List<double> _measuretimes5Seconds = new List<double>(LIST_CAPACITY / 4);
        private List<PmdChannel[]> _channelDataBuffer = new List<PmdChannel[]>(PMD_BUFFER_CAPACITY);
        private string _currentProcess;
        private int _currentProcessId;
        private double _droppedFrametimes = 0.0;
        private double _prevDisplayedFrameInputLagTime = double.NaN;
        private readonly double _maxOnlineStutteringIntervalLength = 5d;

        private int MetricInterval => _appConfiguration.MetricInterval == 0 ? 20 : _appConfiguration.MetricInterval;

        public OnlineMetricService(IStatisticProvider frametimeStatisticProvider,
            ICaptureService captureServive,
            IEventAggregator eventAggregator,
            IOverlayEntryCore oerlayEntryCore,
            IPmdService pmdService,
            ILogger<OnlineMetricService> logger,
            IAppConfiguration appConfiguration)
        {
            _captureService = captureServive;
            _eventAggregator = eventAggregator;
            _overlayEntryCore = oerlayEntryCore;
            _pmdService = pmdService;
            _logger = logger;
            _appConfiguration = appConfiguration;

            _frametimeStatisticProvider = frametimeStatisticProvider;

            SubscribeToUpdateSession();
            ConnectOnlineMetricDataStream();
            ResetMetrics();
        }

        private void SubscribeToUpdateSession()
        {
            _eventAggregator
                .GetEvent<PubSubEvent<ViewMessages.CurrentProcessToCapture>>()
                .Subscribe(msg =>
                {
                    lock (_currentProcessLock)
                    {
                        if (_currentProcess == null
                        || _currentProcess != msg.Process)
                        {
                            ResetMetrics();
                        }

                        _currentProcess = msg.Process;
                        _currentProcessId = msg.ProcessId;
                    }
                });
        }

        private void ConnectOnlineMetricDataStream()
        {
            _captureService
                .FrameDataStream
                .Skip(1)
                .ObserveOn(new EventLoopScheduler())
                .Where(x => EvaluateRealtimeMetrics())
                .Subscribe(UpdateOnlineMetrics);

            _pmdService.PmdChannelStream
                .ObserveOn(new EventLoopScheduler())
                .Where(_ => EvaluatePmdMetrics())
                .Buffer(TimeSpan.FromMilliseconds(50))
                .Subscribe(metricsData => UpdatePmdMetrics(metricsData));
        }

        private bool EvaluateRealtimeMetrics()
        {
            try
            {
                return _overlayEntryCore.RealtimeMetricEntryDict["OnlineAverage"].ShowOnOverlay
                    || _overlayEntryCore.RealtimeMetricEntryDict["OnlineP1"].ShowOnOverlay
                    || _overlayEntryCore.RealtimeMetricEntryDict["OnlineP0dot2"].ShowOnOverlay
					|| _overlayEntryCore.RealtimeMetricEntryDict["Online1PercentLow"].ShowOnOverlay
					|| _overlayEntryCore.RealtimeMetricEntryDict["Online0dot2PercentLow"].ShowOnOverlay
					|| _overlayEntryCore.RealtimeMetricEntryDict["OnlineStutteringPercentage"].ShowOnOverlay
                    || _overlayEntryCore.RealtimeMetricEntryDict["OnlineGpuActiveTimeAverage"].ShowOnOverlay
                    || _overlayEntryCore.RealtimeMetricEntryDict["OnlineCpuActiveTimeAverage"].ShowOnOverlay
                    || _overlayEntryCore.RealtimeMetricEntryDict["OnlineFrameTimeAverage"].ShowOnOverlay
                    || _overlayEntryCore.RealtimeMetricEntryDict["OnlineGpuActiveTimePercentageDeviation"].ShowOnOverlay;
            }
            catch { return false; }
        }

        private bool EvaluatePmdMetrics()
        {
            try
            {
                return _overlayEntryCore.RealtimeMetricEntryDict["PmdGpuPowerCurrent"].ShowOnOverlay
                    || _overlayEntryCore.RealtimeMetricEntryDict["PmdCpuPowerCurrent"].ShowOnOverlay
                    || _overlayEntryCore.RealtimeMetricEntryDict["PmdSystemPowerCurrent"].ShowOnOverlay;
            }
            catch { return false; }
        }

        private void UpdateOnlineMetrics(string[] lineSplit)
        {
            string process;
            try
            {
                process = lineSplit[PresentMonCaptureService.ApplicationName_INDEX].Replace(".exe", "");
            }
            catch { return; }

            lock (_currentProcessLock)
            {
                if (process != _currentProcess)
                    return;
            }

            if (!int.TryParse(lineSplit[PresentMonCaptureService.ProcessID_INDEX], out int processId))
            {
                ResetMetrics();
                return;
            }

            lock (_currentProcessLock)
            {
                if (_currentProcessId != processId)
                    return;
            }

            if (!double.TryParse(lineSplit[PresentMonCaptureService.TimeInSeconds_INDEX], NumberStyles.Any, CultureInfo.InvariantCulture, out double startTime))
            {
                ResetMetrics();
                return;
            }

            // Convert start time to seconds
            startTime *= 1E-03;

            if (!double.TryParse(lineSplit[PresentMonCaptureService.MsBetweenPresents_INDEX], NumberStyles.Any, CultureInfo.InvariantCulture, out double frameTime))
            {
                ResetMetrics();
                return;
            }

			if (!double.TryParse(lineSplit[PresentMonCaptureService.GpuBusy_INDEX], NumberStyles.Any, CultureInfo.InvariantCulture, out double gpuActiveTime))
			{
				ResetMetrics();
				return;
			}

            if (!double.TryParse(lineSplit[PresentMonCaptureService.CpuBusy_INDEX], NumberStyles.Any, CultureInfo.InvariantCulture, out double cpuActiveTime))
            {
                ResetMetrics();
                return;
            }

            // it makes no sense to calculate fps metrics with
            // frame times above the stuttering threshold
            // filtering high frame times caused by focus lost for example
            if (frameTime > STUTTERING_THRESHOLD * 1E03 && !_overlayEntryCore.RealtimeMetricEntryDict["OnlineStutteringPercentage"].ShowOnOverlay)
                return;

            try
            {
                lock (_lockRealtimeMetric)
                {
                    // n sceconds window
                    _frametimesRealtimeSeconds.Add(frameTime);
					_gpuActiveTimesRealtimeSeconds.Add(gpuActiveTime);
                    _cpuActiveTimesRealtimeSeconds.Add(cpuActiveTime);
                    _measuretimesRealtimeSeconds.Add(startTime);

					if (startTime - _measuretimesRealtimeSeconds.First() > MetricInterval)
                    {
                        int position = 0;
                        while (position < _measuretimesRealtimeSeconds.Count &&
                            startTime - _measuretimesRealtimeSeconds[position] > MetricInterval)
                            position++;

                        if (position > 0)
                        {
                            _frametimesRealtimeSeconds.RemoveRange(0, position);
							_gpuActiveTimesRealtimeSeconds.RemoveRange(0, position);
                            _cpuActiveTimesRealtimeSeconds.RemoveRange(0, position);
                            _measuretimesRealtimeSeconds.RemoveRange(0, position);
						}
                    }
                }

                lock (_lock5SecondsMetric)
                {
                    // 5 sceconds window
                    _measuretimes5Seconds.Add(startTime);
                    _frametimes5Seconds.Add(frameTime);

                    if (startTime - _measuretimes5Seconds.First() > _maxOnlineStutteringIntervalLength)
                    {
                        int position = 0;
                        while (position < _measuretimes5Seconds.Count &&
                            startTime - _measuretimes5Seconds[position] > _maxOnlineStutteringIntervalLength)
                            position++;

                        if (position > 0)
                        {
                            _frametimes5Seconds.RemoveRange(0, position);
                            _measuretimes5Seconds.RemoveRange(0, position);
                        }
                    }
                }
            }
            catch { ResetMetrics(); }
        }

        private void UpdatePmdMetrics(IList<PmdChannel[]> metricsData)
        {
            lock (_lockPmdMetrics)
            {
                _channelDataBuffer.AddRange(metricsData);
            }
        }

        private void ResetMetrics()
        {
			lock (_lockRealtimeMetric)
			{
				int capacity = (int)(LIST_CAPACITY * MetricInterval / 20d);

				_frametimesRealtimeSeconds = new List<double>(capacity);
				_measuretimesRealtimeSeconds = new List<double>(capacity);
				_gpuActiveTimesRealtimeSeconds = new List<double>(capacity);
                _cpuActiveTimesRealtimeSeconds = new List<double>(capacity);

            }
		}

        public double GetOnlineFpsMetricValue(EMetric metric)
        {
            lock (_lockRealtimeMetric)
            {
                return _frametimeStatisticProvider
                    .GetFpsMetricValue(_frametimesRealtimeSeconds, metric);
            }
        }

        public double GetOnlineFrameTimeMetricValue(EMetric metric)
        {
            lock (_lockRealtimeMetric)
            {
                return _frametimeStatisticProvider
                    .GetFrametimeMetricValue(_frametimesRealtimeSeconds, metric);
            }
        }

        public double GetOnlineGpuActiveTimeMetricValue(EMetric metric)
		{
			lock (_lockRealtimeMetric)
			{
				return _frametimeStatisticProvider
					.GetFrametimeMetricValue(_gpuActiveTimesRealtimeSeconds, metric);
			}
		}

        public double GetOnlineCpuActiveTimeMetricValue(EMetric metric)
        {
            lock (_lockRealtimeMetric)
            {
                return _frametimeStatisticProvider
                    .GetFrametimeMetricValue(_cpuActiveTimesRealtimeSeconds, metric);
            }
        }

        public double GetOnlineGpuActiveTimeDeviationMetricValue()
        {
            lock (_lockRealtimeMetric)
            {
                var frameTimeAverage = _frametimeStatisticProvider
                    .GetFrametimeMetricValue(_frametimesRealtimeSeconds, EMetric.Average);
                var gpuActiveTimeAverage = _frametimeStatisticProvider
                    .GetFrametimeMetricValue(_gpuActiveTimesRealtimeSeconds, EMetric.GpuActiveAverage);

                return Math.Round(Math.Abs((gpuActiveTimeAverage - frameTimeAverage)/ frameTimeAverage * 100));
            }
        }

        public double GetOnlineStutteringPercentageValue()
        {
            lock (_lock5SecondsMetric)
            {
                if (!_frametimes5Seconds.Any())
                    return 0;

                return _frametimeStatisticProvider
                    .GetOnlineStutteringTimePercentage(_frametimes5Seconds, _appConfiguration.StutteringFactor);
            }
        }

        public OnlinePmdMetrics GetPmdMetricsPowerCurrent()
        {
            OnlinePmdMetrics pmdMetrics;

            lock (_lockPmdMetrics)
            {
                pmdMetrics = new OnlinePmdMetrics()
                {
                    GpuPowerCurrent = GetPmdCurrentPowerByIndexGroup(_channelDataBuffer, PmdChannelExtensions.GPUPowerIndexGroup),
                    CpuPowerCurrent = GetPmdCurrentPowerByIndexGroup(_channelDataBuffer, PmdChannelExtensions.EPSPowerIndexGroup),
                    SystemPowerCurrent = GetPmdCurrentPowerByIndexGroup(_channelDataBuffer, PmdChannelExtensions.SystemPowerIndexGroup),
                };

                _channelDataBuffer = new List<PmdChannel[]>(PMD_BUFFER_CAPACITY);
            }

            return pmdMetrics;
        }

        private float GetPmdCurrentPowerByIndexGroup(IList<PmdChannel[]> channelData, int[] indexGroup)
        {
            double sum = 0;

            foreach (var channel in channelData)
            {
                var currentChannlesSumPower = indexGroup.Sum(index => channel[index].Value);
                sum += currentChannlesSumPower;
            }

            return (float)(sum / channelData.Count);
        }

        public void ResetRealtimeMetrics() => ResetMetrics();

        public void SetMetricInterval() => ResetMetrics();
	}
}